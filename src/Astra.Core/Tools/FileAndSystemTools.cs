using System.Diagnostics;
using System.Text.Json.Nodes;
using Astra.Core.Control;
using Astra.Core.Index;
using Astra.Core.Localization;

namespace Astra.Core.Tools;

public static partial class BuiltInTools
{
    /// <summary>Files for a bulk operation, found through the local index and re-checked on disk.</summary>
    private static List<string> MatchFiles(SearchService search, JsonObject a)
    {
        var hits = search.SearchFiles(a.Str("name_contains"), a.Str("source_folder") ?? a.Str("folder"), a.Str("extension"),
            a.Str("date_range"), 2000, a.Bool("include_subfolders"));
        var paths = hits.Select(h => h.Path).Where(File.Exists).ToList();
        if (a.Int("older_than_days") is { } days)
            paths = paths.Where(p => File.GetLastWriteTime(p) < DateTime.Now.AddDays(-days)).ToList();
        return paths;
    }


    private static IEnumerable<Tool> FileTools()
    {
        yield return new FuncTool("create_file", "Create a text file (Documents, Desktop… or a full path).",
            Args.Schema(("path", "string", "File path", true), ("content", "string", "Text content", false), ("overwrite", "boolean", "Replace if it exists", false)),
            (a, _) =>
            {
                var r = FileOps.Create(a.Str("path")!, a.Str("content") ?? "", a.Bool("overwrite"));
                return Task.FromResult(r.Ok ? ToolResult.Success(r.Message) : ToolResult.Fail(r.Message));
            }, F, validate: (a, _) => a.Str("path") is null ? "path is required" : null,
            describe: a => Loc.F("Creating {0}…", a.Str("path") ?? ""));

        yield return new FuncTool("read_file", "Read a text file's content (truncated). Only call when the content is needed.",
            Args.Schema(("path", "string", "File path", true), ("max_chars", "integer", "Limit, default 3000", false)),
            (a, _) =>
            {
                var (ok, content, truncated) = FileOps.Read(a.Str("path")!, Math.Clamp(a.Int("max_chars") ?? 3000, 100, 8000));
                return Task.FromResult(ok ? ToolResult.Success(truncated ? content + "…(truncated)" : content) : ToolResult.Fail(content));
            }, F, validate: (a, _) => a.Str("path") is null ? "path is required" : null,
            describe: a => Loc.F("Reading {0}…", a.Str("path") ?? ""));

        yield return new FuncTool("list_directory", "List the entries of a folder.",
            Args.Schema(("path", "string", "Folder path or Downloads/Documents/Desktop…", true)),
            (a, _) =>
            {
                var list = FileOps.List(a.Str("path")!, 40);
                return Task.FromResult(list.Count == 0 ? ToolResult.Fail("Folder is empty or not found.")
                    : ToolResult.Success($"{list.Count} entr(ies).", new JsonArray(list.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())));
            }, F, validate: (a, _) => a.Str("path") is null ? "path is required" : null,
            describe: a => Loc.F("Listing {0}…", a.Str("path") ?? ""));

        yield return new FuncTool("move_file", "Move or rename a file or folder.",
            Args.Schema(("source", "string", "Existing path", true), ("destination", "string", "New path or destination folder", true)),
            (a, _) =>
            {
                var r = FileOps.Move(a.Str("source")!, a.Str("destination")!);
                return Task.FromResult(r.Ok ? ToolResult.Success(r.Message) : ToolResult.Fail(r.Message));
            }, F, validate: (a, _) => a.Str("source") is null || a.Str("destination") is null ? "source and destination are required" : null,
            describe: a => Loc.F("Moving {0}…", a.Str("source") ?? ""));

        yield return new FuncTool("copy_file", "Copy a file or folder.",
            Args.Schema(("source", "string", "Existing path", true), ("destination", "string", "Destination path or folder", true)),
            (a, _) =>
            {
                var r = FileOps.Copy(a.Str("source")!, a.Str("destination")!);
                return Task.FromResult(r.Ok ? ToolResult.Success(r.Message) : ToolResult.Fail(r.Message));
            }, F, validate: (a, _) => a.Str("source") is null || a.Str("destination") is null ? "source and destination are required" : null,
            describe: a => Loc.F("Copying {0}…", a.Str("source") ?? ""));

        yield return new FuncTool("delete_file", "Delete a file or folder (sent to the Recycle Bin). The user must confirm.",
            Args.Schema(("path", "string", "Path to delete", true)),
            (a, _) =>
            {
                var r = FileOps.Delete(a.Str("path")!);
                return Task.FromResult(r.Ok ? ToolResult.Success(r.Message) : ToolResult.Fail(r.Message));
            }, F, destructive: true,
            validate: (a, _) => a.Str("path") is null ? "path is required" : FileOps.IsProtected(FileOps.Expand(a.Str("path")!)) ? "That location is protected and cannot be deleted." : null,
            confirm: (a, _) => Loc.F("Astra wants to delete {0}.", a.Str("path") ?? ""),
            describe: a => Loc.F("Deleting {0}…", a.Str("path") ?? ""));

        yield return new FuncTool("move_matching_files", "Move every file matching filters from a folder to another in one step (uses the local index; no need to list files first).",
            Args.Schema(("destination_folder", "string", "Where to move them", true), ("source_folder", "string", "Downloads, Desktop… or a path", true),
                ("extension", "string", "e.g. pdf", false), ("name_contains", "string", "Words in the file name", false),
                ("date_range", "string", "today, last_week, last_month…", false), ("include_subfolders", "boolean", "Default false", false)),
            (a, c) =>
            {
                var files = MatchFiles(c.Runtime.Index.Search, a);
                if (files.Count == 0) return Task.FromResult(ToolResult.Fail("No matching files."));
                var dest = FileOps.Expand(a.Str("destination_folder")!);
                Directory.CreateDirectory(dest);
                int moved = 0; var failed = new List<string>();
                foreach (var f in files)
                {
                    c.Ct.ThrowIfCancellationRequested();
                    var r = FileOps.Move(f, dest);
                    if (r.Ok) moved++; else failed.Add($"{Path.GetFileName(f)}: {r.Message}");
                }
                return Task.FromResult(moved == files.Count ? ToolResult.Success($"Moved {moved} file(s) to {dest}.")
                    : moved > 0 ? ToolResult.Success($"Moved {moved} of {files.Count} file(s). Problems: {string.Join("; ", failed.Take(3))}", null, false)
                    : ToolResult.Fail($"Nothing moved. {string.Join("; ", failed.Take(3))}"));
            }, F,
            validate: (a, _) => a.Str("destination_folder") is null || a.Str("source_folder") is null ? "source_folder and destination_folder are required"
                : a.Str("extension") is null && a.Str("name_contains") is null && a.Str("date_range") is null ? "Add a filter (extension, name_contains or date_range)." : null,
            confirm: (a, c) =>
            {
                var n = MatchFiles(c.Runtime.Index.Search, a).Count;
                return n >= 10 ? Loc.F("Astra wants to move {0} files from {1} to {2}.", n, a.Str("source_folder") ?? "", a.Str("destination_folder") ?? "") : null;
            },
            describe: a => Loc.F("Moving files to {0}…", a.Str("destination_folder") ?? ""));

        yield return new FuncTool("delete_matching_files", "Delete every file matching filters from a folder (Recycle Bin). The user must confirm the exact count.",
            Args.Schema(("folder", "string", "Downloads, Desktop… or a path", true), ("extension", "string", "e.g. tmp", false), ("name_contains", "string", "Words in the file name", false),
                ("older_than_days", "integer", "Only files older than this", false), ("include_subfolders", "boolean", "Default false", false)),
            (a, c) =>
            {
                var files = MatchFiles(c.Runtime.Index.Search, a);
                if (files.Count == 0) return Task.FromResult(ToolResult.Fail("No matching files."));
                int deleted = 0;
                foreach (var f in files) { c.Ct.ThrowIfCancellationRequested(); if (FileOps.Delete(f).Ok) deleted++; }
                return Task.FromResult(deleted == files.Count ? ToolResult.Success($"Moved {deleted} file(s) to the Recycle Bin.")
                    : ToolResult.Success($"Deleted {deleted} of {files.Count} file(s).", null, false));
            }, F, destructive: true,
            validate: (a, _) => a.Str("folder") is null ? "folder is required"
                : a.Str("extension") is null && a.Str("name_contains") is null && a.Int("older_than_days") is null ? "Add a filter (extension, name_contains or older_than_days)." : null,
            confirm: (a, c) => Loc.F("Astra wants to delete {0} files.", MatchFiles(c.Runtime.Index.Search, a).Count),
            describe: a => Loc.F("Deleting files in {0}…", a.Str("folder") ?? ""));

        yield return new FuncTool("open_path", "Open a file or folder with its default app / File Explorer.",
            Args.Schema(("path", "string", "File or folder path", true)),
            (a, _) =>
            {
                var full = FileOps.Expand(a.Str("path")!);
                if (!File.Exists(full) && !Directory.Exists(full)) return Task.FromResult(ToolResult.Fail("Path not found."));
                Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
                return Task.FromResult(ToolResult.Success($"Opened {full}."));
            }, F, validate: (a, _) => a.Str("path") is null ? "path is required" : null,
            describe: a => Loc.F("Opening {0}…", a.Str("path") ?? ""));
    }

