using Astra.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Astra.App.Overlays;

/// <summary>Visual style of the overlays (HUD, bubble, notifications), derived from the selected theme preset.</summary>
internal sealed class OverlayTheme
{
    public Brush Background { get; init; } = null!;
    public Brush Border { get; init; } = null!;
    public double BorderThickness { get; init; } = 1;
    public CornerRadius Radius { get; init; }
    public Brush Foreground { get; init; } = null!;
    public Brush Secondary { get; init; } = null!;
    public Color Accent { get; init; }
    public Brush AccentBrush => new SolidColorBrush(Accent);
    public FontFamily Font { get; init; } = new("Segoe UI Variable");
    public bool Light { get; init; }

    private static Color C(string hex, byte? alpha = null)
    {
        var h = hex.TrimStart('#');
        var v = Convert.ToUInt32(h, 16);
        return h.Length == 8
            ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb(alpha ?? 255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static SolidColorBrush S(string hex) => new(C(hex));

    public static OverlayTheme For(AppearanceSettings a)
    {
        App.TryParseColor(a.AccentColor, out var accent);
        var alpha = (byte)Math.Clamp(a.HudOpacity / 100.0 * 255, 60, 255);
        Color Bg(string rgb) => Color.FromArgb(alpha, C(rgb).R, C(rgb).G, C(rgb).B);

        switch (a.Theme)
        {
            case ThemePreset.Minimal:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("1E1E1E")), Border = S("#33FFFFFF"), Radius = new CornerRadius(10),
                    Foreground = S("#FFFFFF"), Secondary = S("#B3FFFFFF"), Accent = accent,
                };
            case ThemePreset.Cyber:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("061018")), Border = S("#FF00E5FF"), BorderThickness = 1, Radius = new CornerRadius(3),
                    Foreground = S("#E6FBFF"), Secondary = S("#9900E5FF"), Accent = C("#00E5FF"), Font = new FontFamily("Cascadia Mono"),
                };
            case ThemePreset.Neon:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("1A0626")), Border = S("#FFFF2BD6"), BorderThickness = 1.5, Radius = new CornerRadius(14),
                    Foreground = S("#FFFFFF"), Secondary = S("#CCFF9BEF"), Accent = C("#FF2BD6"),
                };
            case ThemePreset.Classic:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("F7F7F7")), Border = S("#33000000"), Radius = new CornerRadius(8),
                    Foreground = S("#1B1B1B"), Secondary = S("#99000000"), Accent = C("#0067C0"), Light = true,
                };
            case ThemePreset.Terminal:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("000000")), Border = S("#FF22C55E"), Radius = new CornerRadius(0),
                    Foreground = S("#22C55E"), Secondary = S("#8022C55E"), Accent = C("#22C55E"), Font = new FontFamily("Cascadia Mono"),
                };
            case ThemePreset.Custom:
                return new OverlayTheme
                {
                    Background = new SolidColorBrush(Bg("14161F")), Border = new SolidColorBrush(Color.FromArgb(160, accent.R, accent.G, accent.B)),
                    Radius = new CornerRadius(14), Foreground = S("#FFFFFF"), Secondary = S("#B3FFFFFF"), Accent = accent,
                };
            default: // Glass
                return new OverlayTheme
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new(0, 0), EndPoint = new(1, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb(alpha, 0x26, 0x28, 0x3C), Offset = 0 },
                            new GradientStop { Color = Color.FromArgb((byte)(alpha * 0.92), 0x16, 0x18, 0x26), Offset = 1 },
                        },
                    },
                    Border = S("#40FFFFFF"), Radius = new CornerRadius(16), Foreground = S("#FFFFFF"), Secondary = S("#B3FFFFFF"), Accent = accent,
                };
        }
    }
}
