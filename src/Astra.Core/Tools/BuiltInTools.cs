using System.Text.Json.Nodes;
using Astra.Core.Assistant;
using Astra.Core.Control;
using Astra.Core.Index;
using Astra.Core.Localization;

namespace Astra.Core.Tools;

/// <summary>Declares a tool from delegates so each tool stays a few lines long.</summary>
public sealed class FuncTool : Tool
{
    private readonly string _desc;
    private readonly Func<JsonObject, ToolContext, Task<ToolResult>> _exec;
    private readonly Func<JsonObject, ToolContext, string?>? _validate;
    private readonly Func<JsonObject, string>? _describe;
    private readonly Func<JsonObject, ToolContext, string?>? _confirm;

    public FuncTool(string name, string description, JsonObject schema, Func<JsonObject, ToolContext, Task<ToolResult>> exec,
        PermissionCategory category = PermissionCategory.None, bool destructive = false,
        Func<JsonObject, ToolContext, string?>? validate = null, Func<JsonObject, string>? describe = null,
        Func<JsonObject, ToolContext, string?>? confirm = null)
    {
        Name = name; _desc = description; Schema = schema; _exec = exec; Category = category; Destructive = destructive;
        _validate = validate; _describe = describe; _confirm = confirm;
    }

    public override string Name { get; }
    public override string Description => _desc;
    public override JsonObject Schema { get; }
    public override PermissionCategory Category { get; }
    public override bool Destructive { get; }
    public override string? Validate(JsonObject args, ToolContext ctx) => _validate?.Invoke(args, ctx);
    public override string? ConfirmationText(JsonObject args, ToolContext ctx) => _confirm?.Invoke(args, ctx);
    public override string Describe(JsonObject args) => _describe?.Invoke(args) ?? Name;
    public override Task<ToolResult> ExecuteAsync(JsonObject args, ToolContext ctx) => _exec(args, ctx);
}

public static partial class BuiltInTools
{
    private static readonly PermissionCategory A = PermissionCategory.Applications, B = PermissionCategory.Browser,
        F = PermissionCategory.Files, I = PermissionCategory.Input, T = PermissionCategory.Terminal,
        S = PermissionCategory.System, P = PermissionCategory.Power;

    public static IEnumerable<Tool> Create()
    {
        foreach (var t in AppAndSearchTools()) yield return t;
        foreach (var t in BrowserTools()) yield return t;
        foreach (var t in InputTools()) yield return t;
        foreach (var t in FileTools()) yield return t;
        foreach (var t in SystemTools()) yield return t;
    }

    // ---- Helpers -----------------------------------------------------------

    internal static AppHit? ResolveApp(AstraRuntime rt, JsonObject args, out string? error)
    {
        error = null;
        var id = SearchService.ParseAppId(args.Str("app_id"));
        if (id is not null)
        {
            var byId = rt.Index.Search.GetApplication(id.Value);
            if (byId is not null) return byId;
        }
        var name = args.Str("name") ?? args.Str("app_id");
        if (name is null) { error = "Provide app_id (from search_applications) or name."; return null; }
        var hit = rt.Index.Search.SearchApplications(name, 3).FirstOrDefault();
        if (hit is null || hit.Score < 0.6)
        {
            error = rt.Index.HasIndex
                ? $"No application matching '{name}' was found in the computer index."
                : "The computer index is empty. Ask the user to run Settings ▸ Computer index ▸ Scan Windows.";
            return null;
        }
        return hit;
    }

