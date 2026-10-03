using System.Net.Http.Headers;
using System.Text.Json;

using Astra.Core.Localization;

namespace Astra.Core.Providers;

public sealed record ProviderTestResult(bool Success, string Message, IReadOnlyList<string> Models);

public interface IAiProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool RequiresApiKey { get; }
    bool SupportsCustomEndpoint { get; }
    string DefaultEndpoint { get; }
    IReadOnlyList<string> SuggestedModels { get; }

    /// <summary>Lists models available to the account; doubles as the connection test.</summary>
    Task<ProviderTestResult> TestAsync(string? apiKey, string? endpoint, CancellationToken ct = default);
}

public abstract class HttpProviderBase : IAiProvider
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual bool RequiresApiKey => true;
    public virtual bool SupportsCustomEndpoint => false;
    public abstract string DefaultEndpoint { get; }
    public abstract IReadOnlyList<string> SuggestedModels { get; }

    protected abstract HttpRequestMessage BuildModelsRequest(string? apiKey, string endpoint);
    protected abstract IEnumerable<string> ParseModels(JsonElement root);

    public async Task<ProviderTestResult> TestAsync(string? apiKey, string? endpoint, CancellationToken ct = default)
    {
        if (RequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            return new(false, Loc.T("API key is missing."), Array.Empty<string>());
        var baseUrl = (string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint).TrimEnd('/');
        try
        {
            using var req = BuildModelsRequest(apiKey, baseUrl);
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var msg = res.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Loc.T("Authentication failed. Check your API key."),
                    System.Net.HttpStatusCode.NotFound => Loc.T("Endpoint not found. Check the base URL."),
                    System.Net.HttpStatusCode.TooManyRequests => Loc.T("Rate limited by the provider."),
                    _ => Loc.F("Server returned {0} {1}.", (int)res.StatusCode, res.ReasonPhrase ?? ""),
                };
                return new(false, msg, Array.Empty<string>());
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var models = ParseModels(doc.RootElement).Distinct().OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            return new(true, models.Count > 0 ? Loc.F("Connected. {0} models available.", models.Count) : Loc.T("Connected."), models);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, Loc.T("Connection timed out."), Array.Empty<string>());
        }
        catch (HttpRequestException ex)
        {
            return new(false, Loc.F("Could not reach the server: {0}", ex.Message), Array.Empty<string>());
        }
        catch (JsonException)
        {
            return new(false, Loc.T("Unexpected response from the server."), Array.Empty<string>());
        }
    }

    protected static IEnumerable<string> ArrayProp(JsonElement root, string array, string prop)
    {
        if (root.TryGetProperty(array, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.TryGetProperty(prop, out var v) && v.GetString() is { } s)
                    yield return s;
    }
}

public class OpenAiCompatibleProvider : HttpProviderBase
{
    private readonly string _id, _name, _endpoint;
    private readonly bool _custom, _keyRequired;
    private readonly string[] _models;

    public OpenAiCompatibleProvider(string id, string name, string endpoint, string[] models, bool custom = false, bool keyRequired = true)
        => (_id, _name, _endpoint, _models, _custom, _keyRequired) = (id, name, endpoint, models, custom, keyRequired);

    public override string Id => _id;
    public override string DisplayName => _name;
    public override bool RequiresApiKey => _keyRequired;
    public override bool SupportsCustomEndpoint => _custom;
    public override string DefaultEndpoint => _endpoint;
    public override IReadOnlyList<string> SuggestedModels => _models;

    protected override HttpRequestMessage BuildModelsRequest(string? apiKey, string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/models");
        if (!string.IsNullOrEmpty(apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    protected override IEnumerable<string> ParseModels(JsonElement root) => ArrayProp(root, "data", "id");
}

public sealed class AnthropicProvider : HttpProviderBase
{
    public override string Id => "anthropic";
    public override string DisplayName => "Anthropic";
    public override string DefaultEndpoint => "https://api.anthropic.com/v1";
    public override IReadOnlyList<string> SuggestedModels { get; } = new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5-20251001" };

    protected override HttpRequestMessage BuildModelsRequest(string? apiKey, string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/models?limit=100");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    protected override IEnumerable<string> ParseModels(JsonElement root) => ArrayProp(root, "data", "id");
}

public sealed class GeminiProvider : HttpProviderBase
{
    public override string Id => "gemini";
    public override string DisplayName => "Google Gemini";
    public override string DefaultEndpoint => "https://generativelanguage.googleapis.com/v1beta";
    public override IReadOnlyList<string> SuggestedModels { get; } = new[] { "gemini-2.5-pro", "gemini-2.5-flash" };

    protected override HttpRequestMessage BuildModelsRequest(string? apiKey, string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/models?pageSize=200");
        req.Headers.Add("x-goog-api-key", apiKey);
        return req;
    }

    protected override IEnumerable<string> ParseModels(JsonElement root) =>
        ArrayProp(root, "models", "name").Select(n => n.StartsWith("models/") ? n[7..] : n);
}

public sealed class OllamaProvider : HttpProviderBase
{
    public override string Id => "ollama";
    public override string DisplayName => "Ollama (local)";
    public override bool RequiresApiKey => false;
    public override bool SupportsCustomEndpoint => true;
    public override string DefaultEndpoint => "http://localhost:11434";
    public override IReadOnlyList<string> SuggestedModels { get; } = Array.Empty<string>();

    protected override HttpRequestMessage BuildModelsRequest(string? apiKey, string endpoint) =>
        new(HttpMethod.Get, endpoint + "/api/tags");

    protected override IEnumerable<string> ParseModels(JsonElement root) => ArrayProp(root, "models", "name");
}

public static class ProviderCatalog
{
    public static IReadOnlyList<IAiProvider> All { get; } = new IAiProvider[]
    {
        new OpenAiCompatibleProvider("openai", "OpenAI", "https://api.openai.com/v1", new[] { "gpt-5", "gpt-5-mini", "gpt-4.1" }),
        new GeminiProvider(),
        new AnthropicProvider(),
        new OpenAiCompatibleProvider("mistral", "Mistral", "https://api.mistral.ai/v1", new[] { "mistral-large-latest", "mistral-small-latest" }),
        new OpenAiCompatibleProvider("deepseek", "DeepSeek", "https://api.deepseek.com/v1", new[] { "deepseek-chat", "deepseek-reasoner" }),
        new OllamaProvider(),
        new OpenAiCompatibleProvider("custom", "OpenAI-compatible API", "http://localhost:1234/v1", Array.Empty<string>(), custom: true, keyRequired: false),
    };

    public static IAiProvider Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
