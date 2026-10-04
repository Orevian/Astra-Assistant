using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Astra.Core.Native;
using Microsoft.VisualBasic.FileIO;
using static Astra.Core.Native.NativeMethods;

namespace Astra.Core.Control;

public static class ClipboardService
{
    public static string? GetText()
    {
        if (!TryOpen()) return null;
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            var p = GlobalLock(h);
            try { return Marshal.PtrToStringUni(p); } finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    public static bool SetText(string text)
    {
        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            var p = GlobalLock(h);
            Marshal.Copy((text + "\0").ToCharArray(), 0, p, text.Length + 1);
            GlobalUnlock(h);
            return SetClipboardData(CF_UNICODETEXT, h) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }

    private static bool TryOpen()
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}

public sealed record FileOpResult(bool Ok, string Message);

/// <summary>File operations with guard rails around system locations. Deletion goes to the Recycle Bin.</summary>
public static class FileOps
{
    private static readonly string[] Protected =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    };

    public static bool IsProtected(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (full.Length <= 3) return true; // drive roots
        return Protected.Any(p => p.Length > 0 && (full.Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                                  || full.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));
    }

    public static string Expand(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(Index.SearchService.ResolveLocation(path) ?? path));

    public static FileOpResult Create(string path, string content, bool overwrite)
    {
        var full = Expand(path);
        if (IsProtected(full)) return new(false, "That location is protected.");
        if (File.Exists(full) && !overwrite) return new(false, "File already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return File.Exists(full) ? new(true, $"Created {full} ({new FileInfo(full).Length} bytes).") : new(false, "File was not created.");
    }

    public static (bool Ok, string Content, bool Truncated) Read(string path, int maxChars)
    {
        var full = Expand(path);
        if (!File.Exists(full)) return (false, "File not found.", false);
        var info = new FileInfo(full);
        if (info.Length > 20 * 1024 * 1024) return (false, "File is too large to read (over 20 MB).", false);
        var text = File.ReadAllText(full);
        return text.Length > maxChars ? (true, text[..maxChars], true) : (true, text, false);
    }

    public static FileOpResult Move(string source, string destination)
    {
        var src = Expand(source);
        var dst = Expand(destination);
        if (IsProtected(src) || IsProtected(dst)) return new(false, "That location is protected.");
        var isDir = Directory.Exists(src);
        if (!isDir && !File.Exists(src)) return new(false, "Source not found.");
        // Moving into an existing folder keeps the original name.
        if (Directory.Exists(dst)) dst = Path.Combine(dst, Path.GetFileName(src.TrimEnd('\\')));
        if (File.Exists(dst) || Directory.Exists(dst)) return new(false, $"{dst} already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (isDir) Directory.Move(src, dst); else File.Move(src, dst);
        return (isDir ? Directory.Exists(dst) : File.Exists(dst)) ? new(true, $"Moved to {dst}.") : new(false, "Move could not be verified.");
    }

    public static FileOpResult Copy(string source, string destination)
    {
        var src = Expand(source);
        var dst = Expand(destination);
        if (IsProtected(dst)) return new(false, "That location is protected.");
        if (Directory.Exists(src))
        {
            if (Directory.Exists(dst)) dst = Path.Combine(dst, Path.GetFileName(src.TrimEnd('\\')));
            FileSystem.CopyDirectory(src, dst, false);
            return Directory.Exists(dst) ? new(true, $"Copied to {dst}.") : new(false, "Copy could not be verified.");
        }
        if (!File.Exists(src)) return new(false, "Source not found.");
        if (Directory.Exists(dst)) dst = Path.Combine(dst, Path.GetFileName(src));
        if (File.Exists(dst)) return new(false, $"{dst} already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst);
        return File.Exists(dst) ? new(true, $"Copied to {dst}.") : new(false, "Copy could not be verified.");
    }

    public static FileOpResult Delete(string path)
    {
        var full = Expand(path);
        if (IsProtected(full)) return new(false, "That location is protected and cannot be deleted.");
        if (Directory.Exists(full)) FileSystem.DeleteDirectory(full, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else if (File.Exists(full)) FileSystem.DeleteFile(full, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else return new(false, "Not found.");
        return !File.Exists(full) && !Directory.Exists(full) ? new(true, "Moved to the Recycle Bin.") : new(false, "Item still exists.");
    }

    public static List<string> List(string path, int max)
    {
        var full = Expand(path);
        if (!Directory.Exists(full)) return new();
        return Directory.EnumerateFileSystemEntries(full).Take(max)
            .Select(e => Directory.Exists(e) ? Path.GetFileName(e) + "\\" : Path.GetFileName(e)).ToList();
    }
}

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

public static class ShellRunner
{
    public static async Task<CommandResult> RunAsync(string command, string shell, int timeoutSec, CancellationToken ct)
    {
        var psi = shell.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/d /s /c \"{command}\"")
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command } };
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.UTF8;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSec, 1, 300)));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
            return new CommandResult(-1, "", true);
        }
        var text = (await stdout + (await stderr is { Length: > 0 } e ? "\n" + e : "")).Trim();
        return new CommandResult(p.ExitCode, text, false);
    }
}

