using System.Text.Json.Nodes;
using Astra.Core.Assistant;
using Astra.Core.Localization;
using Astra.Core.Settings;

namespace Astra.Core.Tools;

public sealed record ConfirmRequest(string Title, string Message, bool Destructive);

/// <summary>Implemented by the UI: shows an Allow / Cancel prompt and returns the user's decision.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct);
}

public sealed record ToolEvent(string Tool, string Description, bool? Ok, string? Message);

/// <summary>Runs tools as Validate → permission check → Execute → Verify, and reports each step.</summary>
public sealed class ToolExecutor
{
    private readonly AstraRuntime _rt;
    private readonly Dictionary<string, Tool> _tools;

    public IConfirmationService? Confirmation { get; set; }
    public event Action<ToolEvent>? Event;

    public ToolExecutor(AstraRuntime rt, IEnumerable<Tool> tools)
    {
        _rt = rt;
        _tools = tools.ToDictionary(t => t.Name);
    }

    public IReadOnlyCollection<Tool> All => _tools.Values;
    public Tool? Get(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<Llm.ToolSpec> Specs() => _tools.Values.Select(t => new Llm.ToolSpec(t.Name, t.Description, t.Schema)).ToList();

    public async Task<ToolResult> ExecuteAsync(string name, JsonObject args, TaskState task, CancellationToken ct)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return ToolResult.Fail($"Unknown tool '{name}'.");

        var ctx = new ToolContext { Runtime = _rt, Task = task, Ct = ct };
        var description = tool.Describe(args);
        Event?.Invoke(new ToolEvent(name, description, null, null));

        // 1. Validate
        var invalid = tool.Validate(args, ctx);
        if (invalid is not null) return Done(name, description, ToolResult.Fail(invalid));

        // 2. Permissions
        var gate = await CheckPermissionAsync(tool, args, ctx);
        if (gate is not null) return Done(name, description, gate);

        // 3. Execute, 4. Verify
        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(args, ctx);
            if (result.Ok) result = await tool.VerifyAsync(args, result, ctx);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result = ToolResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        return Done(name, description, result);
    }

    private ToolResult Done(string name, string description, ToolResult r)
    {
        Event?.Invoke(new ToolEvent(name, description, r.Ok, r.Message));
        return r;
    }

    private async Task<ToolResult?> CheckPermissionAsync(Tool tool, JsonObject args, ToolContext ctx)
    {
        var extra = tool.ConfirmationText(args, ctx);
        if (tool.Category == PermissionCategory.None && extra is null) return null;

        var key = PermissionKeys.Key(tool.Category);
        var level = key is not null && _rt.Settings.Current.Security.Permissions.TryGetValue(key, out var l) ? l : PermissionLevel.Ask;
        var title = Loc.T(PermissionKeys.Title(tool.Category));
        if (level == PermissionLevel.Deny)
            return ToolResult.Fail($"The user has disabled the “{PermissionKeys.Title(tool.Category)}” permission. Do not retry; tell the user how to enable it in Settings ▸ Security.");

        var alwaysConfirm = tool.Destructive && _rt.Settings.Current.Security.AlwaysConfirmDestructive || extra is not null;
        var needsAsk = alwaysConfirm || (level == PermissionLevel.Ask && !ctx.Task.IsGranted(tool.Category));
        if (!needsAsk) return null;

        if (Confirmation is null) return ToolResult.Fail("This action needs the user's approval, but no confirmation prompt is available.");
        var message = extra ?? tool.Describe(args);
        var allowed = await Confirmation.ConfirmAsync(new ConfirmRequest(title, message, tool.Destructive || extra is not null), ctx.Ct);
        if (!allowed)
        {
            ctx.Task.Declined = true;
            return ToolResult.Fail("The user declined this action. Do not retry it; acknowledge and stop or offer an alternative.");
        }
        if (!alwaysConfirm) ctx.Task.Grant(tool.Category);
        return null;
    }
}
