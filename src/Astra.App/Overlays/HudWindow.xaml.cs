using Astra.Core.Assistant;
using Astra.Core.Localization;
using Astra.Core.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Astra.App.Overlays;

/// <summary>Heads-up display: what Astra hears, thinks and is doing right now.</summary>
public sealed partial class HudWindow : Window
{
    private readonly OverlayHost _host;
    private HudSnapshot _last = new(HudPhase.Hidden, "", "", Array.Empty<HudStep>(), null);
    private readonly Rectangle[] _bars = new Rectangle[5];
    private DateTime _hideAt = DateTime.MaxValue;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public HudWindow()
    {
        InitializeComponent();
        _host = new OverlayHost(this, Root);
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
            LevelBars.Children.Add(_bars[i]);
        }
        _timer.Tick += (_, _) => { if (DateTime.UtcNow >= _hideAt) { _hideAt = DateTime.MaxValue; _host.Hide(); _timer.Stop(); } };
    }

    public bool IsShown => _host.IsVisible;
    public double HeightDip => _host.HeightDip;

    public void Apply(HudSnapshot snap)
    {
        _last = snap;
        var settings = App.Store.Current;
        if (!settings.Appearance.HudEnabled || snap.Phase == HudPhase.Hidden)
        {
            if (_host.IsVisible && snap.Phase == HudPhase.Hidden) { _hideAt = DateTime.UtcNow.AddMilliseconds(150); _timer.Start(); }
            else if (!settings.Appearance.HudEnabled) _host.Hide();
            return;
        }

        var theme = OverlayTheme.For(settings.Appearance);
        Card.Background = theme.Background;
        Card.BorderBrush = theme.Border;
        Card.BorderThickness = new Thickness(theme.BorderThickness);
        Card.CornerRadius = theme.Radius;
        NameText.Foreground = theme.Foreground;
        NameText.FontFamily = theme.Font;
        PhaseText.Foreground = theme.Secondary;
        PhaseText.FontFamily = theme.Font;
        TaskText.Foreground = theme.Secondary;
        TaskText.FontFamily = theme.Font;
        MessageText.Foreground = theme.Foreground;
        MessageText.FontFamily = theme.Font;
        foreach (var b in _bars) b.Fill = theme.AccentBrush;
        Progress.Foreground = theme.AccentBrush;
        NameText.Text = settings.General.AssistantName;

        IconHost.Children.Clear();
        IconHost.Children.Add(IconFactory.Create(settings.Appearance, 28, theme.Accent));

        PhaseText.Text = snap.Phase switch
        {
            HudPhase.Listening => Loc.T("Listening…"),
            HudPhase.Thinking => Loc.T("Thinking…"),
            HudPhase.Working => snap.Current,
            HudPhase.Speaking => Loc.T("Speaking…"),
            HudPhase.Done => Loc.T("Done"),
            HudPhase.Error => Loc.T("Something went wrong"),
            _ => "",
        };

        TaskText.Visibility = snap.Task.Length > 0 && snap.Phase is HudPhase.Thinking or HudPhase.Working or HudPhase.Done or HudPhase.Error ? Visibility.Visible : Visibility.Collapsed;
        TaskText.Text = snap.Task;

        StepList.Children.Clear();
        foreach (var s in snap.Steps) StepList.Children.Add(StepRow(s, theme));
        StepList.Visibility = snap.Steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Progress.Visibility = snap.Phase is HudPhase.Thinking or HudPhase.Working ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Visibility = snap.Message is { Length: > 0 } && snap.Phase is HudPhase.Done or HudPhase.Error ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Text = snap.Message ?? "";
        LevelBars.Visibility = snap.Phase == HudPhase.Listening ? Visibility.Visible : Visibility.Collapsed;

        Reposition();
        _host.Show();
        _timer.Start();
        _hideAt = snap.Phase switch
        {
            HudPhase.Done => DateTime.UtcNow.AddSeconds(4),
            HudPhase.Error => DateTime.UtcNow.AddSeconds(6),
            _ => DateTime.MaxValue,
        };
    }

    private UIElement StepRow(HudStep s, OverlayTheme theme)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement glyph = s.Status switch
        {
            StepStatus.Running => new ProgressRing { Width = 12, Height = 12, IsActive = true, Foreground = theme.AccentBrush },
            StepStatus.Ok => new FontIcon { Glyph = "", FontSize = 12, Foreground = theme.AccentBrush },
            _ => new FontIcon { Glyph = "", FontSize = 12, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x5C, 0x5C)) },
        };
        row.Children.Add(glyph);
        var text = new TextBlock
        {
            Text = s.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = s.Status == StepStatus.Running ? theme.Foreground : theme.Secondary, FontFamily = theme.Font,
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    public void SetLevel(float level)
    {
        if (!_host.IsVisible || LevelBars.Visibility != Visibility.Visible) return;
        for (var i = 0; i < _bars.Length; i++)
        {
            var shape = 0.45 + 0.55 * Math.Sin((Environment.TickCount64 / 120.0) + i * 0.9) * Math.Sin(i * 0.7 + 1);
            _bars[i].Height = 4 + 20 * Math.Clamp(level * Math.Abs(shape) * 1.6, 0, 1);
        }
    }

    /// <summary>Re-anchors to the configured corner of the configured display (call after the content size changed).</summary>
    public void Reposition()
    {
        var a = App.Store.Current.Appearance;
        _host.Place(Displays.Resolve(a.HudDisplay), a.HudPosition, 340, 12);
    }
}