public static class SystemInfoService
{
    public static Dictionary<string, object> Get()
    {
        var info = new Dictionary<string, object>
        {
            ["machine"] = Environment.MachineName,
            ["user"] = Environment.UserName,
            ["os"] = RuntimeInformation.OSDescription,
            ["cpu_logical_cores"] = Environment.ProcessorCount,
            ["ram_total_gb"] = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 1),
            ["uptime_hours"] = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, 1),
            ["displays"] = Displays.All().Select(d => $"{d.Label}").ToArray(),
            ["drives"] = DriveInfo.GetDrives().Where(d => d.IsReady)
                .Select(d => $"{d.Name} {d.DriveFormat} {d.AvailableFreeSpace / 1073741824}GB free of {d.TotalSize / 1073741824}GB").ToArray(),
            ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm dddd"),
        };
        return info;
    }

    public static List<(int Pid, string Name, string? Title, double MemoryMb)> Processes(string? filter, int max)
    {
        var f = Index.TextNorm.Normalize(filter);
        return Process.GetProcesses()
            .Select(p => { var t = ""; try { t = p.MainWindowTitle; } catch { } return (p.Id, p.ProcessName, Title: (string?)t, Mem: Math.Round(p.WorkingSet64 / 1048576.0, 0)); })
            .Where(p => f.Length == 0 || Index.TextNorm.Normalize(p.ProcessName).Contains(f) || Index.TextNorm.Normalize(p.Title).Contains(f))
            .OrderByDescending(p => !string.IsNullOrEmpty(p.Title)).ThenByDescending(p => p.Mem)
            .Take(max).Select(p => (p.Id, p.ProcessName, p.Title, p.Mem)).ToList();
    }
}

public sealed record Screenshot(byte[] Png, int Width, int Height, int OriginX, int OriginY, double Scale);

public static class ScreenCapture
{
    /// <summary>Captures a display (or the whole virtual desktop). The image is downscaled to at most maxWidth for model input.</summary>
    public static Screenshot Capture(string? displayDevice = null, int maxWidth = 1600, bool allScreens = false)
    {
        int x, y, w, h;
        if (allScreens)
        {
            x = GetSystemMetrics(76); y = GetSystemMetrics(77); w = GetSystemMetrics(78); h = GetSystemMetrics(79);
        }
        else
        {
            var d = Displays.Resolve(displayDevice);
            (x, y, w, h) = (d.X, d.Y, d.Width, d.Height);
        }
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);

        var scale = w > maxWidth ? maxWidth / (double)w : 1.0;
        using var outBmp = scale < 1 ? new Bitmap(bmp, (int)(w * scale), (int)(h * scale)) : (Bitmap)bmp.Clone();
        using var ms = new MemoryStream();
        outBmp.Save(ms, ImageFormat.Png);
        return new Screenshot(ms.ToArray(), outBmp.Width, outBmp.Height, x, y, scale);
    }

    /// <summary>Mean absolute pixel difference (0..1) between two screenshots, used to verify that something changed.</summary>
    public static double Difference(Screenshot a, Screenshot b)
    {
        using var ba = new Bitmap(new MemoryStream(a.Png));
        using var bb = new Bitmap(new MemoryStream(b.Png));
        var w = Math.Min(ba.Width, bb.Width) / 8;
        var h = Math.Min(ba.Height, bb.Height) / 8;
        if (w == 0 || h == 0) return 0;
        using var sa = new Bitmap(ba, w, h);
        using var sb = new Bitmap(bb, w, h);
        double sum = 0;
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
            {
                var p = sa.GetPixel(i, j); var q = sb.GetPixel(i, j);
                sum += (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B)) / 765.0;
            }
        return sum / (w * h);
    }
}
