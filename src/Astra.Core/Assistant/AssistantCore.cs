using Astra.Core.Agent;
using Astra.Core.Index;
using Astra.Core.Localization;
using Astra.Core.Settings;
using Astra.Core.Tools;
using Astra.Core.Voice;

namespace Astra.Core.Assistant;

public enum AssistantState { Idle, Listening, Thinking, Executing, Speaking, Error }

public enum StepStatus { Running, Ok, Failed }
public sealed record HudStep(string Text, StepStatus Status);

public enum HudPhase { Hidden, Listening, Thinking, Working, Speaking, Done, Error }

/// <summary>Everything the HUD needs to render, in one immutable snapshot.</summary>
public sealed record HudSnapshot(HudPhase Phase, string Task, string Current, IReadOnlyList<HudStep> Steps, string? Message);

public sealed record ChatLine(bool FromUser, string Text, DateTime Time, bool Failed = false, bool ViaVoice = false);

/// <summary>
/// The assistant's brain stem: wires voice, agent, tools and speech together and publishes state for the UI
/// (HUD, AI cursor, visualizer, speech bubble, tray, notifications).
/// </summary>
public sealed class AssistantCore : IDisposable
{
    public AstraRuntime Runtime { get; }
    public VoiceEngine Voice { get; }
    public WhisperService Whisper { get; }
    public TtsService Tts { get; }

    private CancellationTokenSource? _taskCts;
    private readonly List<HudStep> _steps = new();
    private string _task = "";
    private readonly object _gate = new();

    /// <summary>Lets the UI claim a spoken phrase first (e.g. answering a permission prompt with "yes").</summary>
    public Func<string, bool>? VoiceInterceptor { get; set; }

    public AssistantState State { get; private set; } = AssistantState.Idle;
    public bool IsBusy { get; private set; }
    public List<ChatLine> Transcript { get; } = new();

    public event Action<AssistantState>? StateChanged;
    public event Action<HudSnapshot>? Hud;
    public event Action<string>? Spoken;                       // text Astra says (for the speech bubble)
    public event Action<string, string, bool>? Notify;          // title, message, isError
    public event Action<ChatLine>? ChatAdded;
    public event Action<float>? MicLevel;
    public event Action<float[]>? Spectrum;

    public AssistantCore(SettingsStore settings)
    {
        Runtime = new AstraRuntime(settings);
        Whisper = new WhisperService(settings)
        {
            Vocabulary = () => Runtime.Index.HasIndex ? VocabularyFromIndex() : Array.Empty<string>(),
        };
        Voice = new VoiceEngine(settings, Whisper);
        Tts = new TtsService(settings);

        Voice.StateChanged += OnVoiceState;
        Voice.Level += l => MicLevel?.Invoke(l);
        Voice.WakeWordHeard += heardText =>
        {
            if (IsBusy) return;
            SetState(AssistantState.Listening);
            PublishHud(HudPhase.Listening, "", Loc.T("Listening…"));
            // Nobody followed up with a command: stop looking like we're listening.
            _ = Task.Delay(8500).ContinueWith(_ => { if (!IsBusy && !Voice.IsAddressed) { SetState(AssistantState.Idle); PublishHud(HudPhase.Hidden, "", ""); } });
        };
        Voice.CommandReady += cmd => { if (VoiceInterceptor?.Invoke(cmd.Text) == true) return; _ = RunAsync(cmd.Text, cmd.Language, true); };
        Voice.Error += msg => Notify?.Invoke("Astra", Loc.T(msg), true);
        Tts.Spectrum += b => Spectrum?.Invoke(b);
        Tts.SpeakingChanged += speaking => Voice.Suppress(speaking);

        Runtime.Tools.Event += OnToolEvent;
    }

