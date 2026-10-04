using Astra.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Astra.App.Overlays;

/// <summary>Draws the speech spectrum in one of several styles. Fed ~40×/s with 32 band levels in 0..1.</summary>
public sealed class VisualizerControl : UserControl
{
    private readonly Canvas _canvas = new();
    private VisualizerStyle _style = VisualizerStyle.SpectrumBars;
    private Color _accent = Colors.MediumPurple;
    private double _w = 360, _h = 90;
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private Ellipse[] _rings = Array.Empty<Ellipse>();
    private Ellipse? _orb, _glow;
    private Polygon? _wave;
    private const int Bands = 32;

    private static class Colors { public static readonly Color MediumPurple = Color.FromArgb(255, 0x93, 0x70, 0xDB); }

    public VisualizerControl() { Content = _canvas; }

    public static (double W, double H) BaseSize(VisualizerStyle style) => style switch
    {
        VisualizerStyle.CircularSpectrum => (200, 200),
        VisualizerStyle.Pulse or VisualizerStyle.Orb => (160, 160),
        VisualizerStyle.Minimal => (90, 44),
        _ => (360, 90),
    };

    public void Configure(VisualizerStyle style, Color accent, double scale)
    {
        _style = style;
        _accent = accent;
        var (bw, bh) = BaseSize(style);
        _w = bw * scale; _h = bh * scale;
        Width = _w; Height = _h;
        _canvas.Width = _w; _canvas.Height = _h;
        _canvas.Children.Clear();
        _bars = Array.Empty<Rectangle>(); _rings = Array.Empty<Ellipse>(); _orb = _glow = null; _wave = null;

        SolidColorBrush Accent(byte a = 255) => new(Color.FromArgb(a, accent.R, accent.G, accent.B));
        switch (style)
        {
            case VisualizerStyle.SpectrumBars:
            {
                _bars = new Rectangle[Bands];
                var step = _w / Bands;
                for (var i = 0; i < Bands; i++)
                {
                    var r = new Rectangle { Width = step * 0.62, RadiusX = step * 0.31, RadiusY = step * 0.31, Fill = BarGradient(accent), Height = 3 };
                    Canvas.SetLeft(r, i * step + step * 0.19); Canvas.SetTop(r, _h - 3);
                    _bars[i] = r; _canvas.Children.Add(r);
                }
                break;
            }
            case VisualizerStyle.CircularSpectrum:
            {
                const int n = 56;
                _bars = new Rectangle[n];
                for (var i = 0; i < n; i++)
                {
                    var r = new Rectangle { Width = _w * 0.018, Height = 4, RadiusX = 2, RadiusY = 2, Fill = Accent() };
                    r.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1.0);
                    _bars[i] = r; _canvas.Children.Add(r);
                }
                break;
            }
            case VisualizerStyle.Waveform:
            {
                _wave = new Polygon { Fill = Accent(70), Stroke = Accent(), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                _canvas.Children.Add(_wave);
                break;
            }
            case VisualizerStyle.Pulse:
            {
                _rings = new Ellipse[3];
                for (var i = 0; i < 3; i++)
                {
                    var e = new Ellipse { Stroke = Accent((byte)(220 - i * 60)), StrokeThickness = 3 - i * 0.6 };
                    _rings[i] = e; _canvas.Children.Add(e);
                }
                break;
            }
            case VisualizerStyle.Orb:
            {
                _glow = new Ellipse { Fill = new RadialGradientBrush { GradientStops = { new GradientStop { Color = Color.FromArgb(150, accent.R, accent.G, accent.B), Offset = 0 }, new GradientStop { Color = Color.FromArgb(0, accent.R, accent.G, accent.B), Offset = 1 } } } };
                _orb = new Ellipse
                {
                    Fill = new RadialGradientBrush
                    {
                        Center = new Windows.Foundation.Point(0.35, 0.3), GradientOrigin = new Windows.Foundation.Point(0.35, 0.3), RadiusX = 0.8, RadiusY = 0.8,
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb(255, 255, 255, 255), Offset = 0 },
                            new GradientStop { Color = accent, Offset = 0.55 },
                            new GradientStop { Color = Color.FromArgb(255, (byte)(accent.R / 3), (byte)(accent.G / 3), (byte)(accent.B / 3)), Offset = 1 },
                        },
                    },
                };
                _canvas.Children.Add(_glow); _canvas.Children.Add(_orb);
                break;
            }
            case VisualizerStyle.Minimal:
            {
                _bars = new Rectangle[5];
                var step = _w / 5;
                for (var i = 0; i < 5; i++)
                {
                    var r = new Rectangle { Width = step * 0.45, RadiusX = step * 0.22, RadiusY = step * 0.22, Fill = Accent(), Height = 4 };
                    Canvas.SetLeft(r, i * step + step * 0.27); Canvas.SetTop(r, _h / 2 - 2);
                    _bars[i] = r; _canvas.Children.Add(r);
                }
                break;
            }
        }
        Render(new float[Bands]);
    }

    private static Brush BarGradient(Color a) => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 1), EndPoint = new Windows.Foundation.Point(0, 0),
        GradientStops =
        {
            new GradientStop { Color = a, Offset = 0 },
            new GradientStop { Color = Color.FromArgb(255, (byte)Math.Min(255, a.R + 70), (byte)Math.Min(255, a.G + 70), (byte)Math.Min(255, a.B + 70)), Offset = 1 },
        },
    };

    public void Render(float[] b)
    {
        if (b.Length < Bands) return;
        switch (_style)
        {
            case VisualizerStyle.SpectrumBars:
                for (var i = 0; i < _bars.Length; i++)
                {
                    var h = Math.Max(3, b[i] * _h);
                    _bars[i].Height = h;
                    Canvas.SetTop(_bars[i], _h - h);
                }
                break;
            case VisualizerStyle.CircularSpectrum:
            {
                var cx = _w / 2; var cy = _h / 2; var r0 = _w * 0.24; var maxLen = _w * 0.26;
                var n = _bars.Length;
                for (var i = 0; i < n; i++)
                {
                    // Mirror the spectrum left/right so the ring looks symmetric.
                    var t = i < n / 2 ? i : n - 1 - i;
                    var level = b[Math.Min(Bands - 1, t * Bands / (n / 2))];
                    var len = 4 + level * maxLen;
                    var angle = i * 360.0 / n;
                    var bar = _bars[i];
                    bar.Height = len;
                    Canvas.SetLeft(bar, cx - bar.Width / 2);
                    Canvas.SetTop(bar, cy - r0 - len);
                    bar.RenderTransform = new RotateTransform { Angle = angle, CenterX = bar.Width / 2, CenterY = len + r0 };
                }
                break;
            }
            case VisualizerStyle.Waveform:
            {
                var pts = new PointCollection();
                var mid = _h / 2;
                for (var i = 0; i < Bands; i++) pts.Add(new Windows.Foundation.Point(i * _w / (Bands - 1), mid - Math.Max(1, b[i] * mid)));
                for (var i = Bands - 1; i >= 0; i--) pts.Add(new Windows.Foundation.Point(i * _w / (Bands - 1), mid + Math.Max(1, b[i] * mid)));
                _wave!.Points = pts;
                break;
            }
            case VisualizerStyle.Pulse:
            {
                var level = Level(b);
                for (var i = 0; i < _rings.Length; i++)
                {
                    var d = _w * (0.28 + level * 0.55) * (1 + i * 0.28);
                    _rings[i].Width = d; _rings[i].Height = d;
                    Canvas.SetLeft(_rings[i], (_w - d) / 2); Canvas.SetTop(_rings[i], (_h - d) / 2);
                    _rings[i].Opacity = Math.Clamp(0.25 + level * 1.2 - i * 0.25, 0.1, 1);
                }
                break;
            }
            case VisualizerStyle.Orb:
            {
                var level = Level(b);
                var d = _w * (0.38 + level * 0.34);
                _orb!.Width = d; _orb.Height = d;
                Canvas.SetLeft(_orb, (_w - d) / 2); Canvas.SetTop(_orb, (_h - d) / 2);
                var g = _w * (0.62 + level * 0.38);
                _glow!.Width = g; _glow.Height = g;
                Canvas.SetLeft(_glow, (_w - g) / 2); Canvas.SetTop(_glow, (_h - g) / 2);
                break;
            }
            case VisualizerStyle.Minimal:
            {
                int[] idx = { 2, 7, 13, 20, 27 };
                for (var i = 0; i < _bars.Length; i++)
                {
                    var h = Math.Max(4, b[idx[i]] * _h);
                    _bars[i].Height = h;
                    Canvas.SetTop(_bars[i], (_h - h) / 2);
                }
                break;
            }
        }
    }

    private static double Level(float[] b)
    {
        double sum = 0;
        for (var i = 0; i < 20; i++) sum += b[i];
        return Math.Clamp(sum / 20 * 1.4, 0, 1);
    }
}
