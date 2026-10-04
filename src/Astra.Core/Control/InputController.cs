using System.Runtime.InteropServices;
using Astra.Core.Native;
using static Astra.Core.Native.NativeMethods;

namespace Astra.Core.Control;

/// <summary>Mouse and keyboard via SendInput. All coordinates are physical pixels on the virtual desktop.</summary>
public sealed class InputController
{
    /// <summary>Raised whenever Astra moves the pointer, so the AI cursor overlay can follow.</summary>
    public event Action<int, int>? PointerMoved;

    public (int X, int Y) Position
    {
        get { GetCursorPos(out var p); return (p.X, p.Y); }
    }

    public async Task MoveMouseAsync(int x, int y, int durationMs = 350, CancellationToken ct = default)
    {
        var (sx, sy) = Position;
        var steps = Math.Max(1, durationMs / 12);
        for (var i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = i / (double)steps;
            t = t * t * (3 - 2 * t); // ease in-out
            var cx = (int)(sx + (x - sx) * t);
            var cy = (int)(sy + (y - sy) * t);
            SetCursorPos(cx, cy);
            PointerMoved?.Invoke(cx, cy);
            await Task.Delay(12, ct);
        }
        SetCursorPos(x, y);
        PointerMoved?.Invoke(x, y);
    }

    public async Task ClickAsync(int x, int y, string button = "left", int count = 1, CancellationToken ct = default)
    {
        await MoveMouseAsync(x, y, ct: ct);
        await Task.Delay(60, ct);
        var (down, up) = button.ToLowerInvariant() switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };
        for (var i = 0; i < count; i++)
        {
            Mouse(down); Mouse(up);
            if (i < count - 1) await Task.Delay(70, ct);
        }
    }

    public void Scroll(int notches)
    {
        var inp = new INPUT { type = INPUT_MOUSE, u = { mi = { mouseData = (uint)(notches * 120), dwFlags = MOUSEEVENTF_WHEEL } } };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    private static void Mouse(uint flags)
    {
        var inp = new INPUT { type = INPUT_MOUSE, u = { mi = { dwFlags = flags } } };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    // ---- Keyboard ----------------------------------------------------------

    public async Task TypeTextAsync(string text, int delayMs = 8, CancellationToken ct = default)
    {
        foreach (var ch in text)
        {
            ct.ThrowIfCancellationRequested();
            if (ch == '\n') { PressChord("enter"); }
            else
            {
                var down = new INPUT { type = INPUT_KEYBOARD, u = { ki = { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
                var up = new INPUT { type = INPUT_KEYBOARD, u = { ki = { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
                SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
            }
            if (delayMs > 0) await Task.Delay(delayMs, ct);
        }
    }

    private static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B, ["windows"] = 0x5B, ["meta"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20, ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["volume_up"] = 0xAF, ["volume_down"] = 0xAE, ["volume_mute"] = 0xAD, ["play_pause"] = 0xB3, ["next_track"] = 0xB0, ["prev_track"] = 0xB1,
        ["printscreen"] = 0x2C, ["capslock"] = 0x14, ["menu"] = 0x5D,
    };

    private static readonly HashSet<ushort> Extended = new() { 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x26, 0x28, 0x25, 0x27, 0x5B, 0x5D };

    /// <summary>Parses "ctrl+shift+t", "win+r", "f5", "a". Returns false (with a reason) for unknown keys.</summary>
    public static bool TryParseChord(string chord, out List<ushort> keys, out string error)
    {
        keys = new();
        error = "";
        foreach (var raw in chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Keys.TryGetValue(raw, out var vk)) keys.Add(vk);
            else if (raw.Length >= 2 && raw.Length <= 3 && (raw[0] is 'f' or 'F') && int.TryParse(raw[1..], out var n) && n is >= 1 and <= 24) keys.Add((ushort)(0x6F + n));
            else if (raw.Length == 1)
            {
                var scan = VkKeyScan(raw[0]);
                if (scan == -1) { error = $"Unknown key '{raw}'"; return false; }
                keys.Add((ushort)(scan & 0xFF));
            }
            else { error = $"Unknown key '{raw}'"; return false; }
        }
        if (keys.Count == 0) { error = "Empty key combination"; return false; }
        return true;
    }

    public bool PressChord(string chord)
    {
        if (!TryParseChord(chord, out var keys, out _)) return false;
        var inputs = new List<INPUT>();
        foreach (var k in keys) inputs.Add(Key(k, false));
        foreach (var k in Enumerable.Reverse(keys)) inputs.Add(Key(k, true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return true;
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = { ki = { wVk = vk, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (Extended.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0) } },
    };
}
