using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Astra.App.Overlays;

/// <summary>
/// A backdrop that paints nothing, so a borderless window shows only the XAML drawn on it.
/// ICompositionSupportsSystemBackdrop takes a Windows.UI.Composition brush, hence the system Compositor
/// (which needs a Windows.System dispatcher queue on this thread).
/// </summary>
internal sealed class TransparentTintBackdrop : SystemBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    private static IntPtr _controller;
    private static Windows.UI.Composition.Compositor? _compositor;

    private static void EnsureWindowsSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null || _controller != IntPtr.Zero) return;
        var options = new DispatcherQueueOptions { dwSize = Marshal.SizeOf<DispatcherQueueOptions>(), threadType = 2 /* current thread */, apartmentType = 0 };
        CreateDispatcherQueueController(options, out _controller);
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        EnsureWindowsSystemDispatcherQueue();
        _compositor ??= new Windows.UI.Composition.Compositor();
        target.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target) => target.SystemBackdrop = null;
}
