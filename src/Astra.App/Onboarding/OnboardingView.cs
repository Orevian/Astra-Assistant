using Astra.App.Overlays;
using Astra.App.Pages;
using Astra.Core.Localization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Astra.App.Onboarding;

/// <summary>
/// First-launch wizard: welcome → voice → AI → Windows scan → permissions → appearance → done.
/// Each step hosts the real settings page, so nothing here can drift from Settings.
/// </summary>
internal sealed class OnboardingView : UserControl
{
    private sealed record Step(string Title, string Subtitle, Type? Page);

    private static Step[] Steps => new[]
    {
        new Step(Loc.T("Welcome to Astra"), "", null),
        new Step(Loc.T("Voice"), Loc.T("Choose your microphone, wake word and speech engine."), typeof(VoicePage)),
        new Step(Loc.T("AI model"), Loc.T("Connect a cloud provider, or run models locally with Ollama."), typeof(AiPage)),
        new Step(Loc.T("Computer index"), Loc.T("Let Astra learn your apps and files — locally, never sent to an AI model."), typeof(IndexPage)),
        new Step(Loc.T("Permissions"), Loc.T("Decide what Astra may do without asking."), typeof(SecurityPage)),
        new Step(Loc.T("Appearance"), Loc.T("Pick a theme and what Astra shows on screen."), typeof(AppearancePage)),
        new Step(Loc.T("You're all set"), "", null),
    };

    private readonly Grid _stage = new();
    private readonly Frame _frame = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _subtitle = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
    private readonly Button _back = new() { MinWidth = 100 };
    private readonly Button _next = new() { MinWidth = 120, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private readonly Button _skip = new() { MinWidth = 100 };
    private readonly Grid _welcome = new();
    private int _index;
    private static string? _wizardFlag = "wizard";

    public event Action? Finished;

    public OnboardingView()
    {
        var root = new Grid { Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 6, Padding = new Thickness(48, 18, 48, 6), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch };
        header.Children.Add(_dots);
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        root.Children.Add(header);

        Grid.SetRow(_stage, 1);
        _stage.Children.Add(_frame);
        _stage.Children.Add(_welcome);
        root.Children.Add(_stage);

        var footer = new Grid { Padding = new Thickness(48, 12, 48, 20), MaxWidth = 960, ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_back, 1); Grid.SetColumn(_skip, 2); Grid.SetColumn(_next, 3);
        footer.Children.Add(_back); footer.Children.Add(_skip); footer.Children.Add(_next);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        _back.Click += (_, _) => Go(_index - 1);
        _skip.Click += (_, _) => Go(_index + 1);
        _next.Click += (_, _) => { if (_index == Steps.Length - 1) Finish(); else Go(_index + 1); };
        _frame.Navigated += (_, e) =>
        {
            if (e.Content is not FrameworkElement p) return;
            p.Loaded += (_, _) =>
            {
                Localizer.Apply(p);
                HidePageHeader(p);
            };
        };

        Content = root;
        var startStep = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--onboarding-step=", StringComparison.Ordinal));
        Go(startStep is not null && int.TryParse(startStep["--onboarding-step=".Length..], out var n) ? n : 0);
    }

    private void Go(int i)
    {
        _index = Math.Clamp(i, 0, Steps.Length - 1);
        var step = Steps[_index];
        _title.Text = step.Title;
        _subtitle.Text = step.Subtitle;
        _subtitle.Visibility = step.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _dots.Children.Clear();
        for (var d = 0; d < Steps.Length; d++)
            _dots.Children.Add(new Rectangle
            {
                Width = d == _index ? 22 : 8, Height = 8, RadiusX = 4, RadiusY = 4,
                Fill = d <= _index ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"],
            });

        _back.Content = Loc.T("Back");
        _skip.Content = Loc.T("Skip");
        _next.Content = _index == Steps.Length - 1 ? Loc.T("Start using Astra") : _index == 0 ? Loc.T("Get started") : Loc.T("Next");
        _back.Visibility = _index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _skip.Visibility = _index == 0 || _index == Steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;

        if (step.Page is null)
        {
            _frame.Visibility = Visibility.Collapsed;
            _welcome.Visibility = Visibility.Visible;
            BuildHero(_index == 0);
        }
        else
        {
            _welcome.Visibility = Visibility.Collapsed;
            _frame.Visibility = Visibility.Visible;
            _frame.Navigate(step.Page, _wizardFlag, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }

    /// <summary>The wizard already shows the step title, so the hosted page's own title and subtitle are redundant.</summary>
    private static void HidePageHeader(DependencyObject node)
    {
        var title = Application.Current.Resources["PageTitleStyle"];
        var sub = Application.Current.Resources["PageSubtitleStyle"];
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is TextBlock t && (ReferenceEquals(t.Style, title) || ReferenceEquals(t.Style, sub))) t.Visibility = Visibility.Collapsed;
            else HidePageHeader(child);
        }
    }

    private void BuildHero(bool welcome)
    {
        _welcome.Children.Clear();
        _welcome.RowDefinitions.Clear();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 18, MaxWidth = 520 };
        var icon = new Grid { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center };
        icon.Children.Add(IconFactory.Create(App.Store.Current.Appearance, 104));
        stack.Children.Add(icon);

        var text = welcome
            ? Loc.T("Astra is your natural-language interface to Windows. Talk to it, and it opens apps, finds files, browses the web and gets things done — asking you first when it matters. This quick setup takes about two minutes, and every choice can be changed later in Settings.")
            : Loc.T("Astra is ready. Say its wake word, hold the push-to-talk key, or type in the console. You can change anything in Settings at any time.");
        stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 15, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

        if (welcome)
        {
            var lang = new ComboBox { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 220, Header = Loc.T("Interface language") };
            foreach (var (label, tag) in new[] { (Loc.T("Match Windows"), "auto"), ("English", "en"), ("Türkçe", "tr") })
                lang.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
            lang.SelectedIndex = App.Store.Current.General.UiLanguage switch { "en" => 1, "tr" => 2, _ => 0 };
            lang.SelectionChanged += (_, _) =>
            {
                if (lang.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
                App.Store.Current.General.UiLanguage = tag;
                App.Store.Commit();
                Loc.Set(tag);
                Go(0); // rebuild the wizard text in the new language
            };
            stack.Children.Add(lang);
        }
        _welcome.Children.Add(stack);
    }

    private void Finish()
    {
        App.Store.Current.General.FirstRunCompleted = true;
        App.Store.Commit();
        if (App.Store.Current.Index.Completed && App.Store.Current.Index.WatchChanges) App.Assistant.Runtime.Index.Watcher.Start();
        App.Assistant.ApplyVoiceSettings();
        Finished?.Invoke();
    }
}
