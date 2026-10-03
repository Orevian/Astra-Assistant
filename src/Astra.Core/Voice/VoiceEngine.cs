using System.Text.RegularExpressions;
using System.Threading.Channels;
using Astra.Core.Index;
using Astra.Core.Settings;

namespace Astra.Core.Voice;

public enum VoiceState { Off, Paused, WaitingForWakeWord, Listening, Transcribing, Muted, Error }

public sealed record VoiceCommand(string Text, string Language, bool ViaPushToTalk);

/// <summary>
/// Microphone → voice-activity detection → local Whisper → wake word → command.
/// Idle cost is just an energy check per 20 ms frame; Whisper only runs on actual speech.
/// </summary>
public sealed class VoiceEngine : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly MicrophoneCapture _mic = new();
    private readonly WhisperService _whisper;
    private readonly Channel<(float[] Audio, bool Ptt)> _segments = Channel.CreateBounded<(float[], bool)>(4);
    private CancellationTokenSource? _cts;
    private Task? _worker;

    // VAD state
    private readonly List<float[]> _preRoll = new();
    private List<float>? _utterance;
    private int _voiced, _silent;
    private double _noise = 0.004;
    private bool _pttActive;
    private bool _suppressed;               // true while Astra itself is speaking
    private DateTime _awaitCommandUntil;    // after "Astra" alone, the next utterance is the command

    public VoiceState State { get; private set; } = VoiceState.Off;
    public string? LastError { get; private set; }

    /// <summary>True only when Astra was actually addressed: push-to-talk held, or the wake word was just heard and a command is awaited.
    /// Ordinary speech in the room is never "listening" as far as the UI is concerned.</summary>
    public bool IsAddressed => _pttActive || DateTime.UtcNow < _awaitCommandUntil;

    public event Action<VoiceState>? StateChanged;
    public event Action<float>? Level;
    public event Action<string>? WakeWordHeard;      // transcript that triggered the wake word
    public event Action<VoiceCommand>? CommandReady;
    public event Action<string>? Heard;               // every accepted transcript (diagnostics)
    public event Action<string>? Error;

    public VoiceEngine(SettingsStore settings, WhisperService whisper)
    {
        _settings = settings;
        _whisper = whisper;
        _mic.Frame += OnFrame;
        _mic.Error += msg => Fail(msg);
    }

    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (IsRunning) return;
        if (_settings.Current.Voice.Paused) { SetState(VoiceState.Paused); return; }
        if (!_whisper.IsReady) { Fail("The speech model is not downloaded yet (Settings ▸ Voice)."); return; }
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => WorkerAsync(_cts.Token));
        _mic.Start(_settings.Current.Voice.MicrophoneId);
        SetState(_settings.Current.Voice.Mode == ListenMode.WakeWord ? VoiceState.WaitingForWakeWord : VoiceState.WaitingForWakeWord);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _mic.Stop();
        _cts = null;
        _utterance = null;
        SetState(VoiceState.Off);
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Pause(bool paused)
    {
        _settings.Current.Voice.Paused = paused;
        _settings.Commit();
        if (paused) { Stop(); SetState(VoiceState.Paused); } else Start();
    }

    /// <summary>While Astra speaks, the mic is ignored so it never answers itself.</summary>
    public void Suppress(bool suppressed)
    {
        _suppressed = suppressed;
        if (suppressed) { _utterance = null; _voiced = _silent = 0; }
        else if (IsRunning) SetState(VoiceState.WaitingForWakeWord);
    }

    // ---- Push-to-talk ------------------------------------------------------

    public void BeginPushToTalk()
    {
        if (!_whisper.IsReady) { Fail("The speech model is not downloaded yet (Settings ▸ Voice)."); return; }
        if (!IsRunning)
        {
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => WorkerAsync(_cts.Token));
            _mic.Start(_settings.Current.Voice.MicrophoneId);
        }
        _pttActive = true;
        _utterance = new List<float>(_preRoll.SelectMany(f => f));
        SetState(VoiceState.Listening);
    }

    public void EndPushToTalk()
    {
        if (!_pttActive) return;
        _pttActive = false;
        var audio = _utterance?.ToArray();
        _utterance = null;
        if (audio is { Length: > 6400 }) { SetState(VoiceState.Transcribing); _segments.Writer.TryWrite((audio, true)); }
        else SetState(_settings.Current.Voice.Mode == ListenMode.WakeWord && IsRunning ? VoiceState.WaitingForWakeWord : VoiceState.Off);
    }

    // ---- Voice activity detection -----------------------------------------

    private void OnFrame(float[] frame)
    {
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        var rms = Math.Sqrt(sum / frame.Length);
        Level?.Invoke((float)Math.Min(1.0, rms * 8));

        if (_suppressed) return;
        _preRoll.Add(frame);
        if (_preRoll.Count > 15) _preRoll.RemoveAt(0);

        if (_pttActive) { _utterance?.AddRange(frame); if (_utterance?.Count > 16000 * 30) EndPushToTalk(); return; }
        if (_settings.Current.Voice.Mode == ListenMode.PushToTalk) return; // PTT mode: never listens on its own

        var threshold = Math.Max(0.012, _noise * 3.2);
        var speaking = rms > threshold;
        if (_utterance is null)
        {
            if (!speaking) { _noise = _noise * 0.98 + rms * 0.02; _voiced = 0; return; }
            if (++_voiced < 3) return;
            _utterance = new List<float>(_preRoll.SelectMany(f => f));
            _silent = 0;
            SetState(VoiceState.Listening);
            return;
        }

        _utterance.AddRange(frame);
        _silent = speaking ? 0 : _silent + 1;
        var length = _utterance.Count;
        if (_silent >= 36 || length > 16000 * 15) // 720 ms of silence ends the phrase
        {
            var audio = _utterance.ToArray();
            _utterance = null; _voiced = _silent = 0;
            if (audio.Length < 16000 * 0.45) { SetState(VoiceState.WaitingForWakeWord); return; }
            SetState(VoiceState.Transcribing);
            if (!_segments.Writer.TryWrite((audio, false))) SetState(VoiceState.WaitingForWakeWord);
        }
    }

    // ---- Transcription & wake word ----------------------------------------

    private async Task WorkerAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (audio, ptt) in _segments.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var t = await _whisper.TranscribeAsync(audio, _settings.Current.Voice.RecognitionLanguage, ct);
                    if (t is null) { Back(); continue; }
                    Heard?.Invoke(t.Text);
                    Handle(t, ptt);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Fail(ex.Message); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Back() { if (IsRunning && State != VoiceState.Muted) SetState(VoiceState.WaitingForWakeWord); }

    private void Handle(Transcript t, bool ptt)
    {
        if (ptt)
        {
            var (_, rest) = SplitWakeWord(t.Text);
            CommandReady?.Invoke(new VoiceCommand(rest ?? t.Text, t.Language, true));
            Back();
            return;
        }

        var (heard, command) = SplitWakeWord(t.Text);
        if (heard)
        {
            if (!string.IsNullOrWhiteSpace(command)) { CommandReady?.Invoke(new VoiceCommand(command!, t.Language, false)); Back(); }
            else
            {
                _awaitCommandUntil = DateTime.UtcNow.AddSeconds(8); // set before announcing so the UI sees "addressed"
                WakeWordHeard?.Invoke(t.Text);
                SetState(VoiceState.WaitingForWakeWord);
            }
            return;
        }
        if (DateTime.UtcNow < _awaitCommandUntil)
        {
            _awaitCommandUntil = DateTime.MinValue;
            CommandReady?.Invoke(new VoiceCommand(t.Text, t.Language, false));
        }
        Back();
    }

    /// <summary>True when the phrase starts with the wake word (fuzzy: Whisper hears "Astro", "Ostra"…); also returns the text after it.</summary>
    public (bool Heard, string? Command) SplitWakeWord(string text)
    {
        var wake = TextNorm.Normalize(_settings.Current.Voice.WakeWord);
        if (wake.Length == 0) return (false, null);
        var variants = new HashSet<string> { wake };
        if (wake == "astra") foreach (var v in new[] { "astro", "ostra", "estra", "hastra", "asta", "astre", "astrah", "astera" }) variants.Add(v);

        var words = Regex.Matches(text, @"[\p{L}\p{N}]+");
        var take = Math.Min(3, words.Count);
        for (var i = 0; i < take; i++)
        {
            var w = TextNorm.Normalize(words[i].Value);
            var match = variants.Contains(w) || variants.Any(v => Similarity(v, w) >= 0.8);
            if (!match) continue;
            var end = words[i].Index + words[i].Length;
            return (true, text[end..].TrimStart(' ', ',', '.', ':', ';', '!', '?', '-', '…').Trim());
        }
        return (false, null);
    }

    private static double Similarity(string a, string b) =>
        a.Length == 0 || b.Length == 0 ? 0 : 1.0 - (double)TextNorm.Levenshtein(a, b) / Math.Max(a.Length, b.Length);

    private void SetState(VoiceState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    private void Fail(string message)
    {
        LastError = message;
        Error?.Invoke(message);
        SetState(VoiceState.Error);
    }

    public void Dispose()
    {
        Stop();
        _mic.Dispose();
    }
}
