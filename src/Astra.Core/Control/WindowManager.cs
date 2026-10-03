using System.Diagnostics;
using System.Text;
using Astra.Core.Index;
using static Astra.Core.Native.NativeMethods;

namespace Astra.Core.Control;

public sealed record WindowInfo(long Handle, string Title, int ProcessId, string ProcessName, bool Minimized, bool Foreground,
    int X, int Y, int Width, int Height);

public sealed class WindowManager
{
    public List<WindowInfo> List(bool includeUntitled = false)
    {
        var result = new List<WindowInfo>();
        var fg = GetForegroundWindow();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var len = GetWindowTextLength(h);
            if (len == 0 && !includeUntitled) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            GetWindowThreadProcessId(h, out var pid);
            string pname = "";
            try { pname = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (pname is "ApplicationFrameHost" && title.Length == 0) return true;
            GetWindowRect(h, out var r);
            if (r.Width <= 1 && r.Height <= 1) return true;
            result.Add(new WindowInfo(h.ToInt64(), title, (int)pid, pname, IsIconic(h), h == fg, r.Left, r.Top, r.Width, r.Height));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public WindowInfo? Find(string? titleOrProcess)
    {
        if (string.IsNullOrWhiteSpace(titleOrProcess)) return List().FirstOrDefault(w => w.Foreground);
        var q = TextNorm.Normalize(titleOrProcess);
        return List()
            .Select(w => (w, score: Math.Max(TextNorm.Similarity(q, TextNorm.Normalize(w.Title)), TextNorm.Similarity(q, TextNorm.Normalize(w.ProcessName)))))
            .Where(x => x.score >= 0.5).OrderByDescending(x => x.score).Select(x => x.w).FirstOrDefault();
    }

    public WindowInfo? Foreground() => List().FirstOrDefault(w => w.Foreground);

    public bool Focus(long handle)
    {
        var h = new IntPtr(handle);
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        // Windows blocks foreground changes from background processes; attaching to the foreground thread's input queue works around it.
        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var me = GetCurrentThreadId();
        var attached = fgThread != me && AttachThreadInput(me, fgThread, true);
        var ok = SetForegroundWindow(h);
        if (attached) AttachThreadInput(me, fgThread, false);
        return ok && GetForegroundWindow() == h;
    }

    public bool Minimize(long handle) => ShowWindow(new IntPtr(handle), SW_MINIMIZE);
    public bool Maximize(long handle) => ShowWindow(new IntPtr(handle), SW_MAXIMIZE);
    public bool Restore(long handle) => ShowWindow(new IntPtr(handle), SW_RESTORE);
    public bool Close(long handle) => PostMessage(new IntPtr(handle), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Polls until a visible window satisfying the predicate appears.</summary>
    public async Task<WindowInfo?> WaitForWindowAsync(Func<WindowInfo, bool> predicate, int timeoutMs, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var w = List().FirstOrDefault(predicate);
            if (w is not null) return w;
            await Task.Delay(250, ct);
        }
        return null;
    }
}
