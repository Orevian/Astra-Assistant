using Astra.Core.Localization;
using Astra.Core.Security;
using Astra.Core.Settings;
using Astra.Core.Voice;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Astra.App.Pages;

public sealed partial class VoicePage : Page
{
    private readonly VoiceSettings _v = App.Store.Current.Voice;
    private MicrophoneCapture? _micTest;
    private CancellationTokenSource? _downloadCts;
    private bool _loading = true;

    public VoicePage()
    {
        InitializeComponent();

        Bind.Choice(ModeBox, new[] { (Loc.T("Wake word"), ListenMode.WakeWord), (Loc.T("Push-to-talk"), ListenMode.PushToTalk) },
            () => _v.Mode, m => { _v.Mode = m; UpdateModeUi(); RestartVoice(); });
        Bind.Text(WakeBox, () => _v.WakeWord, t => _v.WakeWord = t);
        PttBox.Text = _v.PushToTalkKey;
        Bind.Choice(RecLangBox, new[] { (Loc.T("Auto (Turkish / English)"), "auto"), ("Türkçe", "tr-TR"), ("English", "en-US") },
            () => _v.RecognitionLanguage, t => _v.RecognitionLanguage = t);

        Bind.Choice(ModelBox, WhisperModels.Available.Select(m => ($"{Loc.T(m.Name)} · {m.SizeMb} MB · {Loc.T(m.Note)}", m.Id)).ToList(),
            () => _v.WhisperModel, id => { _v.WhisperModel = id; UpdateModelUi(); RestartVoice(); });

        Bind.Toggle(SpeakToggle, () => _v.SpeakResponses, t => _v.SpeakResponses = t);
        Bind.Choice(TtsBox, new[] { (Loc.T("Windows voices (local)"), "windows"), (Loc.T("OpenAI voices (external)"), "openai") },
            () => _v.TtsProvider, t => { _v.TtsProvider = t; _v.TtsVoice = null; LoadVoices(); });
        Bind.Range(SpeedSlider, () => _v.TtsSpeed, t => _v.TtsSpeed = Math.Round(t, 2), SpeedLabel, "{0:0.00}×");
        Bind.Range(VolumeSlider, () => _v.TtsVolume, t => _v.TtsVolume = (int)t, VolumeLabel, "{0:0}%");
        Bind.Choice(LangBox, new[] { (Loc.T("Detect automatically"), "auto"), ("English", "en-US"), ("Türkçe", "tr-TR") },
            () => _v.ResponseLanguage, t => _v.ResponseLanguage = t);

        UpdateModeUi();
        UpdateModelUi();
        LoadVoices();
        LoadMicrophones();
        _loading = false;
        Unloaded += (_, _) => { _micTest?.Dispose(); _downloadCts?.Cancel(); };
    }

    private void UpdateModeUi() => WakeCard.Visibility = _v.Mode == ListenMode.WakeWord ? Visibility.Visible : Visibility.Collapsed;

    private static void RestartVoice() => App.Assistant.ApplyVoiceSettings();

    // ---- Microphone -----------------------------------------------------------

    private void LoadMicrophones()
    {
        var devices = MicrophoneCapture.Devices();
        MicBox.Items.Add(new ComboBoxItem { Content = Loc.T("Windows default"), Tag = "" });
        foreach (var d in devices) MicBox.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        var idx = MicBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == (_v.MicrophoneId ?? ""));
        MicBox.SelectedIndex = Math.Max(0, idx);
        MicBox.SelectionChanged += (_, _) =>
        {
            if (_loading || MicBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            _v.MicrophoneId = id.Length == 0 ? null : id;
            App.Store.Commit();
            RestartVoice();
        };
    }

    private void MicTest_Click(object sender, RoutedEventArgs e)
    {
        if (_micTest is not null) { StopMicTest(); return; }
        _micTest = new MicrophoneCapture();
        _micTest.Frame += frame =>
        {
            double sum = 0;
            foreach (var s in frame) sum += s * s;
            var level = Math.Min(1.0, Math.Sqrt(sum / frame.Length) * 9);
            DispatcherQueue.TryEnqueue(() => MicLevel.Value = level);
        };
        _micTest.Error += msg => DispatcherQueue.TryEnqueue(() => { StopMicTest(); ShowModelBar(InfoBarSeverity.Error, Loc.F("Could not open the microphone: {0}", msg)); });
        _micTest.Start(_v.MicrophoneId);
        MicTestBtn.Content = Loc.T("Stop");
    }

    private void StopMicTest()
    {
        _micTest?.Dispose();
        _micTest = null;
        MicLevel.Value = 0;
        MicTestBtn.Content = Loc.T("Test");
    }

    // ---- Push-to-talk key capture ---------------------------------------------

