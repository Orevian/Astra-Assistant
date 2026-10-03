using Astra.Core.Index;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Astra.Core.Control;

public sealed record UiElementInfo(string Id, string Name, string Type, string? AutomationId, int X, int Y, int Width, int Height, bool Enabled);

/// <summary>
/// Windows UI Automation: finds controls by accessible name/type so Astra can click them without guessing coordinates.
/// Elements are handed to the model as short ids (ui_1, ui_2…) valid for the current task.
/// </summary>
public sealed class UiAutomationService : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Dictionary<string, AutomationElement> _cache = new();
    private int _counter;
    private readonly object _gate = new();

    private AutomationElement? RootFor(string? windowTitleOrProcess, WindowManager windows)
    {
        var w = windows.Find(windowTitleOrProcess);
        if (w is null) return null;
        try { return _automation.FromHandle(new IntPtr(w.Handle)); } catch { return null; }
    }

    private static readonly Dictionary<string, ControlType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = ControlType.Button, ["edit"] = ControlType.Edit, ["textbox"] = ControlType.Edit, ["checkbox"] = ControlType.CheckBox,
        ["radiobutton"] = ControlType.RadioButton, ["combobox"] = ControlType.ComboBox, ["link"] = ControlType.Hyperlink,
        ["menuitem"] = ControlType.MenuItem, ["tab"] = ControlType.TabItem, ["listitem"] = ControlType.ListItem,
        ["text"] = ControlType.Text, ["image"] = ControlType.Image, ["treeitem"] = ControlType.TreeItem, ["document"] = ControlType.Document,
    };

    public List<UiElementInfo> Find(string? window, string? name, string? type, int max, WindowManager windows)
    {
        var root = RootFor(window, windows);
        if (root is null) return new();
        var wanted = TextNorm.Normalize(name);
        ControlType? ct = type is not null && Types.TryGetValue(type, out var t) ? t : null;

        var hits = new List<(AutomationElement El, double Score)>();
        AutomationElement[] all;
        try { all = root.FindAllDescendants(); } catch { return new(); }
        foreach (var el in all)
        {
            try
            {
                if (el.Properties.IsOffscreen.ValueOrDefault) continue;
                if (ct is not null && el.ControlType != ct) continue;
                var n = el.Properties.Name.ValueOrDefault ?? "";
                var aid = el.Properties.AutomationId.ValueOrDefault ?? "";
                if (wanted.Length > 0)
                {
                    var score = Math.Max(TextNorm.Similarity(wanted, TextNorm.Normalize(n)), TextNorm.Similarity(wanted, TextNorm.Normalize(aid)) * 0.9);
                    if (score < 0.6) continue;
                    hits.Add((el, score));
                }
                else if (n.Length > 0 || aid.Length > 0) hits.Add((el, 0.5));
            }
            catch { /* element went away */ }
        }

        lock (_gate)
        {
            return hits.OrderByDescending(h => h.Score).Take(max).Select(h =>
            {
                var id = $"ui_{++_counter}";
                _cache[id] = h.El;
                var r = h.El.BoundingRectangle;
                return new UiElementInfo(id, h.El.Properties.Name.ValueOrDefault ?? "", h.El.ControlType.ToString(),
                    h.El.Properties.AutomationId.ValueOrDefault, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height,
                    h.El.Properties.IsEnabled.ValueOrDefault);
            }).ToList();
        }
    }

    public AutomationElement? Get(string id)
    {
        lock (_gate) return _cache.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>Invokes the element through its accessibility pattern; returns false if it needs a physical click instead.</summary>
    public bool TryInvoke(string id)
    {
        var el = Get(id);
        if (el is null) return false;
        try
        {
            if (el.Patterns.Invoke.IsSupported) { el.Patterns.Invoke.Pattern.Invoke(); return true; }
            if (el.Patterns.Toggle.IsSupported) { el.Patterns.Toggle.Pattern.Toggle(); return true; }
            if (el.Patterns.SelectionItem.IsSupported) { el.Patterns.SelectionItem.Pattern.Select(); return true; }
            if (el.Patterns.ExpandCollapse.IsSupported) { el.Patterns.ExpandCollapse.Pattern.Expand(); return true; }
        }
        catch { }
        return false;
    }

    public (int X, int Y)? Center(string id)
    {
        var el = Get(id);
        if (el is null) return null;
        try
        {
            var r = el.BoundingRectangle;
            return r.Width <= 0 || r.Height <= 0 ? null : ((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        }
        catch { return null; }
    }

    public bool TrySetText(string id, string text)
    {
        var el = Get(id);
        if (el is null) return false;
        try
        {
            if (el.Patterns.Value.IsSupported && !el.Patterns.Value.Pattern.IsReadOnly) { el.Patterns.Value.Pattern.SetValue(text); return true; }
            el.Focus();
        }
        catch { }
        return false;
    }

    public string? GetText(string id)
    {
        var el = Get(id);
        if (el is null) return null;
        try
        {
            if (el.Patterns.Value.IsSupported) return el.Patterns.Value.Pattern.Value.ValueOrDefault;
            if (el.Patterns.Text.IsSupported) return el.Patterns.Text.Pattern.DocumentRange.GetText(2000);
            return el.Properties.Name.ValueOrDefault;
        }
        catch { return null; }
    }

    public void ClearCache() { lock (_gate) { _cache.Clear(); _counter = 0; } }
    public void Dispose() => _automation.Dispose();
}
