using Astra.App.Overlays;
using Astra.Core.Assistant;
using Astra.Core.Localization;
using Astra.Core.Providers;
using Astra.Core.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Astra.App.Pages;

/// <summary>The assistant console: status, conversation, and a text box for when you'd rather type than talk.</summary>
public sealed partial class HomePage : Page
{
    private readonly AssistantCore _core = App.Assistant;

    public HomePage()
    {
        InitializeComponent();
        var s = App.Store.Current;

        TitleText.Text = s.General.AssistantName;
        Input.PlaceholderText = Loc.T("Type a command…");
        HintText.Text = Loc.F("Say “{0}” followed by a command, hold {1} to talk, or type below.", s.Voice.WakeWord, s.Voice.PushToTalkKey);

        BuildExamples();
        BuildBanners();
        foreach (var line in _core.Transcript) AddBubble(line, scroll: false);
        UpdateEmptyState();
        UpdateStatus(_core.State);
        UpdateListenButton();

        MicBtn.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { if (!_core.IsBusy) _core.Voice.BeginPushToTalk(); }), true);
        MicBtn.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => _core.Voice.EndPushToTalk()), true);
        MicBtn.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => _core.Voice.EndPushToTalk()), true);

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        _core.ChatAdded += OnChat;
        _core.StateChanged += OnState;
        _core.MicLevel += OnLevel;
        _core.Voice.StateChanged += OnVoiceState;
        UpdateListenButton(); // the voice engine may have started before this page was shown
    }

    private void Detach()
    {
        _core.ChatAdded -= OnChat;
        _core.StateChanged -= OnState;
        _core.MicLevel -= OnLevel;
        _core.Voice.StateChanged -= OnVoiceState;
    }

    private void OnChat(ChatLine line) => DispatcherQueue.TryEnqueue(() => { AddBubble(line, true); UpdateEmptyState(); });
    private void OnState(AssistantState st) => DispatcherQueue.TryEnqueue(() => UpdateStatus(st));
    private void OnVoiceState(Core.Voice.VoiceState _) => DispatcherQueue.TryEnqueue(UpdateListenButton);
    private void OnLevel(float level) => DispatcherQueue.TryEnqueue(() => { MicMeter.Value = level; });

    // ---- Status ---------------------------------------------------------------

    private void UpdateStatus(AssistantState st)
    {
        var a = App.Store.Current.Appearance;
        OrbHost.Children.Clear();
        OrbHost.Children.Add(IconFactory.Create(a, 44));
        StatusText.Text = st switch
        {
            AssistantState.Listening => Loc.T("Listening…"),
            AssistantState.Thinking => Loc.T("Thinking…"),
            AssistantState.Executing => Loc.T("Working…"),
            AssistantState.Speaking => Loc.T("Speaking…"),
            AssistantState.Error => Loc.T("Something went wrong"),
            _ => _core.Voice.State switch
            {
                Core.Voice.VoiceState.Paused => Loc.T("Paused"),
                Core.Voice.VoiceState.WaitingForWakeWord => Loc.F("Ready — say “{0}”", App.Store.Current.Voice.WakeWord),
                Core.Voice.VoiceState.Error => _core.Voice.LastError is { } e ? Loc.T(e) : Loc.T("Something went wrong"),
                _ => Loc.T("Ready"),
            },
        };
        MicMeter.Visibility = _core.Voice.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        SendIcon.Glyph = _core.IsBusy ? "" : "";
    }

    private void UpdateListenButton()
    {
        var running = _core.Voice.IsRunning;
        ListenBtn.IsChecked = running;
        ListenLabel.Text = running ? Loc.T("Listening") : (App.Store.Current.Voice.Paused ? Loc.T("Paused") : Loc.T("Start listening"));
        MicMeter.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus(_core.State);
    }

    private void Listen_Click(object sender, RoutedEventArgs e)
    {
        if (_core.Voice.IsRunning) _core.Voice.Stop();
        else
        {
            if (!_core.Whisper.IsReady) { App.MainWindow?.NavigateTo("voice"); UpdateListenButton(); return; }
            App.Store.Current.Voice.Paused = false;
            App.Store.Commit();
            _core.Voice.Start();
        }
        UpdateListenButton();
    }

    // ---- Conversation -----------------------------------------------------------

    private void AddBubble(ChatLine line, bool scroll)
    {
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var text = new TextBlock
        {
            Text = line.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Foreground = line.FromUser ? new SolidColorBrush(Microsoft.UI.Colors.White) : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (line.ViaVoice) meta.Children.Add(new FontIcon { Glyph = "", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] });
        meta.Children.Add(new TextBlock { Text = line.Time.ToString("HH:mm"), FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] });

        var inner = new StackPanel { Spacing = 3 };
        inner.Children.Add(text);
        var border = new Border
        {
            Child = inner, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(14), MaxWidth = 640,
            Background = line.FromUser ? accent
                : line.Failed ? new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0xFF, 0x5C, 0x5C))
                : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(line.FromUser ? 0 : 1),
        };
        var holder = new StackPanel { HorizontalAlignment = line.FromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left, Spacing = 2 };
        holder.Children.Add(border);
        meta.HorizontalAlignment = line.FromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        holder.Children.Add(meta);
        Messages.Children.Add(holder);
        if (scroll)
        {
            ChatScroll.UpdateLayout();
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null);
        }
    }

    private void UpdateEmptyState() => EmptyState.Visibility = Messages.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void BuildExamples()
    {
        foreach (var ex in new[]
        {
            Loc.T("Open Notepad"),
            Loc.T("Play Misery on YouTube"),
            Loc.T("Find my PDF files in Downloads"),
            Loc.T("What can you do?"),
        })
        {
            var b = new Button { Content = "“" + ex + "”", HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 260, Padding = new Thickness(16, 8, 16, 8) };
            b.Click += (_, _) => { Input.Text = ""; _ = _core.RunTextAsync(ex); };
            Examples.Items.Add(b);
        }
    }

    // ---- Setup banners ----------------------------------------------------------

    private void BuildBanners()
    {
        Banners.Children.Clear();
        var s = App.Store.Current;
        var provider = ProviderCatalog.Get(s.Ai.ProviderId);
        var keyOk = !provider.RequiresApiKey || CredentialStore.Exists(CredentialStore.ProviderKeyName(provider.Id));
        if (!keyOk || s.Ai.Model.Length == 0)
            Banners.Children.Add(Banner(Loc.T("Choose an AI model"), keyOk ? Loc.T("Pick a model so Astra can handle requests that need reasoning.") : Loc.F("Add your {0} API key, or switch to Ollama to run locally.", provider.DisplayName), "ai"));
        if (!_core.Whisper.IsReady)
            Banners.Children.Add(Banner(Loc.T("Download the speech model"), Loc.T("Astra listens with a local model. It is downloaded once and nothing leaves your PC."), "voice"));
        if (!s.Index.Completed)
            Banners.Children.Add(Banner(Loc.T("Scan Windows"), Loc.T("Let Astra index your apps and files so it can open and find them instantly."), "index"));
    }

    private InfoBar Banner(string title, string message, string page)
    {
        var bar = new InfoBar { Title = title, Message = message, IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational };
        var btn = new Button { Content = Loc.T("Set up") };
        btn.Click += (_, _) => App.MainWindow?.NavigateTo(page);
        bar.ActionButton = btn;
        return bar;
    }

    // ---- Input ------------------------------------------------------------------

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { Send(); e.Handled = true; }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_core.IsBusy) _core.Cancel(); else Send();
    }

    private void Send()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        Input.Text = "";
        _ = _core.RunTextAsync(text);
    }
}