    private static IEnumerable<Tool> SystemTools()
    {
        yield return new FuncTool("run_command", "Run a PowerShell (default) or cmd command and return its output. The user must confirm every command.",
            Args.Schema(("command", "string", "Command text", true), ("shell", "powershell|cmd", "Shell", false), ("timeout_seconds", "integer", "Default 30", false)),
            async (a, c) =>
            {
                var r = await ShellRunner.RunAsync(a.Str("command")!, a.Str("shell") ?? "powershell", a.Int("timeout_seconds") ?? 30, c.Ct);
                if (r.TimedOut) return ToolResult.Fail("The command timed out.");
                var output = r.Output.Length > 1500 ? r.Output[..1500] + "…(truncated)" : r.Output;
                return r.ExitCode == 0 ? ToolResult.Success(output.Length == 0 ? "Done (no output)." : output) : ToolResult.Fail($"Exit code {r.ExitCode}. {output}");
            }, T, validate: (a, _) => a.Str("command") is null ? "command is required" : null,
            confirm: (a, _) => Loc.F("Astra wants to run this command: {0}", a.Str("command") ?? ""),
            describe: _ => Loc.T("Running a command…"));

        yield return new FuncTool("set_volume", "Change system volume with the media keys.",
            Args.Schema(("action", "up|down|mute", "Volume action", true), ("steps", "integer", "Each step is 2%, default 5", false)),
            async (a, c) =>
            {
                var key = a.Str("action") switch { "down" => "volume_down", "mute" => "volume_mute", _ => "volume_up" };
                var steps = key == "volume_mute" ? 1 : Math.Clamp(a.Int("steps") ?? 5, 1, 50);
                for (var i = 0; i < steps; i++) { c.Runtime.Input.PressChord(key); await Task.Delay(30, c.Ct); }
                return ToolResult.Success($"Volume {a.Str("action")}.");
            }, S, describe: a => Loc.F("Volume {0}…", a.Str("action") ?? ""));

        yield return new FuncTool("power_action", "Lock, sleep, sign out, restart or shut down the PC. The user must confirm every time.",
            Args.Schema(("action", "lock|sleep|signout|restart|shutdown", "Power action", true)),
            (a, _) =>
            {
                var action = a.Str("action");
                (string file, string args)? cmd = action switch
                {
                    "lock" => ("rundll32.exe", "user32.dll,LockWorkStation"),
                    "sleep" => ("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
                    "signout" => ("shutdown.exe", "/l"),
                    "restart" => ("shutdown.exe", "/r /t 10"),
                    "shutdown" => ("shutdown.exe", "/s /t 10"),
                    _ => null,
                };
                if (cmd is null) return Task.FromResult(ToolResult.Fail("Unknown power action."));
                Process.Start(new ProcessStartInfo(cmd.Value.file, cmd.Value.args) { CreateNoWindow = true, UseShellExecute = false });
                return Task.FromResult(ToolResult.Success(action is "restart" or "shutdown" ? $"{action} starts in 10 seconds (shutdown /a cancels)." : $"{action} requested."));
            }, P, destructive: true, validate: (a, _) => a.Str("action") is null ? "action is required" : null,
            confirm: (a, _) => Loc.T(a.Str("action") switch
            {
                "lock" => "Astra wants to lock this computer.", "sleep" => "Astra wants to put this computer to sleep.",
                "signout" => "Astra wants to sign you out.", "restart" => "Astra wants to restart this computer.",
                _ => "Astra wants to shut down this computer.",
            }),
            describe: a => Loc.F("Power: {0}…", a.Str("action") ?? ""));

        yield return new FuncTool("remember", "Save a durable fact or preference the user asked you to remember. Never passwords, keys or card numbers.",
            Args.Schema(("text", "string", "The fact, one short sentence", true)),
            (a, c) =>
            {
                var (ok, msg) = c.Runtime.Memory.Add(a.Str("text")!);
                return Task.FromResult(ok ? ToolResult.Success(msg) : ToolResult.Fail(msg));
            }, validate: (a, _) => a.Str("text") is null ? "text is required" : null, describe: _ => Loc.T("Saving to memory…"));

        yield return new FuncTool("forget", "Delete saved memories that contain the given words.",
            Args.Schema(("query", "string", "Words identifying the memory", true)),
            (a, c) =>
            {
                var n = c.Runtime.Memory.Forget(a.Str("query")!);
                return Task.FromResult(n > 0 ? ToolResult.Success($"Forgot {n} memor{(n == 1 ? "y" : "ies")}.") : ToolResult.Fail("No matching memory."));
            }, validate: (a, _) => a.Str("query") is null ? "query is required" : null, describe: _ => Loc.T("Updating memory…"));

        yield return new FuncTool("wait", "Wait a few seconds (max 10), e.g. for a page or app to load.",
            Args.Schema(("seconds", "integer", "1-10", true)),
            async (a, c) => { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(a.Int("seconds") ?? 2, 1, 10)), c.Ct); return ToolResult.Success("Waited."); },
            describe: _ => Loc.T("Waiting…"));
    }
}
