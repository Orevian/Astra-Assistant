using Astra.Core.Assistant;
using Astra.Core.Localization;
using Astra.Core.Settings;
using Microsoft.UI.Dispatching;

namespace Astra.App.Overlays;

/// <summary>Owns every overlay window and feeds them from the assistant's events (all marshalled to the UI thread).</summary>
internal sealed class OverlayManager
{
    private readonly AssistantCore _core;
    private readonly DispatcherQueue _ui;
    private readonly SettingsStore _store;

    public HudWindow Hud { get; }
    public CursorWindow Cursor { get; }
    public SpectrumWindow Spectrum { get; }
    public BubbleWindow Bubble { get; }
    public NotificationWindow Notifications { get; }

    private string _bubbleText = "";
    private DateTime _lastSpectrumUi = DateTime.MinValue;

    public OverlayManager(AssistantCore core, DispatcherQueue ui, SettingsStore store)
    {
        _core = core; _ui = ui; _store = store;
        Hud = new HudWindow();
        Cursor = new CursorWindow();
        Spectrum = new SpectrumWindow();
        Bubble = new BubbleWindow();
        Notifications = new NotificationWindow();

        core.Hud += snap => _ui.TryEnqueue(() => { Hud.Apply(snap); RefreshOffsets(); });
        core.StateChanged += s => _ui.TryEnqueue(() => Cursor.SetState(s));
        core.MicLevel += l => _ui.TryEnqueue(() => Hud.SetLevel(l));
        core.Spectrum += bands =>
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSpectrumUi).TotalMilliseconds < 22) return; // ~45 fps is plenty for the eye
            _lastSpectrumUi = now;
            _ui.TryEnqueue(() => { Spectrum.Render(bands); Cursor.SetLevel(bands.Take(16).Average()); });
        };
        core.Tts.SpeakingChanged += speaking => _ui.TryEnqueue(() => Spectrum.SetSpeaking(speaking));
        core.Spoken += text => _ui.TryEnqueue(() =>
        {
            _bubbleText = text;
            if (string.IsNullOrWhiteSpace(text)) Bubble.Hide();
            else { Bubble.Show(text, BubbleOffset()); RefreshOffsets(); }
        });
        core.Notify += (title, message, error) => _ui.TryEnqueue(() => { RefreshOffsets(); Notifications.Push(title, message, error); });
        store.Changed += _ => _ui.TryEnqueue(Refresh);
    }

    // Stack the bubble and notifications beside the HUD instead of on top of it when they share a corner.
    private double HudHeight => Hud.IsShown ? Hud.HeightDip + 8 : 0;

    private double BubbleOffset()
    {
        var a = _store.Current.Appearance;
        return a.BubblePosition == a.HudPosition ? HudHeight : 0;
    }

    private void RefreshOffsets()
    {
        var a = _store.Current.Appearance;
        var bubble = Bubble.IsShown && a.BubblePosition == a.NotificationPosition ? Bubble.HeightDip + 8 : 0;
        Notifications.HudOffsetDip = HudHeight + bubble;
        Notifications.Relayout();
    }

    private void Refresh()
    {
        if (Hud.IsShown) Hud.Reposition();
        Spectrum.Configure(force: true);
        Cursor.Refresh();
        if (Bubble.IsShown) Bubble.Show(_bubbleText, BubbleOffset());
    }

    /// <summary>Shows every enabled overlay with sample content so the user can judge their settings.</summary>
    public async void Preview()
    {
        var a = _store.Current.Appearance;
        var steps = new[]
        {
            new HudStep(Loc.T("Browser opened"), StepStatus.Ok),
            new HudStep(Loc.T("Finding “Misery” on YouTube…"), StepStatus.Running),
        };
        Hud.Apply(new HudSnapshot(HudPhase.Working, Loc.T("Open the browser and play Misery"), Loc.T("Finding “Misery” on YouTube…"), steps, null));
        Cursor.SetState(AssistantState.Executing);
        if (a.SpeechBubbleEnabled) Bubble.Show(Loc.T("This is how Astra's speech bubble looks."), BubbleOffset());
        RefreshOffsets();
        Notifications.Push(Loc.T("Preview"), Loc.T("This is how notifications look."), false);
        Spectrum.SetSpeaking(true);

        var t0 = Environment.TickCount64;
        var bands = new float[32];
        while (Environment.TickCount64 - t0 < 3500)
        {
            var t = (Environment.TickCount64 - t0) / 1000.0;
            for (var i = 0; i < 32; i++)
                bands[i] = (float)Math.Clamp(0.1 + 0.55 * Math.Abs(Math.Sin(t * 5 + i * 0.45)) * Math.Exp(-i / 28.0) * (0.6 + 0.4 * Math.Sin(t * 2.1)), 0, 1);
            Spectrum.Render(bands);
            Cursor.SetLevel(bands.Take(16).Average());
            await Task.Delay(25);
        }
        Spectrum.SetSpeaking(false);
        Hud.Apply(new HudSnapshot(HudPhase.Hidden, "", "", Array.Empty<HudStep>(), null));
        Cursor.SetState(AssistantState.Idle);
        Bubble.Hide();
    }
}