    private IEnumerable<string> VocabularyFromIndex()
    {
        using var c = Runtime.Index.Database.Open();
        using var cmd = c.CreateCommand();
        // Start Menu / Store apps are what people actually say; games and browsers first. Spoken aliases ("VS Code") bias Whisper too.
        cmd.CommandText = "SELECT name, name_norm FROM applications WHERE source IN ('startmenu','uwp','browser','steam','epic') ORDER BY (kind='browser') DESC, (kind='game') DESC, length(name) LIMIT 45";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read())
        {
            list.Add(r.GetString(0));
            var alias = AppAliases.For(r.GetString(1)).FirstOrDefault(a => a.Contains(' ') || a.Length > 3);
            if (alias is not null) list.Add(alias);
        }
        return list;
    }

    // ---- Lifecycle ---------------------------------------------------------

    public void StartVoice()
    {
        var v = Runtime.Settings.Current.Voice;
        if (v.Paused || v.Mode == ListenMode.PushToTalk) { if (v.Mode == ListenMode.PushToTalk) _ = Whisper.WarmUpAsync(CancellationToken.None); return; }
        if (!Whisper.IsReady) return;
        Voice.Start();
        _ = Whisper.WarmUpAsync(CancellationToken.None);
    }

    public void ApplyVoiceSettings()
    {
        Voice.Stop();
        StartVoice();
    }

    // ---- Running a request -------------------------------------------------

    public Task RunTextAsync(string text) => RunAsync(text, null, false);

    public async Task RunAsync(string text, string? language, bool viaVoice)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (IsBusy) { Notify?.Invoke("Astra", Loc.T("Astra is still working on the previous request."), false); return; }
        IsBusy = true;
        _taskCts = new CancellationTokenSource();
        var ct = _taskCts.Token;
        lock (_gate) { _steps.Clear(); _task = text; }
        AddChat(new ChatLine(true, text, DateTime.Now, ViaVoice: viaVoice));
        SetState(AssistantState.Thinking);
        PublishHud(HudPhase.Thinking, text, Loc.T("Thinking…"));

        AgentResult? result = null;
        try
        {
            result = await Runtime.Agent.RunAsync(text, language, OnAgentEvent, ct);
        }
        catch (OperationCanceledException)
        {
            result = new AgentResult(false, Loc.T("Cancelled."), Loc.Language, false, 0);
        }
        catch (Exception ex)
        {
            result = new AgentResult(false, ex.Message, Loc.Language, false, 0);
        }

        IsBusy = false;
        var ok = result.Ok;
        AddChat(new ChatLine(false, result.Message, DateTime.Now, !ok));
        SetState(ok ? AssistantState.Speaking : AssistantState.Error);
        PublishHud(ok ? HudPhase.Done : HudPhase.Error, text, result.Message, result.Message);
        Notify?.Invoke(ok ? Loc.T("Task complete") : Loc.T("Task failed"), result.Message, !ok);

        var settings = Runtime.Settings.Current;
        if (settings.Voice.SpeakResponses && result.Message.Length > 0)
        {
            Spoken?.Invoke(TtsService.ForSpeech(result.Message));
            try { await Tts.SpeakAsync(result.Message, result.Language, CancellationToken.None); } catch { }
        }
        else Spoken?.Invoke(TtsService.ForSpeech(result.Message));

        await Task.Delay(ok ? 300 : 1200);
        SetState(AssistantState.Idle);
        PublishHud(HudPhase.Hidden, "", "");
        Spoken?.Invoke("");
        _taskCts = null;
    }

    /// <summary>Lets the app raise a notification through the same channel the assistant uses.</summary>
    public void Announce(string title, string message, bool isError = false) => Notify?.Invoke(title, message, isError);

    public void Cancel()
    {
        _taskCts?.Cancel();
        Tts.Stop();
    }

    // ---- Event plumbing ----------------------------------------------------

    private void OnAgentEvent(Agent.AgentEvent e)
    {
        switch (e.Kind)
        {
            case Agent.AgentEventKind.Thinking:
                SetState(AssistantState.Thinking);
                PublishHud(HudPhase.Thinking, _task, Loc.T("Thinking…"));
                break;
        }
    }

    private void OnToolEvent(ToolEvent e)
    {
        lock (_gate)
        {
            if (e.Ok is null)
            {
                _steps.Add(new HudStep(e.Description, StepStatus.Running));
                SetState(AssistantState.Executing);
            }
            else
            {
                var idx = _steps.FindLastIndex(s => s.Status == StepStatus.Running);
                if (idx >= 0) _steps[idx] = new HudStep(e.Ok == true ? e.Description : (e.Message ?? e.Description), e.Ok == true ? StepStatus.Ok : StepStatus.Failed);
            }
        }
        var current = _steps.LastOrDefault(s => s.Status == StepStatus.Running)?.Text ?? Loc.T("Thinking…");
        PublishHud(HudPhase.Working, _task, current);
    }

    private void OnVoiceState(VoiceState s)
    {
        if (IsBusy) return;
        // Voice-activity detection fires on any sound. Only show "listening" when Astra was addressed
        // (wake word heard / push-to-talk); otherwise room chatter would flash the HUD.
        switch (s)
        {
            case VoiceState.Listening when Voice.IsAddressed:
                SetState(AssistantState.Listening);
                PublishHud(HudPhase.Listening, "", Loc.T("Listening…"));
                break;
            case VoiceState.Transcribing when Voice.IsAddressed:
                PublishHud(HudPhase.Thinking, "", Loc.T("Understanding…"));
                break;
            case VoiceState.WaitingForWakeWord or VoiceState.Off or VoiceState.Paused when !Voice.IsAddressed:
                if (State is AssistantState.Listening) SetState(AssistantState.Idle);
                PublishHud(HudPhase.Hidden, "", "");
                break;
        }
    }

    private void PublishHud(HudPhase phase, string task, string current, string? message = null)
    {
        HudStep[] steps;
        lock (_gate) steps = _steps.TakeLast(5).ToArray();
        Hud?.Invoke(new HudSnapshot(phase, task, current, steps, message));
    }

    private void SetState(AssistantState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    private void AddChat(ChatLine line)
    {
        Transcript.Add(line);
        if (Transcript.Count > 200) Transcript.RemoveAt(0);
        ChatAdded?.Invoke(line);
    }

    public void Dispose()
    {
        Cancel();
        Voice.Dispose();
        Tts.Dispose();
        Whisper.Dispose();
        Runtime.Dispose();
    }
}
