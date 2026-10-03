using System.Text.Json.Nodes;
using Astra.Core.Control;
using Astra.Core.Localization;
using Astra.Core.Native;

namespace Astra.Core.Tools;

public static partial class BuiltInTools
{
    private static async Task<(int X, int Y)?> ResolvePoint(JsonObject a, ToolContext c)
    {
        if (a.Str("element_id") is { } id)
        {
            var center = c.Runtime.Ui.Center(id);
            if (center is not null) return center;
        }
        if (a.Int("x") is { } x && a.Int("y") is { } y) return (x, y);
        await Task.CompletedTask;
        return null;
    }

    private static IEnumerable<Tool> InputTools()
    {
        var pointSchema = Args.Schema(("x", "integer", "Screen x in pixels", false), ("y", "integer", "Screen y in pixels", false),
            ("element_id", "string", "Element id from find_ui_element (preferred over coordinates)", false));

        yield return new FuncTool("find_ui_element", "Find buttons/fields/links in a window by accessible name via Windows UI Automation. Returns element ids for click/type_text.",
            Args.Schema(("name", "string", "Visible/accessible name", false), ("window", "string", "Window title or process; default foreground window", false),
                ("type", "button|edit|checkbox|combobox|link|menuitem|tab|listitem|text", "Control type", false)),
            (a, c) =>
            {
                var list = c.Runtime.Ui.Find(a.Str("window"), a.Str("name"), a.Str("type"), 12, c.Runtime.Windows);
                return Task.FromResult(list.Count == 0 ? ToolResult.Fail("No matching UI element. Try analyze_screen or click_element_by_description.")
                    : ToolResult.Success($"{list.Count} element(s).", Rows(list, e => new JsonObject { ["id"] = e.Id, ["name"] = e.Name, ["type"] = e.Type, ["enabled"] = e.Enabled })));
            }, I, describe: a => Loc.F("Looking for “{0}”…", a.Str("name") ?? ""));

        yield return new FuncTool("click", "Click a UI element by element_id or screen coordinates.",
            Args.Schema(("x", "integer", "Screen x", false), ("y", "integer", "Screen y", false), ("element_id", "string", "Id from find_ui_element", false),
                ("button", "left|right|middle", "Mouse button", false)),
            async (a, c) =>
            {
                var button = a.Str("button") ?? "left";
                if (a.Str("element_id") is { } id && button == "left" && c.Runtime.Settings.Current.Automation.PreferUiAutomation && c.Runtime.Ui.TryInvoke(id))
                    return ToolResult.Success("Activated the element through UI Automation.");
                if (!c.Runtime.Settings.Current.Automation.AllowMouse) return ToolResult.Fail("Mouse control is disabled in Settings ▸ Automation.");
                var p = await ResolvePoint(a, c);
                if (p is null) return ToolResult.Fail("Provide element_id or x and y.");
                await c.Runtime.Input.ClickAsync(p.Value.X, p.Value.Y, button, 1, c.Ct);
                await Task.Delay(c.Runtime.Settings.Current.Automation.ActionDelayMs, c.Ct);
                return ToolResult.Success($"Clicked at ({p.Value.X}, {p.Value.Y}).");
            }, I, describe: _ => Loc.T("Clicking…"));

        yield return new FuncTool("double_click", "Double-click at coordinates or on an element.", pointSchema,
            async (a, c) =>
            {
                if (!c.Runtime.Settings.Current.Automation.AllowMouse) return ToolResult.Fail("Mouse control is disabled in Settings ▸ Automation.");
                var p = await ResolvePoint(a, c);
                if (p is null) return ToolResult.Fail("Provide element_id or x and y.");
                await c.Runtime.Input.ClickAsync(p.Value.X, p.Value.Y, "left", 2, c.Ct);
                return ToolResult.Success($"Double-clicked at ({p.Value.X}, {p.Value.Y}).");
            }, I, describe: _ => Loc.T("Double-clicking…"));

        yield return new FuncTool("move_mouse", "Move the mouse pointer to screen coordinates.",
            Args.Schema(("x", "integer", "Screen x", true), ("y", "integer", "Screen y", true)),
            async (a, c) =>
            {
                await c.Runtime.Input.MoveMouseAsync(a.Int("x")!.Value, a.Int("y")!.Value, ct: c.Ct);
                return ToolResult.Success("Moved the pointer.");
            }, I, validate: (a, _) => a.Int("x") is null || a.Int("y") is null ? "x and y are required" : null,
            describe: _ => Loc.T("Moving the mouse…"));

        yield return new FuncTool("scroll", "Scroll the mouse wheel: positive scrolls up, negative scrolls down.",
            Args.Schema(("amount", "integer", "Wheel notches, e.g. -3", true)),
            (a, c) => { c.Runtime.Input.Scroll(a.Int("amount") ?? -3); return Task.FromResult(ToolResult.Success("Scrolled.")); },
            I, describe: _ => Loc.T("Scrolling…"));

        yield return new FuncTool("type_text", "Type text into the focused field, or into element_id. submit=true presses Enter after.",
            Args.Schema(("text", "string", "Text to type", true), ("element_id", "string", "Optional field id from find_ui_element", false), ("submit", "boolean", "Press Enter afterwards", false)),
            async (a, c) =>
            {
                if (!c.Runtime.Settings.Current.Automation.AllowKeyboard) return ToolResult.Fail("Keyboard control is disabled in Settings ▸ Automation.");
                var text = a.Str("text")!;
                var done = false;
                if (a.Str("element_id") is { } id)
                {
                    done = c.Runtime.Settings.Current.Automation.PreferUiAutomation && c.Runtime.Ui.TrySetText(id, text);
                    if (!done)
                    {
                        if (c.Runtime.Ui.Center(id) is { } p) { await c.Runtime.Input.ClickAsync(p.X, p.Y, "left", 1, c.Ct); await Task.Delay(120, c.Ct); }
                    }
                }
                if (!done) await c.Runtime.Input.TypeTextAsync(text, 6, c.Ct);
                if (a.Bool("submit")) { await Task.Delay(120, c.Ct); c.Runtime.Input.PressChord("enter"); }
                return ToolResult.Success($"Typed {text.Length} character(s).");
            }, I, validate: (a, _) => a.Str("text") is null ? "text is required" : null,
            describe: _ => Loc.T("Typing…"));

        yield return new FuncTool("press_key", "Press a key or combination: enter, tab, esc, ctrl+l, ctrl+shift+t, win+d, alt+f4, f5, volume_up…",
            Args.Schema(("keys", "string", "Key or combo joined with +", true)),
            (a, c) =>
            {
                if (!c.Runtime.Settings.Current.Automation.AllowKeyboard) return Task.FromResult(ToolResult.Fail("Keyboard control is disabled in Settings ▸ Automation."));
                return Task.FromResult(c.Runtime.Input.PressChord(a.Str("keys")!) ? ToolResult.Success($"Pressed {a.Str("keys")}.") : ToolResult.Fail("Unknown key."));
            }, I,
            validate: (a, ctx) => a.Str("keys") is null ? "keys is required" : InputController.TryParseChord(a.Str("keys")!, out var parsed, out var err) ? null : err,
            describe: a => Loc.F("Pressing {0}…", a.Str("keys") ?? ""));

        yield return new FuncTool("screenshot", "Save a screenshot of a display to a PNG file and return its path.",
            Args.Schema(("display", "string", "Display device name; default the primary display", false)),
            (a, c) =>
            {
                var shot = ScreenCapture.Capture(a.Str("display") ?? c.Runtime.Settings.Current.Appearance.HudDisplay, 3840);
                var dir = Path.Combine(Settings.SettingsStore.DataDirectory, "screenshots");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"screen-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                File.WriteAllBytes(path, shot.Png);
                return Task.FromResult(ToolResult.Success($"Saved {shot.Width}x{shot.Height} screenshot.", new JsonObject { ["path"] = path }));
            }, I, describe: _ => Loc.T("Taking a screenshot…"));

        yield return new FuncTool("analyze_screen", "Look at the screen with the vision model and answer a question about what is visible.",
            Args.Schema(("question", "string", "What to look for", true)),
            async (a, c) =>
            {
                var shot = ScreenCapture.Capture(DisplayOfForeground(c), 1600);
                var answer = await c.Runtime.Vision.AskAsync(shot, a.Str("question")!, c.Ct);
                return ToolResult.Success(answer);
            }, I, validate: (a, _) => a.Str("question") is null ? "question is required" : null,
            describe: _ => Loc.T("Looking at the screen…"));

        yield return new FuncTool("click_element_by_description", "Find a visible element by description (e.g. 'the red button') with the vision model, click it, and verify the screen changed.",
            Args.Schema(("description", "string", "What to click, e.g. 'red Subscribe button'", true), ("button", "left|right", "Mouse button", false)),
            async (a, c) =>
            {
                if (!c.Runtime.Settings.Current.Automation.AllowMouse) return ToolResult.Fail("Mouse control is disabled in Settings ▸ Automation.");
                var display = DisplayOfForeground(c);
                var before = ScreenCapture.Capture(display, 1600);
                var loc = await c.Runtime.Vision.LocateAsync(before, a.Str("description")!, c.Ct);
                if (!loc.Found) return ToolResult.Fail("The vision model could not find that element on screen.");
                await c.Runtime.Input.ClickAsync(loc.X, loc.Y, a.Str("button") ?? "left", 1, c.Ct);
                await Task.Delay(Math.Max(700, c.Runtime.Settings.Current.Automation.ActionDelayMs), c.Ct);
                var after = ScreenCapture.Capture(display, 1600);
                var changed = ScreenCapture.Difference(before, after) > 0.002;
                return ToolResult.Success($"Clicked “{loc.Label}” at ({loc.X}, {loc.Y}).{(changed ? " The screen changed." : " The screen did not visibly change.")}",
                    new JsonObject { ["x"] = loc.X, ["y"] = loc.Y }, changed);
            }, I, validate: (a, _) => a.Str("description") is null ? "description is required" : null,
            describe: a => Loc.F("Looking for {0}…", a.Str("description") ?? ""));

        yield return new FuncTool("clipboard_get", "Read text from the clipboard.", Args.Schema(),
            (_, _) =>
            {
                var t = ClipboardService.GetText();
                return Task.FromResult(string.IsNullOrEmpty(t) ? ToolResult.Fail("The clipboard has no text.")
                    : ToolResult.Success(t.Length > 1500 ? t[..1500] + "…" : t));
            }, I, describe: _ => Loc.T("Reading the clipboard…"));

        yield return new FuncTool("clipboard_set", "Put text on the clipboard.",
            Args.Schema(("text", "string", "Text to copy", true)),
            (a, _) => Task.FromResult(ClipboardService.SetText(a.Str("text")!) ? ToolResult.Success("Copied to the clipboard.") : ToolResult.Fail("Clipboard is busy.")),
            I, validate: (a, _) => a.Str("text") is null ? "text is required" : null, describe: _ => Loc.T("Copying to the clipboard…"));
    }

    private static string? DisplayOfForeground(ToolContext c)
    {
        var w = c.Runtime.Windows.Foreground();
        return w is null ? null : Displays.FromPoint(w.X + w.Width / 2, w.Y + w.Height / 2).DeviceName;
    }
}
