using System.Runtime.InteropServices;
using Astra.App.Overlays;
using Astra.Core.Localization;
using Astra.Core.Settings;

namespace Astra.App;

/// <summary>Notification-area icon with the Astra menu. Uses a hidden message window; no WinForms.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 1, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONUP = 0x205, WM_NULL = 0, WM_DESTROY = 2;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_NONOTIFY = 0x80;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? n);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
    private delegate int SetPreferredAppModeFn(int mode);
    private delegate void FlushMenuThemesFn();

    private readonly WndProc _proc;   // kept alive for the native window
    private IntPtr _hwnd, _icon;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _added;

    public event Action? OpenRequested, ToggleListeningRequested, TogglePauseRequested, SettingsRequested, PermissionsRequested, ExitRequested;
    public Func<bool> IsListening { get; set; } = () => false;
    public Func<bool> IsPaused { get; set; } = () => false;

    public TrayIcon() => _proc = HandleMessage;

    public void Show(AppearanceSettings appearance)
    {
        if (_hwnd == IntPtr.Zero)
        {
            var cls = "AstraTrayWindow";
            var wc = new WNDCLASS { lpfnWndProc = _proc, hInstance = GetModuleHandle(null), lpszClassName = cls };
            RegisterClass(ref wc);
            _hwnd = CreateWindowEx(0, cls, "Astra", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            // Native popup menus follow the system dark mode only when asked (uxtheme ordinals 135/136).
            try
            {
                var ux = LoadLibrary("uxtheme.dll");
                var set = GetProcAddress(ux, new IntPtr(135));
                if (set != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(set)(1 /* AllowDark */);
                var flush = GetProcAddress(ux, new IntPtr(136));
                if (flush != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush)();
            }
            catch { }
        }
        UpdateIcon(appearance);
    }

    public void UpdateIcon(AppearanceSettings appearance)
    {
        var path = IconFactory.ActiveFile(appearance);
        IntPtr newIcon = IntPtr.Zero;
        try
        {
            if (path is not null && path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) newIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            else if (path is not null && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var bmp = new System.Drawing.Bitmap(path);
                newIcon = bmp.GetHicon();
            }
        }
        catch { }
        if (newIcon == IntPtr.Zero) newIcon = LoadImage(IntPtr.Zero, IconFactory.EnsureIconFile(appearance.AccentColor), IMAGE_ICON, 32, 32, LR_LOADFROMFILE);

        var old = _icon;
        _icon = newIcon;
        var data = Data();
        Shell_NotifyIcon(_added ? NIM_MODIFY : NIM_ADD, ref data);
        _added = true;
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1, uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_APP_TRAY, hIcon = _icon, szTip = App.Store.Current.General.AssistantName,
    };

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            var m = (int)(lParam.ToInt64() & 0xFFFF);
            if (m is WM_LBUTTONUP or WM_LBUTTONDBLCLK) OpenRequested?.Invoke();
            else if (m == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _added)
        {
            var d = Data();
            Shell_NotifyIcon(NIM_ADD, ref d);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, 1, Loc.F("Open {0}", App.Store.Current.General.AssistantName));
        AppendMenu(menu, MF_STRING, 2, IsListening() ? Loc.T("Stop Listening") : Loc.T("Start Listening"));
        AppendMenu(menu, MF_STRING | (IsPaused() ? MF_CHECKED : 0), 3, Loc.T("Pause Assistant"));
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, 4, Loc.T("Settings"));
        AppendMenu(menu, MF_STRING, 5, Loc.T("Permissions"));
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, 6, Loc.T("Exit"));

        GetCursorPos(out var p);
        SetForegroundWindow(_hwnd); // required so the menu closes when you click elsewhere
        var cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        switch (cmd)
        {
            case 1: OpenRequested?.Invoke(); break;
            case 2: ToggleListeningRequested?.Invoke(); break;
            case 3: TogglePauseRequested?.Invoke(); break;
            case 4: SettingsRequested?.Invoke(); break;
            case 5: PermissionsRequested?.Invoke(); break;
            case 6: ExitRequested?.Invoke(); break;
        }
    }

    public void Dispose()
    {
        if (_added) { var d = Data(); Shell_NotifyIcon(NIM_DELETE, ref d); _added = false; }
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }
}