    private static JsonArray Rows<TRow>(IEnumerable<TRow> items, Func<TRow, JsonObject> map)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add(map(i));
        return a;
    }

    // ---- Applications & local search --------------------------------------

    private static IEnumerable<Tool> AppAndSearchTools()
    {
        yield return new FuncTool("search_applications", "Find installed apps, games or browsers by name in the local index. Returns ids for open_application.",
            Args.Schema(("query", "string", "App name, e.g. Valorant", true)),
            (a, c) =>
            {
                var hits = c.Runtime.Index.Search.SearchApplications(a.Str("query")!, 5);
                if (hits.Count == 0)
                    return Task.FromResult(ToolResult.Fail(c.Runtime.Index.HasIndex ? "No matching application." : "The computer index is empty; ask the user to run a scan."));
                return Task.FromResult(ToolResult.Success($"{hits.Count} match(es).",
                    Rows(hits, h => new JsonObject { ["id"] = h.AppId, ["name"] = h.Name, ["kind"] = h.Kind, ["score"] = Math.Round(h.Score, 2) })));
            },
            validate: (a, _) => a.Str("query") is null ? "query is required" : null,
            describe: a => Loc.F("Looking for {0}…", a.Str("query") ?? ""));

        yield return new FuncTool("open_application", "Launch an installed app/game/browser. Pass app_id from search_applications, or a name.",
            Args.Schema(("app_id", "string", "Id such as app_42", false), ("name", "string", "App name if no id", false)),
            async (a, c) =>
            {
                var app = ResolveApp(c.Runtime, a, out var err);
                if (app is null) return ToolResult.Fail(err!);
                var r = await c.Runtime.Apps.LaunchAsync(app, c.Ct);
                return r.Started ? ToolResult.Success(r.Message, null, r.Verified) : ToolResult.Fail(r.Message);
            }, A,
            describe: a => Loc.F("Opening {0}…", a.Str("name") ?? a.Str("app_id") ?? ""));

        yield return new FuncTool("close_application", "Close a running app. Set force=true only if it refuses to close.",
            Args.Schema(("app_id", "string", "Id from search_applications", false), ("name", "string", "App or process name", false), ("force", "boolean", "Terminate forcefully", false)),
            async (a, c) =>
            {
                var app = ResolveApp(c.Runtime, a, out _);
                var name = a.Str("name") ?? a.Str("app_id");
                var r = await c.Runtime.Apps.CloseAsync(app, name, a.Bool("force"), c.Ct);
                return r.Closed ? ToolResult.Success(r.Message) : ToolResult.Fail(r.Message);
            }, A,
            confirm: (a, _) => a.Bool("force") ? Loc.F("Astra wants to force-close {0}. Unsaved work may be lost.", a.Str("name") ?? a.Str("app_id") ?? "") : null,
            describe: a => Loc.F("Closing {0}…", a.Str("name") ?? a.Str("app_id") ?? ""));

        yield return new FuncTool("search_files", "Search the local file index (names, not contents). Returns paths.",
            Args.Schema(("query", "string", "Words in the file name", false), ("location", "string", "Downloads, Documents, Desktop, Pictures, Music, Videos or a folder path", false),
                ("extension", "string", "e.g. pdf", false), ("date_range", "string", "today, yesterday, last_week, last_month, last_year", false),
                ("limit", "integer", "Max results (default 8)", false)),
            (a, c) =>
            {
                var hits = c.Runtime.Index.Search.SearchFiles(a.Str("query"), a.Str("location"), a.Str("extension"), a.Str("date_range"), Math.Clamp(a.Int("limit") ?? 8, 1, 25));
                return Task.FromResult(hits.Count == 0 ? ToolResult.Fail("No matching files.")
                    : ToolResult.Success($"{hits.Count} file(s).", Rows(hits, h => new JsonObject { ["path"] = h.Path, ["kb"] = h.Size / 1024, ["modified"] = h.Modified.ToString("yyyy-MM-dd") })));
            },
            validate: (a, _) => a.Str("query") is null && a.Str("location") is null && a.Str("extension") is null && a.Str("date_range") is null ? "Give at least one filter." : null,
            describe: a => Loc.F("Searching files: {0}", a.Str("query") ?? a.Str("extension") ?? a.Str("location") ?? ""));

        yield return new FuncTool("search_folders", "Search the local folder index by name. Returns paths.",
            Args.Schema(("query", "string", "Words in the folder name", true), ("location", "string", "Optional parent location", false)),
            (a, c) =>
            {
                var hits = c.Runtime.Index.Search.SearchFolders(a.Str("query"), a.Str("location"), 8);
                return Task.FromResult(hits.Count == 0 ? ToolResult.Fail("No matching folders.")
                    : ToolResult.Success($"{hits.Count} folder(s).", Rows(hits, h => new JsonObject { ["path"] = h.Path })));
            },
            validate: (a, _) => a.Str("query") is null ? "query is required" : null,
            describe: a => Loc.F("Searching folders: {0}", a.Str("query") ?? ""));

        yield return new FuncTool("get_processes", "List running processes (optionally filtered by name or window title).",
            Args.Schema(("filter", "string", "Part of the name or title", false)),
            (a, _) =>
            {
                var list = SystemInfoService.Processes(a.Str("filter"), 15);
                return Task.FromResult(list.Count == 0 ? ToolResult.Fail("No matching process.")
                    : ToolResult.Success($"{list.Count} process(es).", Rows(list, p => new JsonObject { ["pid"] = p.Pid, ["name"] = p.Name, ["title"] = p.Title, ["mb"] = p.MemoryMb })));
            }, describe: a => Loc.T("Checking running processes…"));

        yield return new FuncTool("get_window", "Describe open windows. With title: the best match; without: the foreground window plus visible windows.",
            Args.Schema(("title", "string", "Window title or process name", false)),
            (a, c) =>
            {
                var title = a.Str("title");
                var wins = title is null ? c.Runtime.Windows.List().Take(12).ToList()
                    : (c.Runtime.Windows.Find(title) is { } w ? new List<WindowInfo> { w } : new());
                return Task.FromResult(wins.Count == 0 ? ToolResult.Fail("No matching window.")
                    : ToolResult.Success($"{wins.Count} window(s).", Rows(wins, w => new JsonObject { ["title"] = w.Title, ["process"] = w.ProcessName, ["foreground"] = w.Foreground, ["minimized"] = w.Minimized })));
            }, describe: a => Loc.T("Looking at open windows…"));

        yield return new FuncTool("window_action", "Focus, minimize, maximize, restore or close a window found by title/process.",
            Args.Schema(("title", "string", "Window title or process name", true), ("action", "focus|minimize|maximize|restore|close", "What to do", true)),
            (a, c) =>
            {
                var w = c.Runtime.Windows.Find(a.Str("title"));
                if (w is null) return Task.FromResult(ToolResult.Fail("No matching window."));
                var action = a.Str("action") ?? "focus";
                var ok = action switch
                {
                    "minimize" => c.Runtime.Windows.Minimize(w.Handle),
                    "maximize" => c.Runtime.Windows.Maximize(w.Handle),
                    "restore" => c.Runtime.Windows.Restore(w.Handle),
                    "close" => c.Runtime.Windows.Close(w.Handle),
                    _ => c.Runtime.Windows.Focus(w.Handle),
                };
                return Task.FromResult(ok ? ToolResult.Success($"{action}: {w.Title}") : ToolResult.Fail($"Could not {action} “{w.Title}”."));
            }, A, describe: a => Loc.F("Window: {0} {1}", a.Str("action") ?? "focus", a.Str("title") ?? ""));

        yield return new FuncTool("get_browser", "Resolve a browser (by name, or the preferred/default one) and list its profiles.",
            Args.Schema(("name", "string", "Browser name, e.g. Avast Secure Browser", false)),
            (a, c) =>
            {
                var b = c.Runtime.Browser.ResolveBrowser(a.Str("name"));
                if (b is null) return Task.FromResult(ToolResult.Fail("No browser found in the index."));
                var profiles = c.Runtime.Index.Search.BrowserProfiles(b.Name).Select(p => p.ProfileName).ToArray();
                return Task.FromResult(ToolResult.Success(b.Name, new JsonObject { ["id"] = b.AppId, ["name"] = b.Name, ["chromium"] = BrowserHelpers.IsChromium(b), ["profiles"] = ToNode(profiles) }));
            }, describe: a => Loc.T("Finding the browser…"));

        yield return new FuncTool("get_system_information", "OS, CPU, RAM, drives, displays, uptime and current time.",
            Args.Schema(), (_, _) =>
            {
                var d = new JsonObject();
                foreach (var (k, v) in SystemInfoService.Get()) d[k] = ToNode(v);
                return Task.FromResult(ToolResult.Success("System information.", d));
            }, describe: _ => Loc.T("Reading system information…"));
    }

    internal static JsonNode? ToNode(object? v) =>
        System.Text.Json.JsonSerializer.SerializeToNode(v);
}

internal static class BrowserHelpers
{
    public static bool IsChromium(AppHit b) => Browser.BrowserController.IsChromium(b.ExePath);
}