    private async void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        var preview = new TextBlock { Text = Loc.T("Press the keys you want to hold…"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        var captured = "";
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = Loc.T("Push-to-talk hotkey"), Content = preview,
            PrimaryButtonText = Loc.T("Use this"), CloseButtonText = Loc.T("Cancel"), IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary,
        };
        dlg.PreviewKeyDown += (_, args) =>
        {
            var name = KeyName(args.Key);
            if (name is null) { args.Handled = true; return; }
            var parts = new List<string>();
            if (Down(VirtualKey.Control)) parts.Add("Ctrl");
            if (Down(VirtualKey.Menu)) parts.Add("Alt");
            if (Down(VirtualKey.Shift)) parts.Add("Shift");
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) parts.Add("Win");
            if (name != "") parts.Add(name);
            if (name == "") { preview.Text = string.Join("+", parts) + "+…"; args.Handled = true; return; }
            captured = string.Join("+", parts);
            preview.Text = captured;
            dlg.IsPrimaryButtonEnabled = true;
            args.Handled = true;
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary || captured.Length == 0) return;
        if (!Core.Control.InputController.TryParseChord(captured, out _, out _)) return;
        _v.PushToTalkKey = captured;
        PttBox.Text = captured;
        App.Store.Commit();
        App.ApplyHotkey();
    }

    private static bool Down(VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>"" for a pure modifier (keep waiting), null for keys that can't be used, otherwise a name the hotkey parser understands.</summary>
    private static string? KeyName(VirtualKey k)
    {
        if (k is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftWindows or VirtualKey.RightWindows) return "";
        if (k >= VirtualKey.A && k <= VirtualKey.Z) return k.ToString();
        if (k >= VirtualKey.Number0 && k <= VirtualKey.Number9) return ((int)k - (int)VirtualKey.Number0).ToString();
        if (k >= VirtualKey.F1 && k <= VirtualKey.F24) return k.ToString();
        return k switch
        {
            VirtualKey.Space => "Space", VirtualKey.Enter => "Enter", VirtualKey.Tab => "Tab", VirtualKey.Escape => null,
            VirtualKey.Back => "Backspace", VirtualKey.Delete => "Delete", VirtualKey.Insert => "Insert", VirtualKey.Home => "Home", VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp", VirtualKey.PageDown => "PageDown", VirtualKey.Up => "Up", VirtualKey.Down => "Down", VirtualKey.Left => "Left", VirtualKey.Right => "Right",
            _ => null,
        };
    }

    // ---- Whisper model ----------------------------------------------------------

    private void UpdateModelUi()
    {
        var info = WhisperModels.Available.First(m => m.Id == _v.WhisperModel);
        var installed = WhisperModels.IsInstalled(info.Id);
        DownloadBtn.Content = installed ? Loc.T("Installed ✓") : Loc.F("Download ({0} MB)", info.SizeMb);
        DownloadBtn.IsEnabled = !installed && _downloadCts is null;
        if (installed) ModelBar.IsOpen = false;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var id = _v.WhisperModel;
        _downloadCts = new CancellationTokenSource();
        DownloadBtn.IsEnabled = false;
        DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.Value = 0;
        ModelBar.IsOpen = false;
        try
        {
            await WhisperModels.DownloadAsync(id, new Progress<double>(p => DownloadBar.Value = p), _downloadCts.Token);
            ShowModelBar(InfoBarSeverity.Success, Loc.T("Speech model ready. Astra can hear you now."));
            RestartVoice();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowModelBar(InfoBarSeverity.Error, Loc.F("Download failed: {0}", ex.Message));
        }
        finally
        {
            _downloadCts = null;
            DownloadBar.Visibility = Visibility.Collapsed;
            UpdateModelUi();
        }
    }

    private void ShowModelBar(InfoBarSeverity sev, string msg)
    {
        ModelBar.Severity = sev;
        ModelBar.Title = sev == InfoBarSeverity.Success ? Loc.T("Success") : Loc.T("Problem");
        ModelBar.Message = msg;
        ModelBar.IsOpen = true;
    }

    // ---- Voices & test -----------------------------------------------------------

    private void LoadVoices()
    {
        var wasLoading = _loading;
        _loading = true;
        VoiceBox.Items.Clear();
        var voices = TtsService.Voices(_v.TtsProvider);
        foreach (var v in voices)
            VoiceBox.Items.Add(new ComboBoxItem { Content = v.Provider == "openai" ? v.Name : $"{v.Name} ({v.Language})", Tag = v.Id });
        var idx = voices.ToList().FindIndex(v => v.Id == _v.TtsVoice);
        VoiceBox.SelectedIndex = idx >= 0 ? idx : (voices.Count > 0 ? 0 : -1);
        if (idx < 0 && voices.Count > 0 && _v.TtsVoice is null) _v.TtsVoice = voices[0].Id;
        _loading = wasLoading;
        VoiceBox.SelectionChanged -= VoiceChanged;
        VoiceBox.SelectionChanged += VoiceChanged;
    }

    private void VoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VoiceBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _v.TtsVoice = id;
        App.Store.Commit();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        try
        {
            var name = App.Store.Current.General.AssistantName;
            var lang = _v.ResponseLanguage == "auto" ? (Loc.IsTurkish ? "tr" : "en") : _v.ResponseLanguage[..2];
            var text = lang == "tr" ? $"Merhaba, ben {name}. Sana nasıl yardımcı olabilirim?" : $"Hello, I'm {name}. How can I help you?";
            await App.Assistant.Tts.SpeakAsync(text, lang);
        }
        catch { /* nothing to play */ }
        finally { TestBtn.IsEnabled = true; }
    }
}
