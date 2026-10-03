using Astra.Core.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Astra.App.Overlays;

/// <summary>
/// Astra's own toast notifications. Drawn by Astra (not Windows toasts) so they can appear on the display the
/// user picked, in the corner the user picked.
/// </summary>
public sealed partial class NotificationWindow : Window
{
    private readonly OverlayHost _host;
    private readonly List<(UIElement Card, DateTime Expires)> _items = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    public double HudOffsetDip { get; set; }

    public NotificationWindow()
    {
        InitializeComponent();
        _host = new OverlayHost(this, Root);
        _timer.Tick += (_, _) => Expire();
    }

    public void Push(string title, string message, bool isError)
    {
        var s = App.Store.Current;
        if (!s.Appearance.NotificationsEnabled) return;
        var theme = OverlayTheme.For(s.Appearance);
        var accent = isError ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x5C, 0x5C)) : theme.AccentBrush;

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Rectangle { Width = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = accent, VerticalAlignment = VerticalAlignment.Stretch });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, Foreground = theme.Foreground, FontFamily = theme.Font });
        text.Children.Add(new TextBlock { Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = theme.Secondary, FontFamily = theme.Font });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var card = new Border
        {
            Child = grid, Padding = new Thickness(14, 10, 14, 10), Background = theme.Background, BorderBrush = theme.Border,
            BorderThickness = new Thickness(theme.BorderThickness), CornerRadius = theme.Radius,
        };
        List.Children.Insert(0, card);
        _items.Insert(0, (card, DateTime.UtcNow.AddSeconds(isError ? 8 : 5)));
        while (_items.Count > 3) { var old = _items[^1]; List.Children.Remove(old.Card); _items.RemoveAt(_items.Count - 1); }
        Layout();
        _timer.Start();
    }

    private void Expire()
    {
        var now = DateTime.UtcNow;
        var removed = false;
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Expires <= now) { List.Children.Remove(_items[i].Card); _items.RemoveAt(i); removed = true; }
        if (_items.Count == 0) { _timer.Stop(); _host.Hide(); }
        else if (removed) Layout();
    }

    /// <summary>Re-anchors after the HUD or bubble above/below it changed size.</summary>
    public void Relayout()
    {
        if (_items.Count > 0) Layout();
    }

    private void Layout()
    {
        var a = App.Store.Current.Appearance;
        // Share a corner with the HUD without overlapping it.
        var offset = a.NotificationPosition == a.HudPosition && a.NotificationDisplay == a.HudDisplay ? HudOffsetDip : 0;
        _host.Place(Displays.Resolve(a.NotificationDisplay), a.NotificationPosition, 360, 12, offset);
        _host.Show();
    }
}
