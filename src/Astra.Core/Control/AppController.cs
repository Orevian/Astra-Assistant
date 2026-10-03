using System.Diagnostics;
using Astra.Core.Index;

namespace Astra.Core.Control;

public sealed record LaunchResult(bool Started, bool Verified, string Message, int? ProcessId = null, string? WindowTitle = null);
public sealed record CloseResult(bool Closed, int Count, string Message);

/// <summary>Launches and closes applications that were resolved through the local index.</summary>
public sealed class AppController
{
    private readonly WindowManager _windows;
    public AppController(WindowManager windows) => _windows = windows;

    public async Task<LaunchResult> LaunchAsync(AppHit app, CancellationToken ct = default)
    {
        var before = SnapshotWindows();
        var started = DateTime.Now;
        try
        {
            switch (app.LaunchKind)
            {
                case "uwp":
                    Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.Launch}") { UseShellExecute = true });
                    break;
                default: // file (exe / lnk) or uri
                    Process.Start(new ProcessStartInfo(app.Launch) { UseShellExecute = true, WorkingDirectory = WorkDir(app) });
                    break;
            }
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, false, $"Could not start {app.Name}: {ex.Message}");
        }

        // Verify: a window of the right process (or with a matching title) must show up.
        var names = ExpectedProcessNames(app);
        var nameTokens = TextNorm.Tokens(app.Name);
        var win = await _windows.WaitForWindowAsync(w =>
        {
            if (before.Contains(w.Handle) && !w.Foreground) return false;
            if (names.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase)) return true;
            var t = TextNorm.Normalize(w.Title);
            return nameTokens.Length > 0 && nameTokens.All(tok => t.Contains(tok)) && !before.Contains(w.Handle);
        }, app.LaunchKind == "uri" ? 25000 : 12000, ct);

        if (win is not null) return new LaunchResult(true, true, $"{app.Name} is open.", win.ProcessId, win.Title);

        // Single-instance apps may already be running with a window that existed before launch.
        var existing = _windows.List().FirstOrDefault(w => names.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase));
        if (existing is not null) return new LaunchResult(true, true, $"{app.Name} is open.", existing.ProcessId, existing.Title);

        var running = names.Any(n => Process.GetProcessesByName(n).Any(p => p.StartTime >= started.AddSeconds(-2)));
        return running
            ? new LaunchResult(true, false, $"{app.Name} process started but no window appeared yet.")
            : new LaunchResult(true, false, $"Launched {app.Name}, but could not confirm it opened.");
    }

    private static string? WorkDir(AppHit app)
    {
        try { return app.ExePath is not null ? Path.GetDirectoryName(app.ExePath) : null; } catch { return null; }
    }

    private HashSet<long> SnapshotWindows() => _windows.List().Select(w => w.Handle).ToHashSet();

    private static List<string> ExpectedProcessNames(AppHit app)
    {
        var names = new List<string>();
        if (app.ExePath is not null) names.Add(Path.GetFileNameWithoutExtension(app.ExePath));
        else if (app.LaunchKind == "file")
        {
            var target = ResolveLnkTarget(app.Launch);
            if (target is not null) names.Add(Path.GetFileNameWithoutExtension(target));
            else names.Add(Path.GetFileNameWithoutExtension(app.Launch));
        }
        names.Add(TextNorm.Normalize(app.Name).Replace(" ", ""));
        return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ResolveLnkTarget(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            return shell.CreateShortcut(path).TargetPath as string;
        }
        catch { return null; }
    }

    public async Task<CloseResult> CloseAsync(AppHit? app, string? nameFallback, bool force, CancellationToken ct = default)
    {
        var names = app is not null ? ExpectedProcessNames(app) : new List<string> { TextNorm.Normalize(nameFallback).Replace(" ", "") };
        var procs = Process.GetProcesses()
            .Where(p => names.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)
                                       || TextNorm.Normalize(p.ProcessName).Replace(" ", "") == n))
            .ToList();
        if (procs.Count == 0)
        {
            // Store (UWP) apps run inside ApplicationFrameHost, so close them through their window instead.
            var tokens = TextNorm.Tokens(app?.Name ?? nameFallback);
            var wins = tokens.Length == 0 ? new List<WindowInfo>() : _windows.List()
                .Where(w => { var t = TextNorm.Normalize(w.Title); return tokens.All(tok => t.Contains(tok)); }).ToList();
            if (wins.Count == 0) return new CloseResult(false, 0, "No running process found for that application.");
            foreach (var w in wins) _windows.Close(w.Handle);
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(250, ct);
                var handles = _windows.List().Select(w => w.Handle).ToHashSet();
                if (wins.All(w => !handles.Contains(w.Handle))) return new CloseResult(true, wins.Count, $"Closed {wins.Count} window(s).");
            }
            return new CloseResult(false, 0, "The window did not close (it may be asking to save).");
        }

        foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
        for (var i = 0; i < 12 && procs.Any(p => !p.HasExited); i++) await Task.Delay(250, ct);
        var remaining = procs.Where(p => !p.HasExited).ToList();
        if (remaining.Count > 0 && force)
        {
            foreach (var p in remaining) { try { p.Kill(true); } catch { } }
            await Task.Delay(500, ct);
            remaining = procs.Where(p => { try { return !p.HasExited; } catch { return false; } }).ToList();
        }
        var closed = procs.Count - remaining.Count;
        return remaining.Count == 0
            ? new CloseResult(true, closed, $"Closed {closed} process(es).")
            : new CloseResult(false, closed, "The application did not close (it may be asking to save). Use force to terminate it.");
    }
}
