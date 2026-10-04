using System.Runtime.InteropServices;
using Astra.Core.Native;
using Astra.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Astra.App.Overlays;

/// <summary>
/// Turns a plain WinUI window into a borderless, transparent, always-on-top overlay that never takes focus
/// and (optionally) lets mouse clicks pass through. Positions it on a chosen display.
/// </summary>
internal sealed class OverlayHost
{
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    private const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    private const long WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_BORDER = 0x800000, WS_DLGFRAME = 0x400000;
    private const long WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_DLGMODALFRAME = 0x1, WS_EX_STATICEDGE = 0x20000;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);

    /// <summary>Per-pixel transparency needs both the empty backdrop and a fully extended DWM frame.</summary>
    private static void EnableTransparency(IntPtr hwnd)
    {
        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
    }

    public Window Window { get; }
    public IntPtr Hwnd { get; }
    public AppWindow AppWindow { get; }
    private readonly FrameworkElement _root;
    public bool IsVisible { get; private set; }
    public double HeightDip { get; private set; }

    public OverlayHost(Window window, FrameworkElement root, bool clickThrough = true)
    {
        Window = window;
        _root = root;
        Hwnd = WindowNative.GetWindowHandle(window);
        AppWindow = window.AppWindow;

        window.SystemBackdrop = new TransparentTintBackdrop();
        EnableTransparency(Hwnd);
        window.ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;

        // WinUI windows carry WS_DLGFRAME and WS_EX_WINDOWEDGE, which Windows draws as a visible frame around our transparent card.
        var style = GetWindowLongPtr(Hwnd, GWL_STYLE).ToInt64() & ~(WS_DLGFRAME | WS_SYSMENU);
        SetWindowLongPtr(Hwnd, GWL_STYLE, new IntPtr(style));

        var ex = (GetWindowLongPtr(Hwnd, GWL_EXSTYLE).ToInt64() & ~(WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME | WS_EX_STATICEDGE)) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
        SetWindowLongPtr(Hwnd, GWL_EXSTYLE, new IntPtr(ex));
        if (clickThrough) SetLayeredWindowAttributes(Hwnd, 0, 255, 2);
        SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); // refresh the non-client frame

        int none = 1;           // DWMWCP_DONOTROUND: our XAML card draws its own rounded corners
        DwmSetWindowAttribute(Hwnd, 33, ref none, sizeof(int));
        int noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(Hwnd, 34, ref noBorder, sizeof(int));
    }

    public void Show()
    {
        if (IsVisible) return;
        IsVisible = true;
        AppWindow.Show(false); // false = do not activate
        SetWindowPos(Hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // topmost, no move/size/activate
    }

    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        AppWindow.Hide();
    }

    /// <summary>
    /// Sizes the window to its content (width in DIPs) and anchors it to a corner of a display.
    /// Moves first so the window adopts that monitor's DPI before the physical size is computed.
    /// </summary>
    public void Place(DisplayInfo display, ScreenCorner corner, double widthDip, int marginDip = 16, double offsetDip = 0, double? fixedHeightDip = null)
    {
        _root.Width = widthDip;
        _root.Measure(new Size(widthDip, double.PositiveInfinity));
        var heightDip = fixedHeightDip ?? Math.Ceiling(_root.DesiredSize.Height);
        if (heightDip < 1) heightDip = 1;
        HeightDip = heightDip;

        // Land on the target monitor first, then read the DPI Windows actually applies there.
        AppWindow.MoveAndResize(new RectInt32(display.X + 10, display.Y + 10, 8, 8));
        var scale = Math.Max(1.0, GetDpiForWindow(Hwnd) / 96.0);
        var w = (int)Math.Ceiling(widthDip * scale);
        var h = (int)Math.Ceiling(heightDip * scale);
        var (x, y) = Displays.Place(display, corner, w, h, marginDip, (int)(offsetDip * scale));
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        if (IsVisible) RaiseToTop();
    }

    /// <summary>Re-asserts "always on top" so the overlay stays above other topmost windows (e.g. a full-screen video).</summary>
    public void RaiseToTop() => SetWindowPos(Hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);

    /// <summary>Positions at an absolute physical-pixel point (used by the AI cursor).</summary>
    public void MoveTo(int x, int y, int widthPx, int heightPx) => AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));

    public double CurrentScale => Math.Max(1.0, GetDpiForWindow(Hwnd) / 96.0);
}
