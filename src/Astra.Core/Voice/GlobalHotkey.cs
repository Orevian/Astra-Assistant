using System.Runtime.InteropServices;

namespace Astra.Core.Voice;

/// <summary>
/// Global push-to-talk hotkey via a low-level keyboard hook. Must be created on a thread that pumps messages (the UI thread).
/// Reports both press and release, which RegisterHotKey cannot.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

    private readonly HookProc _proc;   // kept alive so the GC doesn't collect the delegate
    private IntPtr _hook;
    private readonly HashSet<int> _down = new();
    private HashSet<int> _combo = new();
    private bool _active;

    public event Action? Pressed;
    public event Action? Released;

    public GlobalHotkey() => _proc = Callback;

    public string Combo { get; private set; } = "";

    /// <summary>"Ctrl+Alt+Space", "F9", "Win+Shift+A"…</summary>
    public bool SetCombo(string combo)
    {
        if (!Control.InputController.TryParseChord(combo, out var keys, out _)) return false;
        _combo = keys.Select(k => Normalize(k)).ToHashSet();
        Combo = combo;
        if (_hook == IntPtr.Zero)
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    // Left/right modifier variants collapse to the generic virtual keys that TryParseChord produces.
    private static int Normalize(int vk) => vk switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, 0x5C => 0x5B, _ => vk };

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _combo.Count > 0)
        {
            var vk = Normalize(Marshal.ReadInt32(lParam));
            var msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                _down.Add(vk);
                if (!_active && _combo.IsSubsetOf(_down)) { _active = true; Pressed?.Invoke(); }
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                _down.Remove(vk);
                if (_active && _combo.Contains(vk)) { _active = false; Released?.Invoke(); }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
