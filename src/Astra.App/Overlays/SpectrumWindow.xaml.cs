using Astra.Core.Native;
using Astra.Core.Settings;
using Microsoft.UI.Xaml;

namespace Astra.App.Overlays;

/// <summary>Audio visualizer overlay (default: top-right of the chosen display). Shown only while Astra speaks.</summary>
public sealed partial class SpectrumWindow : Window
{
    private readonly OverlayHost _host;
    private readonly VisualizerControl _viz = new();
    private VisualizerStyle _configuredStyle = VisualizerStyle.None;
    private double _configuredScale;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public SpectrumWindow()
    {
        InitializeComponent();
        Root.Children.Add(_viz);
        _host = new OverlayHost(this, Root);
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); _host.Hide(); };
    }

    public void SetSpeaking(bool speaking)
    {
        var a = App.Store.Current.Appearance;
        if (!a.VisualizerEnabled || a.Visualizer == VisualizerStyle.None) { _hideTimer.Stop(); _host.Hide(); return; }
        if (speaking)
        {
            _hideTimer.Stop();
            Configure(force: !_host.IsVisible);
            _host.Show();
        }
        else _hideTimer.Start();
    }

    public void Render(float[] bands)
    {
        if (!_host.IsVisible) return;
        _viz.Render(bands);
    }

    /// <summary>Applies style/size/opacity/position from settings.</summary>
    public void Configure(bool force = false)
    {
        var a = App.Store.Current.Appearance;
        var scale = a.VisualizerSize / 100.0;
        if (force || _configuredStyle != a.Visualizer || Math.Abs(_configuredScale - scale) > 0.001)
        {
            _viz.Configure(a.Visualizer, OverlayTheme.For(a).Accent, scale);
            _configuredStyle = a.Visualizer;
            _configuredScale = scale;
        }
        _viz.Opacity = a.VisualizerOpacity / 100.0;
        var (bw, bh) = VisualizerControl.BaseSize(a.Visualizer);
        _host.Place(Displays.Resolve(a.HudDisplay), a.VisualizerPosition, bw * scale, 24, 0, bh * scale);
    }
}
