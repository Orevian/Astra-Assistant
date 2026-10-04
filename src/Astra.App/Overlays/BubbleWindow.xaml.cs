using Astra.Core.Native;
using Microsoft.UI.Xaml;

namespace Astra.App.Overlays;

/// <summary>Optional speech bubble that shows what Astra is saying (off by default).</summary>
public sealed partial class BubbleWindow : Window
{
    private readonly OverlayHost _host;

    public BubbleWindow()
    {
        InitializeComponent();
        _host = new OverlayHost(this, Root);
    }

    public bool IsShown => _host.IsVisible;
    public double HeightDip => _host.HeightDip;

    /// <param name="offsetDip">Vertical distance to keep from the corner so the bubble sits above/below the HUD.</param>
    public void Show(string text, double offsetDip)
    {
        var s = App.Store.Current;
        if (!s.Appearance.SpeechBubbleEnabled || string.IsNullOrWhiteSpace(text)) { _host.Hide(); return; }
        var theme = OverlayTheme.For(s.Appearance);
        Card.Background = theme.Background;
        Card.BorderBrush = theme.Border;
        Card.BorderThickness = new Thickness(theme.BorderThickness);
        Card.CornerRadius = theme.Radius;
        BubbleText.Foreground = theme.Foreground;
        BubbleText.FontFamily = theme.Font;
        BubbleText.Text = text;
        IconHost.Children.Clear();
        IconHost.Children.Add(IconFactory.Create(s.Appearance, 20, theme.Accent));

        var a = s.Appearance;
        _host.Place(Displays.Resolve(a.HudDisplay), a.BubblePosition, 400, 12, offsetDip);
        _host.Show();
    }

    public void Hide() => _host.Hide();
}
