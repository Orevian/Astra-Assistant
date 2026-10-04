using Astra.App.Overlays;
using Astra.Core.Assistant;
using Astra.Core.Localization;
using Astra.Core.Settings;
using Astra.Core.Voice;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Astra.App;

public partial class App : Application
{
    public static SettingsStore Store { get; } = new();
    public static MainWindow? MainWindow { get; private set; }
    public static AssistantCore Assistant { get; private set; } = null!;
    internal static OverlayManager Overlays { get; private set; } = null!;
    internal static ConfirmationService Confirm { get; private set; } = null!;
    internal static TrayIcon Tray { get; private set; } = null!;
    internal static GlobalHotkey Hotkey { get; private set; } = null!;
    public static DispatcherQueue UiQueue { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    private static Mutex? _singleInstance;
    private static EventWaitHandle? _showSignal;
    private string? _iconKey;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { File.AppendAllText(Path.Combine(SettingsStore.DataDirectory, "crash.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n"); }
            catch { }
        };
    }

    private static bool HasArg(string name) => Environment.GetCommandLineArgs().Contains(name);

    private static string? ArgValue(string prefix) =>
        Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);

        // One Astra at a time: a second launch just brings the first one forward.
        if (!HasArg("--multi"))
        {
            _singleInstance = new Mutex(true, "Astra.SingleInstance.v1", out var first);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Astra.ShowWindow.v1");
            if (!first)
            {
                _showSignal.Set();
                Environment.Exit(0);
                return;
            }
            var waiter = new Thread(() => { while (true) { _showSignal.WaitOne(); UiQueue?.TryEnqueue(() => ShowMainWindow()); } }) { IsBackground = true };
            waiter.Start();
        }

        UiQueue = DispatcherQueue.GetForCurrentThread();
        Loc.Set(ArgValue("--lang=") ?? Store.Current.General.UiLanguage);
        ApplyAccent(Store.Current.Appearance.AccentColor);

        Assistant = new AssistantCore(Store);
        Confirm = new ConfirmationService(UiQueue);
        Assistant.Runtime.Tools.Confirmation = Confirm;
        Assistant.VoiceInterceptor = Confirm.TryAnswerByVoice;
        Overlays = new OverlayManager(Assistant, UiQueue, Store);

        Tray = new TrayIcon
        {
            IsListening = () => Assistant.Voice.IsRunning,
            IsPaused = () => Store.Current.Voice.Paused,
        };
        Tray.OpenRequested += () => UiQueue.TryEnqueue(() => ShowMainWindow());
        Tray.SettingsRequested += () => UiQueue.TryEnqueue(() => ShowMainWindow("general"));
        Tray.PermissionsRequested += () => UiQueue.TryEnqueue(() => ShowMainWindow("security"));
        Tray.ExitRequested += () => UiQueue.TryEnqueue(Quit);
        Tray.ToggleListeningRequested += () => UiQueue.TryEnqueue(() =>
        {
            if (Assistant.Voice.IsRunning) Assistant.Voice.Stop();
            else
            {
                Store.Current.Voice.Paused = false;
                Store.Commit();
                if (Assistant.Whisper.IsReady) Assistant.Voice.Start(); else ShowMainWindow("voice");
            }
        });
        Tray.TogglePauseRequested += () => UiQueue.TryEnqueue(() => Assistant.Voice.Pause(!Store.Current.Voice.Paused));
        Tray.Show(Store.Current.Appearance);

        Hotkey = new GlobalHotkey();
        Hotkey.Pressed += () => UiQueue.TryEnqueue(() => { if (!Store.Current.Voice.Paused) Assistant.Voice.BeginPushToTalk(); });
        Hotkey.Released += () => UiQueue.TryEnqueue(() => Assistant.Voice.EndPushToTalk());
        ApplyHotkey();

        _iconKey = IconKey(Store.Current.Appearance);
        Store.Changed += s => UiQueue.TryEnqueue(() => OnSettingsChanged(s));

        // Developer switch for automated checks: enables the optional overlays and skips the wizard for this session only.
        if (HasArg("--demo-all")) { Store.Current.Appearance.SpeechBubbleEnabled = true; Store.Current.Appearance.VisualizerEnabled = true; Store.Current.General.FirstRunCompleted = true; }
        MainWindow = new MainWindow();
        var startHidden = (HasArg("--minimized") || Store.Current.General.StartMinimized) && Store.Current.General.FirstRunCompleted;
        if (!startHidden) MainWindow.Activate();

        if (Store.Current.Index.Completed && Store.Current.Index.WatchChanges) Assistant.Runtime.Index.Watcher.Start();
        Assistant.StartVoice();

        // Developer switches used for automated checks; harmless otherwise.
        if (ArgValue("--ai=") is { } ai && ai.Split(':', 2) is { Length: 2 } parts) { Store.Current.Ai.ProviderId = parts[0]; Store.Current.Ai.Model = parts[1]; }
        if (HasArg("--scan")) _ = ScanController.Instance.StartAsync();
        if (HasArg("--preview")) UiQueue.TryEnqueue(() => Overlays.Preview());
        if (ArgValue("--run=") is { } cmd) _ = Task.Run(async () => { await Task.Delay(3000); await Assistant.RunTextAsync(cmd); });
    }

    private static string IconKey(AppearanceSettings a) => $"{a.IconId}|{a.CustomIconPath}|{a.AccentColor}";

    private void OnSettingsChanged(AstraSettings s)
    {
        ApplyAccent(s.Appearance.AccentColor);
        var key = IconKey(s.Appearance);
        if (key != _iconKey) { _iconKey = key; Tray.UpdateIcon(s.Appearance); MainWindow?.RefreshIcon(); }
    }

    public static void ApplyHotkey()
    {
        var combo = Store.Current.Voice.PushToTalkKey;
        if (!Hotkey.SetCombo(combo)) Hotkey.SetCombo("Ctrl+Alt+Space");
    }

    public static void ShowMainWindow(string? page = null)
    {
        if (MainWindow is null) return;
        MainWindow.ShowAndActivate();
        if (page is not null) MainWindow.NavigateTo(page);
    }

    public static void Quit()
    {
        if (IsExiting) return;
        IsExiting = true;
        try { Store.SaveNow(); } catch { }
        try { Hotkey.Dispose(); } catch { }
        try { Tray.Dispose(); } catch { }
        try { Assistant.Dispose(); } catch { }
        Environment.Exit(0);
    }

    /// <summary>Overrides the system accent so toggles, sliders and buttons pick up the user's color.</summary>
    public static void ApplyAccent(string hex)
    {
        if (!TryParseColor(hex, out var c)) return;
        var r = Application.Current.Resources;
        r["SystemAccentColor"] = c;
        r["SystemAccentColorDark1"] = Mix(c, Colors.Black, 0.15);
        r["SystemAccentColorDark2"] = Mix(c, Colors.Black, 0.30);
        r["SystemAccentColorDark3"] = Mix(c, Colors.Black, 0.45);
        r["SystemAccentColorLight1"] = Mix(c, Colors.White, 0.20);
        r["SystemAccentColorLight2"] = Mix(c, Colors.White, 0.40);
        r["SystemAccentColorLight3"] = Mix(c, Colors.White, 0.60);
    }

    public static bool TryParseColor(string hex, out Color c)
    {
        c = default;
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        c = Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
