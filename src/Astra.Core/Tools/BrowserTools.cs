using System.Text.Json.Nodes;
using Astra.Core.Browser;
using Astra.Core.Control;
using Astra.Core.Localization;

namespace Astra.Core.Tools;

public static partial class BuiltInTools
{
    private static ToolResult FromOpen(BrowserController.OpenResult r) =>
        r.Started ? ToolResult.Success(r.Message, null, r.Verified) : ToolResult.Fail(r.Message);

    private static IEnumerable<Tool> BrowserTools()
    {
        yield return new FuncTool("launch_url", "Open a URL or site name (youtube, github…) in a browser. Verifies the page title.",
            Args.Schema(("url", "string", "URL or well-known site name", true), ("browser", "string", "Browser name; omit for the preferred one", false)),
            async (a, c) =>
            {
                var url = BrowserController.NormalizeUrl(a.Str("url")!);
                return FromOpen(await c.Runtime.Browser.OpenUrlAsync(url, a.Str("browser"), null, c.Ct));
            }, B, validate: (a, _) => a.Str("url") is null ? "url is required" : null,
            describe: a => Loc.F("Opening {0}…", a.Str("url") ?? ""));

        yield return new FuncTool("browser_navigate", "Navigate to a URL. Uses the Astra-controlled browser session when one is open, otherwise your browser.",
            Args.Schema(("url", "string", "URL or site name", true), ("browser", "string", "Browser name", false)),
            async (a, c) =>
            {
                var url = BrowserController.NormalizeUrl(a.Str("url")!);
                if (c.Runtime.Browser.Cdp is { IsAlive: true } cdp)
                {
                    await cdp.NavigateAsync(url, c.Ct);
                    var snap = await cdp.SnapshotAsync(c.Ct);
                    return ToolResult.Success($"Opened “{snap.Title}”.", new JsonObject { ["title"] = snap.Title, ["url"] = snap.Url });
                }
                return FromOpen(await c.Runtime.Browser.OpenUrlAsync(url, a.Str("browser"), null, c.Ct));
            }, B, validate: (a, _) => a.Str("url") is null ? "url is required" : null,
            describe: a => Loc.F("Opening {0}…", a.Str("url") ?? ""));

        yield return new FuncTool("browser_search", "Open a web search RESULTS page (google, bing, duckduckgo, youtube, github, wikipedia, maps). Does not open or play a result; to play a video use play_youtube_video.",
            Args.Schema(("query", "string", "Search text", true), ("engine", "string", "Search engine, default google", false), ("browser", "string", "Browser name", false)),
            async (a, c) =>
            {
                var q = a.Str("query")!;
                var url = BrowserController.SearchUrl(q, a.Str("engine"));
                return FromOpen(await c.Runtime.Browser.OpenUrlAsync(url, a.Str("browser"), q, c.Ct));
            }, B, validate: (a, _) => a.Str("query") is null ? "query is required" : null,
            describe: a => Loc.F("Searching for “{0}”…", a.Str("query") ?? ""));

        yield return new FuncTool("play_youtube_video", "Play a video or song: searches YouTube and opens the first real video result in a browser. Use this whenever the user wants to play/open/watch/listen to something.",
            Args.Schema(("query", "string", "What to play, e.g. 'Misery Maroon 5'", true), ("browser", "string", "Browser name", false)),
            async (a, c) =>
            {
                var q = a.Str("query")!;
                var video = await c.Runtime.Browser.FirstYouTubeVideoAsync(q, c.Ct);
                if (video is null)
                {
                    var fallback = await c.Runtime.Browser.OpenUrlAsync(BrowserController.SearchUrl(q, "youtube"), a.Str("browser"), "YouTube", c.Ct);
                    return fallback.Started ? ToolResult.Success("Could not pick a result automatically; opened the YouTube results page.", null, false) : FromOpen(fallback);
                }
                var hint = video.Title.Length > 18 ? video.Title[..18] : video.Title;
                var r = await c.Runtime.Browser.OpenUrlAsync(video.Url, a.Str("browser"), hint, c.Ct);
                return r.Started ? ToolResult.Success($"Opened “{video.Title}”.", new JsonObject { ["title"] = video.Title, ["url"] = video.Url }, r.Verified) : FromOpen(r);
            }, B, validate: (a, _) => a.Str("query") is null ? "query is required" : null,
            describe: a => Loc.F("Finding “{0}” on YouTube…", a.Str("query") ?? ""));

        yield return new FuncTool("browser_read_page", "Read the current page of the Astra-controlled browser: text plus numbered clickable elements. Starts the session (a separate Astra browser profile) if needed.",
            Args.Schema(("url", "string", "Optional URL to open first", false), ("browser", "string", "Chromium browser name", false)),
            async (a, c) =>
            {
                var cdp = await c.Runtime.Browser.EnsureSessionAsync(a.Str("browser"), c.Ct);
                if (a.Str("url") is { } u) await cdp.NavigateAsync(BrowserController.NormalizeUrl(u), c.Ct);
                var snap = await cdp.SnapshotAsync(c.Ct);
                var text = snap.Text.Length > 1200 ? snap.Text[..1200] + "…" : snap.Text;
                return ToolResult.Success($"“{snap.Title}”", new JsonObject
                {
                    ["url"] = snap.Url, ["text"] = text,
                    ["elements"] = Rows(snap.Elements.Take(30), e => new JsonObject { ["i"] = e.Index, ["t"] = e.Tag, ["text"] = e.Text }),
                });
            }, B, describe: _ => Loc.T("Reading the page…"));

        yield return new FuncTool("browser_click", "Click an element in the Astra-controlled browser by element number from browser_read_page, CSS selector, or visible text.",
            Args.Schema(("target", "string", "Element number, selector or text", true)),
            async (a, c) =>
            {
                var cdp = c.Runtime.Browser.Cdp;
                if (cdp is not { IsAlive: true }) return ToolResult.Fail("No Astra browser session. Call browser_read_page first.");
                var label = await cdp.ClickAsync(a.Str("target")!, c.Ct);
                if (label.Length == 0) return ToolResult.Fail("Element not found. Call browser_read_page to refresh the element list.");
                var snap = await cdp.SnapshotAsync(c.Ct);
                return ToolResult.Success($"Clicked “{label}”. Page is now “{snap.Title}”.", new JsonObject { ["url"] = snap.Url });
            }, B, validate: (a, _) => a.Str("target") is null ? "target is required" : null,
            describe: a => Loc.F("Clicking {0}…", a.Str("target") ?? ""));

        yield return new FuncTool("browser_type", "Type text into a field in the Astra-controlled browser (default: the first search/text box). submit=true presses Enter.",
            Args.Schema(("text", "string", "Text to type", true), ("target", "string", "Element number or selector", false), ("submit", "boolean", "Press Enter afterwards", false)),
            async (a, c) =>
            {
                var cdp = c.Runtime.Browser.Cdp;
                if (cdp is not { IsAlive: true }) return ToolResult.Fail("No Astra browser session. Call browser_read_page first.");
                var ok = await cdp.TypeAsync(a.Str("target") ?? "", a.Str("text")!, a.Bool("submit"), c.Ct);
                if (!ok) return ToolResult.Fail("No text field found.");
                var snap = await cdp.SnapshotAsync(c.Ct);
                return ToolResult.Success($"Typed. Page: “{snap.Title}”.", new JsonObject { ["url"] = snap.Url });
            }, B, validate: (a, _) => a.Str("text") is null ? "text is required" : null,
            describe: _ => Loc.T("Typing in the page…"));

        yield return new FuncTool("browser_tabs", "Manage tabs of the Astra-controlled browser: list, switch, close or open a new tab.",
            Args.Schema(("action", "list|switch|close|new", "Tab action", true), ("tab_id", "string", "Tab id for switch/close", false), ("url", "string", "URL for new", false)),
            async (a, c) =>
            {
                var cdp = c.Runtime.Browser.Cdp;
                if (cdp is not { IsAlive: true }) return ToolResult.Fail("No Astra browser session. Call browser_read_page first.");
                switch (a.Str("action"))
                {
                    case "switch": await cdp.AttachAsync(a.Str("tab_id") ?? "", c.Ct); break;
                    case "close": await cdp.CloseTabAsync(a.Str("tab_id") ?? "", c.Ct); break;
                    case "new": await cdp.NewTabAsync(BrowserController.NormalizeUrl(a.Str("url") ?? "about:blank"), c.Ct); break;
                }
                var tabs = await cdp.TabsAsync(c.Ct);
                return ToolResult.Success($"{tabs.Count} tab(s).", Rows(tabs, t => new JsonObject { ["id"] = t.Id, ["title"] = t.Title, ["url"] = t.Url, ["active"] = t.Active }));
            }, B, describe: a => Loc.T("Managing tabs…"));

        yield return new FuncTool("browser_screenshot", "Take a screenshot of the Astra-controlled browser page and answer a question about it with the vision model.",
            Args.Schema(("question", "string", "What to look for", false)),
            async (a, c) =>
            {
                var cdp = c.Runtime.Browser.Cdp;
                if (cdp is not { IsAlive: true }) return ToolResult.Fail("No Astra browser session. Call browser_read_page first.");
                var png = await cdp.ScreenshotAsync(c.Ct);
                var (w, h) = PngSize(png);
                var answer = await c.Runtime.Vision.AskAsync(new Screenshot(png, w, h, 0, 0, 1), a.Str("question") ?? "Describe this page briefly.", c.Ct);
                return ToolResult.Success(answer);
            }, B, describe: _ => Loc.T("Looking at the page…"));
    }

    internal static (int W, int H) PngSize(byte[] png) =>
        png.Length > 24 ? ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]) : (0, 0);
}
