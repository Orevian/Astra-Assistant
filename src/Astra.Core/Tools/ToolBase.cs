using System.Text.Json.Nodes;
using Astra.Core.Assistant;

namespace Astra.Core.Tools;

public enum PermissionCategory { None, Applications, Browser, Files, Input, Terminal, System, Power }

public static class PermissionKeys
{
    public static string? Key(PermissionCategory c) => c switch
    {
        PermissionCategory.Applications => "applications",
        PermissionCategory.Browser => "browser",
        PermissionCategory.Files => "files",
        PermissionCategory.Input => "input",
        PermissionCategory.Terminal => "terminal",
        PermissionCategory.System => "system",
        PermissionCategory.Power => "power",
        _ => null,
    };

    public static string Title(PermissionCategory c) => c switch
    {
        PermissionCategory.Applications => "Applications",
        PermissionCategory.Browser => "Browser",
        PermissionCategory.Files => "Files",
        PermissionCategory.Input => "Mouse & keyboard",
        PermissionCategory.Terminal => "Terminal",
        PermissionCategory.System => "System settings",
        PermissionCategory.Power => "Shutdown / restart",
        _ => "",
    };
}

public sealed record ToolResult(bool Ok, string Message, JsonNode? Data = null, bool Verified = true)
{
    public static ToolResult Success(string message, JsonNode? data = null, bool verified = true) => new(true, message, data, verified);
    public static ToolResult Fail(string message, JsonNode? data = null) => new(false, message, data, false);

    /// <summary>Compact form handed back to the model: short, no wasted tokens.</summary>
    public string ForModel(int maxChars = 1800)
    {
        var o = new JsonObject { ["ok"] = Ok, ["message"] = Message };
        if (!Verified && Ok) o["verified"] = false;
        if (Data is not null) o["data"] = Data.DeepClone();
        var s = o.ToJsonString();
        return s.Length <= maxChars ? s : s[..maxChars] + "…(truncated)";
    }
}

/// <summary>Per-request state: which permission categories the user already approved for this task.</summary>
public sealed class TaskState
{
    private readonly HashSet<PermissionCategory> _granted = new();
    public bool Declined { get; set; }
    public string Language { get; set; } = "en";
    public bool IsGranted(PermissionCategory c) => _granted.Contains(c);
    public void Grant(PermissionCategory c) => _granted.Add(c);
}

public sealed class ToolContext
{
    public required AstraRuntime Runtime { get; init; }
    public required TaskState Task { get; init; }
    public CancellationToken Ct { get; init; }
}

public abstract class Tool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonObject Schema { get; }
    public virtual PermissionCategory Category => PermissionCategory.None;
    /// <summary>Irreversible or high-impact: always asks, even when the category is set to "Always allow".</summary>
    public virtual bool Destructive => false;

    /// <summary>Returns an error string when the arguments are unusable. Runs before anything touches the system.</summary>
    public virtual string? Validate(JsonObject args, ToolContext ctx) => null;
    /// <summary>Extra reason to confirm for this particular call (e.g. force-killing). Null means no extra confirmation.</summary>
    public virtual string? ConfirmationText(JsonObject args, ToolContext ctx) => null;
    /// <summary>Short HUD line such as "Opening VALORANT…".</summary>
    public virtual string Describe(JsonObject args) => Name;

    public abstract Task<ToolResult> ExecuteAsync(JsonObject args, ToolContext ctx);
    public virtual Task<ToolResult> VerifyAsync(JsonObject args, ToolResult result, ToolContext ctx) => Task.FromResult(result);
}

/// <summary>Tiny helpers to build JSON schemas and read arguments tolerantly (models are sloppy with types).</summary>
public static class Args
{
    public static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var p = new JsonObject();
        var req = new JsonArray();
        foreach (var (name, type, desc, required) in props)
        {
            if (type.EndsWith("[]")) p[name] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = type[..^2] }, ["description"] = desc };
            else if (type.Contains('|'))
            {
                var vals = new JsonArray();
                foreach (var v in type.Split('|')) vals.Add(v);
                p[name] = new JsonObject { ["type"] = "string", ["enum"] = vals, ["description"] = desc };
            }
            else p[name] = new JsonObject { ["type"] = type, ["description"] = desc };
            if (required) req.Add(name);
        }
        return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = req };
    }

    public static string? Str(this JsonObject a, string key)
    {
        if (a[key] is not JsonNode n) return null;
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return string.IsNullOrWhiteSpace(s) ? null : s;
        return n.ToString();
    }

    public static int? Int(this JsonObject a, string key)
    {
        if (a[key] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d);
        return v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var ds) ? (int)Math.Round(ds) : null;
    }

    public static bool Bool(this JsonObject a, string key, bool dflt = false)
    {
        if (a[key] is not JsonValue v) return dflt;
        if (v.TryGetValue<bool>(out var b)) return b;
        return v.TryGetValue<string>(out var s) && bool.TryParse(s, out var bs) ? bs : dflt;
    }

    public static List<string> StrList(this JsonObject a, string key)
    {
        if (a[key] is JsonArray arr) return arr.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        if (a[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            return s.Split(new[] { '\n', ';', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        return new();
    }
}
