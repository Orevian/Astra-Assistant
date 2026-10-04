using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Astra.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;

namespace Astra.App.Overlays;

/// <summary>
/// One place that decides what "the Astra icon" is. Default is the user's icon.ico (next to the exe);
/// Settings can pick a built-in style or a custom PNG/SVG. If icon.ico is missing a generated orb is used.
/// </summary>
internal static class IconFactory
{
    public static string BundledIconPath => Path.Combine(AppContext.BaseDirectory, "icon.ico");
    public static bool HasBundledIcon => File.Exists(BundledIconPath);

    /// <summary>Path to an .ico/.png/.svg file for the active icon, or null for a built-in drawn style.</summary>
    public static string? ActiveFile(AppearanceSettings a)
    {
        if (a.CustomIconPath is { } custom && File.Exists(custom)) return custom;
        if (a.IconId == "app" && HasBundledIcon) return BundledIconPath;
        return null;
    }

    /// <summary>Visual for overlays, the title bar and previews.</summary>
    public static UIElement Create(AppearanceSettings a, double size, Windows.UI.Color? tint = null)
    {
        var file = ActiveFile(a);
        if (file is not null)
        {
            try
            {
                ImageSource src = file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    ? new SvgImageSource(new Uri(file))
                    : new BitmapImage(new Uri(file)) { DecodePixelWidth = (int)Math.Ceiling(size * 2) };
                return new Microsoft.UI.Xaml.Controls.Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform };
            }
            catch { /* fall through to the drawn icon */ }
        }
        var accent = tint ?? (App.TryParseColor(a.AccentColor, out var c) ? c : Colors.MediumPurple);
        return BuiltIn(a.IconId == "app" ? "orb" : a.IconId, size, accent);
    }

    public static UIElement BuiltIn(string id, double size, Windows.UI.Color accent)
    {
        var brush = new SolidColorBrush(accent);
        switch (id)
        {
            case "star": return new FontIcon { Glyph = "", FontSize = size * 0.9, Foreground = brush };
            case "ring": return new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = Math.Max(2, size * 0.14) };
            case "minimal": return new Ellipse { Width = size * 0.45, Height = size * 0.45, Fill = brush };
            case "abstract": return new FontIcon { Glyph = "", FontSize = size * 0.85, Foreground = brush };
            default:
                return new Ellipse
                {
                    Width = size, Height = size,
                    Fill = new RadialGradientBrush
                    {
                        Center = new(0.35, 0.3), GradientOrigin = new(0.35, 0.3), RadiusX = 0.8, RadiusY = 0.8,
                        GradientStops =
                        {
                            new GradientStop { Color = Colors.White, Offset = 0 },
                            new GradientStop { Color = accent, Offset = 0.55 },
                            new GradientStop { Color = Windows.UI.Color.FromArgb(255, (byte)(accent.R / 3), (byte)(accent.G / 3), (byte)(accent.B / 3)), Offset = 1 },
                        },
                    },
                };
        }
    }

    /// <summary>.ico file for the window, tray and exe-less contexts: icon.ico if present, otherwise a generated orb.</summary>
    public static string EnsureIconFile(string accentHex)
    {
        if (HasBundledIcon) return BundledIconPath;
        var path = Path.Combine(SettingsStore.DataDirectory, "generated-icon.ico");
        try
        {
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(typeof(IconFactory).Assembly.Location)) return path;
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                var ok = App.TryParseColor(accentHex, out var c);
                var accent = ok ? System.Drawing.Color.FromArgb(c.R, c.G, c.B) : System.Drawing.Color.MediumPurple;
                using var gp = new GraphicsPath();
                gp.AddEllipse(4, 4, 56, 56);
                using var pgb = new PathGradientBrush(gp)
                {
                    CenterPoint = new PointF(24, 20), CenterColor = System.Drawing.Color.White,
                    SurroundColors = new[] { System.Drawing.Color.FromArgb(accent.R / 3, accent.G / 3, accent.B / 3) },
                };
                pgb.SetBlendTriangularShape(0.5f);
                g.FillEllipse(pgb, 4, 4, 56, 56);
                using var rim = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, accent), 3);
                g.DrawEllipse(rim, 5, 5, 54, 54);
            }
            using var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((short)0); w.Write((short)1); w.Write((short)1);          // ICONDIR: 1 image
            w.Write((byte)64); w.Write((byte)64); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32); w.Write((int)png.Length); w.Write(22);
            w.Write(png.ToArray());
        }
        catch { /* no icon is better than a crash */ }
        return path;
    }
}
