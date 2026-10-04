using System.Text.Json.Nodes;

namespace Astra.Core.Llm;

public sealed record ChatImage(byte[] Data, string MediaType = "image/png");

public sealed record ToolCall(string Id, string Name, JsonObject Args);

public sealed record ToolSpec(string Name, string Description, JsonObject Schema);

/// <param name="Role">user | assistant | tool</param>
/// <param name="Raw">Provider-specific payload that must be replayed verbatim (e.g. Gemini thought signatures).</param>
public sealed record ChatMessage(string Role, string? Text = null, List<ToolCall>? ToolCalls = null,
    string? ToolCallId = null, string? ToolName = null, List<ChatImage>? Images = null, JsonNode? Raw = null)
{
    public static ChatMessage User(string text, params ChatImage[] images) => new("user", text, Images: images.Length > 0 ? images.ToList() : null);
    public static ChatMessage Tool(ToolCall call, string result) => new("tool", result, ToolCallId: call.Id, ToolName: call.Name);
}

public sealed record ChatResponse(string? Text, List<ToolCall> ToolCalls, string? Error = null, int InputTokens = 0, int OutputTokens = 0, JsonNode? Raw = null)
{
    public bool Ok => Error is null;
    public static ChatResponse Fail(string error) => new(null, new(), error);
}

public interface IChatClient
{
    string ProviderId { get; }
    string Model { get; }
    bool SupportsVision { get; }
    Task<ChatResponse> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools,
        CancellationToken ct, int maxTokens = 1024);
}
