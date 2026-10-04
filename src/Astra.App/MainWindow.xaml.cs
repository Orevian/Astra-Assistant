using Astra.App.Pages;
using Astra.Core.Localization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Astra.App;

public sealed partial class MainWindow : Window
{
    private sealed record SearchEntry(string Title, string Page, string Keywords)
    {
        public override string ToString() => $"{Loc.T(Title)}  ·  {Loc.T(PageNames[Page])}";
        public bool Matches(string q) =>
            Loc.T(Title).Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || Keywords.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, string> PageNames = new()
    {
        ["general"] = "General", ["ai"] = "AI model", ["voice"] = "Voice", ["appearance"] = "Appearance",
        ["automation"] = "Automation", ["security"] = "Security", ["index"] = "Computer index", ["memory"] = "Memory",
    };

    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage), ["general"] = typeof(GeneralPage), ["ai"] = typeof(AiPage),
        ["voice"] = typeof(VoicePage), ["appearance"] = typeof(AppearancePage), ["automation"] = typeof(AutomationPage),
        ["security"] = typeof(SecurityPage), ["index"] = typeof(IndexPage), ["memory"] = typeof(MemoryPage),
    };

    private static readonly SearchEntry[] SearchIndex =
    {
        new("Assistant name", "general", "name call"),
        new("Start with Windows", "general", "startup boot login autostart"),
        new("Minimize to tray", "general", "system tray close background"),
        new("Language", "general", "dil türkçe english"),
        new("Reset all settings", "general", "default restore"),
        new("AI provider", "ai", "openai gemini anthropic mistral deepseek ollama"),
        new("API key", "ai", "credential token secret"),
        new("Model", "ai", "gpt claude llama"),
        new("Test connection", "ai", "verify check"),
        new("Ollama", "ai", "local offline"),
        new("Microphone", "voice", "input device mic"),
        new("Wake word", "voice", "hey activation name"),
        new("Push-to-talk", "voice", "hotkey shortcut key"),
        new("Text-to-speech voice", "voice", "tts speaker speed volume"),
        new("Response language", "voice", "multilingual auto detect"),
        new("Theme", "appearance", "glass cyber neon terminal minimal classic"),
        new("Accent color", "appearance", "colour"),
        new("Astra icon", "appearance", "orb star ring custom png svg"),
        new("HUD", "appearance", "overlay progress"),
        new("AI cursor", "appearance", "pointer"),
        new("Audio visualizer", "appearance", "spectrum waveform orb pulse"),
        new("Animations", "appearance", "motion"),
        new("Notifications", "appearance", "toast alerts"),
        new("Browser", "automation", "chrome edge firefox avast"),
        new("UI Automation", "automation", "accessibility coordinates"),
        new("Mouse and keyboard", "automation", "input control"),
        new("Verify actions", "automation", "check result"),
        new("Local-first intents", "automation", "tokens llm offline"),
        new("Permissions", "security", "allow deny ask confirm"),
        new("Confirm destructive actions", "security", "delete files"),
        new("Stored credentials", "security", "keys remove"),
        new("Scan Windows", "index", "indexer apps files"),
        new("Excluded folders", "index", "ignore skip"),
        new("Watch for changes", "index", "incremental watcher"),
        new("Memory", "memory", "remember forget history"),
        new("Short-term context", "memory", "turns conversation"),
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = "Astra";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(800 * scale)));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        RootGrid.Loaded += (_, _) =>
            RightInsetColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / RootGrid.XamlRoot.RasterizationScale);

        ApplyTheme(App.Store.Current.Appearance.AppTheme);
        App.Store.Changed += s => DispatcherQueue.TryEnqueue(() => ApplyTheme(s.Appearance.AppTheme));

        // Translate every page once it has loaded, and refresh everything when the language changes.
        ContentFrame.Navigated += (_, e) =>
        {
            if (e.Content is FrameworkElement page)
                page.Loaded += (_, _) => Localizer.Apply(page);
        };
        RootGrid.Loaded += (_, _) => Localizer.Apply(RootGrid);
        Loc.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            Localizer.Apply(TitleBarGrid);
            Localizer.Apply(Nav);
            if (ContentFrame.CurrentSourcePageType is { } t)
            {
                ContentFrame.BackStack.Clear();
                ContentFrame.Navigate(t);
            }
        });

        RefreshIcon();
        AppWindow.Closing += (_, e) =>
        {
            if (App.IsExiting) return;
            if (App.Store.Current.General.MinimizeToTray) { e.Cancel = true; AppWindow.Hide(); }
            else App.Quit();
        };

        Nav.SelectedItem = Nav.MenuItems[0];
        if (!App.Store.Current.General.FirstRunCompleted) ShowOnboarding();
        var start = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--page=", StringComparison.Ordinal));
        if (start is not null) NavigateTo(start["--page=".Length..]);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Applies the active Astra icon to the window, the title bar and (via App) the tray.</summary>
    public void RefreshIcon()
    {
        var a = App.Store.Current.Appearance;
        try { AppWindow.SetIcon(Overlays.IconFactory.ActiveFile(a) is { } f && f.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? f : Overlays.IconFactory.EnsureIconFile(a.AccentColor)); } catch { }
        LogoHost.Children.Clear();
        LogoHost.Children.Add(Overlays.IconFactory.Create(a, 22));
    }

    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }

    public void ShowOnboarding()
    {
        OnboardingHost.Children.Clear();
        var view = new Onboarding.OnboardingView();
        view.Finished += () =>
        {
            OnboardingHost.Visibility = Visibility.Collapsed;
            OnboardingHost.Children.Clear();
            Nav.SelectedItem = Nav.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));
        };
        OnboardingHost.Children.Add(view);
        OnboardingHost.Visibility = Visibility.Visible;
    }

    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    private void ApplyTheme(string mode)
    {
        RootGrid.RequestedTheme = mode switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        sender.ItemsSource = q.Length == 0
            ? null
            : SearchIndex.Where(e => e.Matches(q)).Take(8).ToList();
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchEntry e) sender.Text = Loc.T(e.Title);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var entry = args.ChosenSuggestion as SearchEntry
            ?? SearchIndex.FirstOrDefault(e => e.Matches(args.QueryText));
        if (entry is not null) NavigateTo(entry.Page);
    }
}
