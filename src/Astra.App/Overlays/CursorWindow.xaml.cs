using Astra.Core.Assistant;
using Astra.Core.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

namespace Astra.App.Overlays;

/// <summary>
/// Astra's own pointer companion. Follows the real mouse (which Astra drives while it acts) and shows the
/// assistant state: idle icon, listening pulse, thinking dots, executing spinner, speaking bars, error mark.
/// </summary>
public sealed partial class CursorWindow : Window
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private readonly OverlayHost _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private AssistantState _state = AssistantState.Idle;
    private DateTime _hideAt = DateTime.MaxValue;
    private float _level;
    private long _t0 = Environment.TickCount64;
    private int _lastSize;

    public CursorWindow()
    {
        InitializeComponent();
        _host = new OverlayHost(this, Root);
        _timer.Tick += (_, _) => Tick();
    }

    public void SetLevel(float level) => _level = Math.Max(_level * 0.7f, level);

    public void SetState(AssistantState state)
    {
        _state = state;
        var a = App.Store.Current.Appearance;
        if (!a.AiCursorEnabled) { Stop(); return; }
        Rebuild();
        if (state == AssistantState.Idle)
        {
            _hideAt = DateTime.UtcNow.AddMilliseconds(1400);
        }
        else
        {
            _hideAt = DateTime.MaxValue;
            if (!_host.IsVisible) { Tick(); _host.Show(); _timer.Start(); }
        }
        if (state == AssistantState.Error) _hideAt = DateTime.UtcNow.AddSeconds(3);
    }

    private void Stop()
    {
        _timer.Stop();
        _host.Hide();
    }

    private void Rebuild()
    {
        var a = App.Store.Current.Appearance;
        var theme = OverlayTheme.For(a);
        var size = Math.Clamp(a.CursorSize, 16, 64);
        var box = size + 26;
        Root.Width = Root.Height = box;
        foreach (var e in new[] { Pulse, Spinner }) { e.Width = e.Height = size + 14; e.Stroke = theme.AccentBrush; }
        foreach (var d in new[] { Dot1, Dot2, Dot3 }) d.Fill = theme.AccentBrush;
        foreach (var b in new[] { Bar1, Bar2, Bar3, Bar4 }) b.Fill = theme.AccentBrush;
        IconHost.Children.Clear();
        IconHost.Children.Add(IconFactory.Create(a, size, theme.Accent));
        _lastSize = box;
    }

    private void Tick()
    {
        var a = App.Store.Current.Appearance;
        if (!a.AiCursorEnabled) { Stop(); return; }
        if (DateTime.UtcNow >= _hideAt) { Stop(); return; }

        // Follow the real pointer, offset so Astra's icon sits beside it rather than covering it.
        GetCursorPos(out var p);
        var scale = _host.CurrentScale;
        var px = (int)(_lastSize * scale);
        var d = Displays.FromPoint(p.X, p.Y);
        var x = Math.Min(p.X + (int)(14 * scale), d.X + d.Width - px);
        var y = Math.Min(p.Y + (int)(14 * scale), d.Y + d.Height - px);
        _host.MoveTo(x, y, px, px);

        var anim = a.AnimationsEnabled;
        var t = (Environment.TickCount64 - _t0) / 1000.0;
        _level *= 0.85f;

        Pulse.Opacity = 0; Spinner.Opacity = 0; Dots.Opacity = 0; Bars.Opacity = 0; ErrorMark.Opacity = 0;
        switch (_state)
        {
            case AssistantState.Listening:
            {
                var ph = anim ? (t * 1.4) % 1.0 : 0.5;
                PulseScale.ScaleX = PulseScale.ScaleY = 0.85 + ph * 0.55;
                Pulse.Opacity = 1 - ph;
                break;
            }
            case AssistantState.Thinking:
                Dots.Opacity = 1;
                Dot1.Opacity = anim ? 0.35 + 0.65 * Math.Abs(Math.Sin(t * 4)) : 1;
                Dot2.Opacity = anim ? 0.35 + 0.65 * Math.Abs(Math.Sin(t * 4 - 0.7)) : 1;
                Dot3.Opacity = anim ? 0.35 + 0.65 * Math.Abs(Math.Sin(t * 4 - 1.4)) : 1;
                break;
            case AssistantState.Executing:
                Spinner.Opacity = 0.95;
                SpinRotate.Angle = anim ? (t * 220) % 360 : 0;
                break;
            case AssistantState.Speaking:
                Bars.Opacity = 1;
                var b = new[] { Bar1, Bar2, Bar3, Bar4 };
                for (var i = 0; i < b.Length; i++)
                    b[i].Height = 4 + 14 * Math.Clamp(_level * 3 * (0.5 + 0.5 * Math.Abs(Math.Sin(t * 9 + i * 1.3))), 0, 1);
                break;
            case AssistantState.Error:
                ErrorMark.Opacity = 1;
                break;
        }
    }

    /// <summary>Moves the overlay smoothly to a screen point (used when Astra acts on a specific element).</summary>
    public void Refresh() { if (_host.IsVisible) Rebuild(); }
}
