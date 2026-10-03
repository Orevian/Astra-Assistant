using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astra.Core.Control;
using Astra.Core.Llm;
using Astra.Core.Localization;

namespace Astra.Core.Assistant;

public sealed record ScreenLocation(bool Found, int X, int Y, int Width, int Height, string Label);

/// <summary>Screen understanding: screenshot → vision model → answer or element coordinates (absolute screen pixels).</summary>
public sealed class VisionService
{
    private readonly AstraRuntime _rt;
    public VisionService(AstraRuntime rt) => _rt = rt;

    private static readonly Dictionary<string, string> DefaultModels = new()
    {
        ["openai"] = "gpt-5-mini", ["anthropic"] = "claude-sonnet-5-5", ["gemini"] = "gemini-2.5-flash",
        ["mistral"] = "pixtral-large-latest",
    };

    public async Task<(IChatClient? Client, string? Error)> ResolveClientAsync(CancellationToken ct)
    {
        var ai = _rt.Settings.Current.Ai;
        if (ai.VisionProviderId == "same")
        {
            var (chat, err) = _rt.CreateChatClient();
            if (chat is null) return (null, err);
            return chat.SupportsVision ? (chat, null)
                : (null, Loc.F("The current model “{0}” cannot see images. Choose a vision-capable model or a separate vision provider under Settings ▸ AI model.", chat.Model));
        }

        var id = ai.VisionProviderId;
        string? model = string.IsNullOrWhiteSpace(ai.VisionModel) ? null : ai.VisionModel;
        if (id == "ollama" && model is null)
        {
            var test = await Providers.ProviderCatalog.Get("ollama").TestAsync(null, null, ct);
            model = test.Models.FirstOrDefault(m => Regex.IsMatch(m, "llava|vision|gemma3|minicpm-v|qwen2.5vl|qwen3-vl", RegexOptions.IgnoreCase));
            if (model is null) return (null, Loc.T("No vision-capable Ollama model is installed (for example gemma3 or llava)."));
        }
        model ??= DefaultModels.GetValueOrDefault(id);
        if (model is null) return (null, Loc.T("Choose a vision model under Settings ▸ AI model."));
        var (client, error) = ChatClientFactory.Create(id, model, id == ai.ProviderId ? ai.CustomEndpoint : null, Security.CredentialStore.Load);
        return (client, error);
    }

    public async Task<string> AskAsync(Screenshot shot, string question, CancellationToken ct)
    {
        var (client, err) = await ResolveClientAsync(ct);
        if (client is null) throw new InvalidOperationException(err);
        var res = await client.CompleteAsync(
            "You analyze screenshots. Answer concisely and factually about what is visible. Do not invent elements.",
            new[] { ChatMessage.User($"{question}\n(The image is {shot.Width}x{shot.Height} pixels.)", new ChatImage(shot.Png)) }, null, ct, 500);
        if (!res.Ok) throw new InvalidOperationException(res.Error);
        return res.Text ?? "";
    }

    public async Task<ScreenLocation> LocateAsync(Screenshot shot, string description, CancellationToken ct)
    {
        var (client, err) = await ResolveClientAsync(ct);
        if (client is null) throw new InvalidOperationException(err);
        var prompt = $$"""
            Find this UI element in the screenshot: "{{description}}".
            The image is {{shot.Width}} pixels wide and {{shot.Height}} pixels tall; the origin (0,0) is the top-left corner.
            Reply with ONLY JSON, no prose: {"found": true|false, "x": <center x in image pixels>, "y": <center y in image pixels>, "width": <w>, "height": <h>, "label": "<what you found>"}
            If several match, pick the most prominent one. If nothing matches, reply {"found": false}.
            """;
        var res = await client.CompleteAsync("You locate UI elements in screenshots and answer with strict JSON.",
            new[] { ChatMessage.User(prompt, new ChatImage(shot.Png)) }, null, ct, 300);
        if (!res.Ok) throw new InvalidOperationException(res.Error);

        var m = Regex.Match(res.Text ?? "", @"\{.*\}", RegexOptions.Singleline);
        if (!m.Success) return new ScreenLocation(false, 0, 0, 0, 0, "");
        JsonNode? n;
        try { n = JsonNode.Parse(m.Value); } catch { return new ScreenLocation(false, 0, 0, 0, 0, ""); }
        if (n?["found"]?.GetValue<bool>() != true) return new ScreenLocation(false, 0, 0, 0, 0, "");

        double x = n["x"]?.GetValue<double>() ?? 0, y = n["y"]?.GetValue<double>() ?? 0;
        double w = n["width"]?.GetValue<double>() ?? 0, h = n["height"]?.GetValue<double>() ?? 0;
        // Some models answer in a 0-1000 grid instead of pixels; detect that and rescale.
        if (x <= 1000 && y <= 1000 && (x > shot.Width || y > shot.Height)) { x = x / 1000 * shot.Width; y = y / 1000 * shot.Height; w = w / 1000 * shot.Width; h = h / 1000 * shot.Height; }
        var sx = shot.OriginX + (int)(x / shot.Scale);
        var sy = shot.OriginY + (int)(y / shot.Scale);
        return new ScreenLocation(true, sx, sy, (int)(w / shot.Scale), (int)(h / shot.Scale), (string?)n["label"] ?? description);
    }
}
