using Astra.Core.Localization;
using Astra.Core.Native;
using Astra.Core.Tools;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Astra.App.Overlays;

/// <summary>"Astra wants to … [Cancel] [Allow]" — always on top, on the display chosen for the HUD.</summary>
public sealed partial class ConfirmWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _decided;

    public bool IsDestructive { get; }
    public Task<bool> Result => _result.Task;

    public ConfirmWindow(ConfirmRequest request)
    {
        InitializeComponent();
        IsDestructive = request.Destructive;
        Title = "Astra";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;

        TitleText.Text = request.Message;
        CategoryText.Text = Loc.F("Permission: {0}", request.Title);
        MessageText.Visibility = Visibility.Collapsed;
        if (request.Message.Length > 90)
        {
            TitleText.Text = Loc.T("Astra needs your approval");
            MessageText.Text = request.Message;
            MessageText.Visibility = Visibility.Visible;
        }
        CancelBtn.Content = Loc.T("Cancel");
        AllowBtn.Content = Loc.T("Allow");
        HintText.Text = request.Destructive ? Loc.T("This cannot be undone.") : "";
        if (request.Destructive)
        {
            IconBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C));
            IconGlyph.Glyph = "";
        }
        else
        {
            IconBadge.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            IconGlyph.Glyph = "";
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        Root.KeyDown += OnKey;
        Closed += (_, _) => { if (!_decided) { _decided = true; _result.TrySetResult(false); } };

        // Centre on the HUD display; wait one layout pass so the DPI of that monitor is known.
        var display = Displays.Resolve(App.Store.Current.Appearance.HudDisplay);
        AppWindow.MoveAndResize(new RectInt32(display.X + 20, display.Y + 20, 10, 10));
        var scale = Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0);
        var w = (int)(480 * scale);
        var h = (int)((request.Message.Length > 90 ? 320 : 230) * scale);
        AppWindow.MoveAndResize(new RectInt32(display.X + (display.Width - w) / 2, display.Y + (display.Height - h) / 2, w, h));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    public void Present()
    {
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
        (IsDestructive ? CancelBtn : AllowBtn).Focus(FocusState.Programmatic);
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape) Decide(false);
    }

    private void Allow_Click(object sender, RoutedEventArgs e) => Decide(true);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Decide(false);

    public void Decide(bool allowed)
    {
        if (_decided) return;
        _decided = true;
        _result.TrySetResult(allowed);
        Close();
    }
}
