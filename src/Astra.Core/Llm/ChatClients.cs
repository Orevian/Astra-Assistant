using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astra.Core.Localization;

namespace Astra.Core.Llm;

internal static class Wire
{
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    public static async Task<(JsonNode? Body, string? Error)> PostAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            using var res = await Http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var detail = text.Length > 400 ? text[..400] : text;
                try
                {
                    var n = JsonNode.Parse(text);
                    detail = (string?)n?["error"]?["message"] ?? (string?)n?["error"] ?? (string?)n?["message"] ?? detail;
                }
                catch { }
                return (null, res.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Loc.F("The provider rejected the API key ({0}).", detail),
                    System.Net.HttpStatusCode.TooManyRequests => Loc.F("The provider is rate limiting requests: {0}", detail),
                    _ => Loc.F("The provider returned {0}: {1}", (int)res.StatusCode, detail),
                });
            }
            return (JsonNode.Parse(text), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return (null, Loc.T("The model took too long to respond.")); }
        catch (HttpRequestException ex) { return (null, Loc.F("Could not reach the model: {0}", ex.Message)); }
    }

    public static JsonObject ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); } catch { return new JsonObject(); }
    }

    public static string StripThinking(string? text) =>
        string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();

    public static string ToDataUrl(ChatImage i) => $"data:{i.MediaType};base64,{Convert.ToBase64String(i.Data)}";
}

/// <summary>OpenAI Chat Completions wire format: OpenAI, Mistral, DeepSeek and any OpenAI-compatible server.</summary>
public sealed class OpenAiChatClient : IChatClient
{
    private readonly string _endpoint;
    private readonly string? _key;
    public string ProviderId { get; }
    public string Model { get; }
    public bool SupportsVision => ProviderId != "deepseek";

    public OpenAiChatClient(string providerId, string endpoint, string? key, string model)
        => (ProviderId, _endpoint, _key, Model) = (providerId, endpoint.TrimEnd('/'), key, model);

    public async Task<ChatResponse> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools,
        CancellationToken ct, int maxTokens = 1024)
    {
        var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "user":
                    if (m.Images is { Count: > 0 })
                    {
                        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = m.Text ?? "" } };
                        foreach (var img in m.Images)
                            parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = Wire.ToDataUrl(img) } });
                        msgs.Add(new JsonObject { ["role"] = "user", ["content"] = parts });
                    }
                    else msgs.Add(new JsonObject { ["role"] = "user", ["content"] = m.Text ?? "" });
                    break;
                case "assistant":
                    var a = new JsonObject { ["role"] = "assistant", ["content"] = m.Text };
                    if (m.ToolCalls is { Count: > 0 })
                    {
                        var calls = new JsonArray();
                        foreach (var c in m.ToolCalls)
                            calls.Add(new JsonObject
                            {
                                ["id"] = c.Id, ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Args.ToJsonString() },
                            });
                        a["tool_calls"] = calls;
                    }
                    msgs.Add(a);
                    break;
                case "tool":
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId, ["content"] = m.Text ?? "" });
                    break;
            }
        }

        var body = new JsonObject { ["model"] = Model, ["messages"] = msgs };
        body[ProviderId == "openai" ? "max_completion_tokens" : "max_tokens"] = maxTokens;
        if (tools is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var t in tools)
                arr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Schema.DeepClone() },
                });
            body["tools"] = arr;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/chat/completions") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrEmpty(_key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        var (res, err) = await Wire.PostAsync(req, ct);
        if (err is not null) return ChatResponse.Fail(err);

        var msg = res?["choices"]?[0]?["message"];
        var calls2 = new List<ToolCall>();
        if (msg?["tool_calls"] is JsonArray tc)
            foreach (var c in tc)
                calls2.Add(new ToolCall((string?)c?["id"] ?? Guid.NewGuid().ToString("N")[..8], (string)c!["function"]!["name"]!, Wire.ParseArgs((string?)c["function"]!["arguments"])));
        return new ChatResponse(Wire.StripThinking((string?)msg?["content"]), calls2, null,
            (int?)res?["usage"]?["prompt_tokens"] ?? 0, (int?)res?["usage"]?["completion_tokens"] ?? 0);
    }
}

/// <summary>Anthropic Messages API.</summary>
public sealed class AnthropicChatClient : IChatClient
{
    private readonly string _endpoint, _key;
    public string ProviderId => "anthropic";
    public string Model { get; }
    public bool SupportsVision => true;

    public AnthropicChatClient(string endpoint, string key, string model) => (_endpoint, _key, Model) = (endpoint.TrimEnd('/'), key, model);

    public async Task<ChatResponse> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools,
        CancellationToken ct, int maxTokens = 1024)
    {
        var msgs = new JsonArray();
        JsonArray? pendingResults = null;
        void FlushResults() { if (pendingResults is not null) { msgs.Add(new JsonObject { ["role"] = "user", ["content"] = pendingResults }); pendingResults = null; } }

        foreach (var m in messages)
        {
            if (m.Role == "tool")
            {
                pendingResults ??= new JsonArray();
                pendingResults.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = m.ToolCallId, ["content"] = m.Text ?? "" });
                continue;
            }
            FlushResults();
            if (m.Role == "user")
            {
                var blocks = new JsonArray();
                if (m.Images is not null)
                    foreach (var img in m.Images)
                        blocks.Add(new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = img.MediaType, ["data"] = Convert.ToBase64String(img.Data) },
                        });
                blocks.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text ?? "" });
                msgs.Add(new JsonObject { ["role"] = "user", ["content"] = blocks });
            }
            else
            {
                var blocks = new JsonArray();
                if (!string.IsNullOrEmpty(m.Text)) blocks.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text });
                if (m.ToolCalls is not null)
                    foreach (var c in m.ToolCalls)
                        blocks.Add(new JsonObject { ["type"] = "tool_use", ["id"] = c.Id, ["name"] = c.Name, ["input"] = c.Args.DeepClone() });
                msgs.Add(new JsonObject { ["role"] = "assistant", ["content"] = blocks });
            }
        }
        FlushResults();

        var body = new JsonObject { ["model"] = Model, ["max_tokens"] = maxTokens, ["system"] = system, ["messages"] = msgs };
        if (tools is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var t in tools)
                arr.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["input_schema"] = t.Schema.DeepClone() });
            body["tools"] = arr;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/messages") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("x-api-key", _key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        var (res, err) = await Wire.PostAsync(req, ct);
        if (err is not null) return ChatResponse.Fail(err);

        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        if (res?["content"] is JsonArray content)
            foreach (var b in content)
            {
                var type = (string?)b?["type"];
                if (type == "text") text.Append((string?)b!["text"]);
                else if (type == "tool_use") calls.Add(new ToolCall((string)b!["id"]!, (string)b["name"]!, b["input"] as JsonObject ?? new JsonObject()));
            }
        return new ChatResponse(text.ToString().Trim(), calls, null, (int?)res?["usage"]?["input_tokens"] ?? 0, (int?)res?["usage"]?["output_tokens"] ?? 0);
    }
}

/// <summary>Google Gemini generateContent API.</summary>
public sealed class GeminiChatClient : IChatClient
{
    private readonly string _endpoint, _key;
    public string ProviderId => "gemini";
    public string Model { get; }
    public bool SupportsVision => true;

    public GeminiChatClient(string endpoint, string key, string model) => (_endpoint, _key, Model) = (endpoint.TrimEnd('/'), key, model);

    private static JsonObject CleanSchema(JsonObject schema)
    {
        var s = (JsonObject)schema.DeepClone();
        void Walk(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                o.Remove("additionalProperties");
                foreach (var kv in o.ToList()) Walk(kv.Value);
            }
            else if (n is JsonArray a) foreach (var x in a) Walk(x);
        }
        Walk(s);
        return s;
    }

    public async Task<ChatResponse> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools,
        CancellationToken ct, int maxTokens = 1024)
    {
        var contents = new JsonArray();
        JsonArray? pendingResults = null;
        void Flush() { if (pendingResults is not null) { contents.Add(new JsonObject { ["role"] = "user", ["parts"] = pendingResults }); pendingResults = null; } }

        foreach (var m in messages)
        {
            if (m.Role == "tool")
            {
                pendingResults ??= new JsonArray();
                pendingResults.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject { ["name"] = m.ToolName, ["response"] = new JsonObject { ["result"] = m.Text ?? "" } },
                });
                continue;
            }
            Flush();
            if (m.Role == "user")
            {
                var parts = new JsonArray { new JsonObject { ["text"] = m.Text ?? "" } };
                if (m.Images is not null)
                    foreach (var img in m.Images)
                        parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = img.MediaType, ["data"] = Convert.ToBase64String(img.Data) } });
                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = parts });
            }
            else if (m.Raw is JsonArray raw) contents.Add(new JsonObject { ["role"] = "model", ["parts"] = raw.DeepClone() });
            else
            {
                var parts = new JsonArray();
                if (!string.IsNullOrEmpty(m.Text)) parts.Add(new JsonObject { ["text"] = m.Text });
                if (m.ToolCalls is not null)
                    foreach (var c in m.ToolCalls)
                        parts.Add(new JsonObject { ["functionCall"] = new JsonObject { ["name"] = c.Name, ["args"] = c.Args.DeepClone() } });
                contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
            }
        }
        Flush();

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system } } },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = maxTokens },
        };
        if (tools is { Count: > 0 })
        {
            var decls = new JsonArray();
            foreach (var t in tools)
                decls.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = CleanSchema(t.Schema) });
            body["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = decls } };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/models/{Model}:generateContent") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("x-goog-api-key", _key);
        var (res, err) = await Wire.PostAsync(req, ct);
        if (err is not null) return ChatResponse.Fail(err);

        var parts2 = res?["candidates"]?[0]?["content"]?["parts"] as JsonArray;
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        if (parts2 is not null)
            foreach (var p in parts2)
            {
                if ((bool?)p?["thought"] == true) continue;
                if (p?["text"] is { } t) text.Append((string?)t);
                if (p?["functionCall"] is JsonObject fc) calls.Add(new ToolCall(Guid.NewGuid().ToString("N")[..8], (string)fc["name"]!, fc["args"] as JsonObject ?? new JsonObject()));
            }
        if (parts2 is null && res?["promptFeedback"]?["blockReason"] is { } br) return ChatResponse.Fail($"Blocked by Gemini: {br}");
        return new ChatResponse(text.ToString().Trim(), calls, null, (int?)res?["usageMetadata"]?["promptTokenCount"] ?? 0,
            (int?)res?["usageMetadata"]?["candidatesTokenCount"] ?? 0, parts2?.DeepClone());
    }
}

/// <summary>Native Ollama /api/chat (local models, no API key).</summary>
public sealed class OllamaChatClient : IChatClient
{
    private readonly string _endpoint;
    public string ProviderId => "ollama";
    public string Model { get; }
    public bool SupportsVision => Regex.IsMatch(Model, "llava|vision|gemma3|minicpm-v|qwen2.5vl|qwen3-vl|llama3.2-vision", RegexOptions.IgnoreCase);

    public OllamaChatClient(string endpoint, string model) => (_endpoint, Model) = (endpoint.TrimEnd('/'), model);

    public async Task<ChatResponse> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools,
        CancellationToken ct, int maxTokens = 1024)
    {
        var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "user":
                    var u = new JsonObject { ["role"] = "user", ["content"] = m.Text ?? "" };
                    if (m.Images is { Count: > 0 })
                    {
                        var imgs = new JsonArray();
                        foreach (var i in m.Images) imgs.Add(Convert.ToBase64String(i.Data));
                        u["images"] = imgs;
                    }
                    msgs.Add(u);
                    break;
                case "assistant":
                    var a = new JsonObject { ["role"] = "assistant", ["content"] = m.Text ?? "" };
                    if (m.ToolCalls is { Count: > 0 })
                    {
                        var calls = new JsonArray();
                        foreach (var c in m.ToolCalls)
                            calls.Add(new JsonObject { ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Args.DeepClone() } });
                        a["tool_calls"] = calls;
                    }
                    msgs.Add(a);
                    break;
                case "tool":
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_name"] = m.ToolName, ["content"] = m.Text ?? "" });
                    break;
            }
        }

        var body = new JsonObject
        {
            ["model"] = Model, ["messages"] = msgs, ["stream"] = false, ["think"] = false,
            ["options"] = new JsonObject { ["num_ctx"] = 8192, ["num_predict"] = maxTokens, ["temperature"] = 0.2 },
            ["keep_alive"] = "10m",
        };
        if (tools is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var t in tools)
                arr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Schema.DeepClone() },
                });
            body["tools"] = arr;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/api/chat") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        var (res, err) = await Wire.PostAsync(req, ct);
        if (err is not null)
            return ChatResponse.Fail(err.Contains("does not support tools") ? Loc.F("The model “{0}” does not support tool calling. Pick another Ollama model (for example qwen3 or llama3.1).", Model) : err);

        var msg = res?["message"];
        var list = new List<ToolCall>();
        if (msg?["tool_calls"] is JsonArray tc)
            foreach (var c in tc)
            {
                var fn = c!["function"]!;
                list.Add(new ToolCall(Guid.NewGuid().ToString("N")[..8], (string)fn["name"]!,
                    fn["arguments"] is JsonObject o ? o : Wire.ParseArgs((string?)fn["arguments"])));
            }
        return new ChatResponse(Wire.StripThinking((string?)msg?["content"]), list, null, (int?)res?["prompt_eval_count"] ?? 0, (int?)res?["eval_count"] ?? 0);
    }
}

public static class ChatClientFactory
{
    /// <summary>Builds a client for the configured provider. Returns an error message instead when it isn't usable yet.</summary>
    public static (IChatClient? Client, string? Error) Create(string providerId, string model, string? endpoint, Func<string, string?> loadKey)
    {
        var provider = Providers.ProviderCatalog.Get(providerId);
        var ep = string.IsNullOrWhiteSpace(endpoint) ? provider.DefaultEndpoint : endpoint;
        if (string.IsNullOrWhiteSpace(model)) return (null, Loc.T("No model is selected. Choose one under Settings ▸ AI model."));
        var key = loadKey(Security.CredentialStore.ProviderKeyName(providerId));
        if (provider.RequiresApiKey && string.IsNullOrWhiteSpace(key)) return (null, Loc.F("No API key is saved for {0}. Add it under Settings ▸ AI model.", provider.DisplayName));
        return providerId switch
        {
            "anthropic" => (new AnthropicChatClient(ep, key!, model), null),
            "gemini" => (new GeminiChatClient(ep, key!, model), null),
            "ollama" => (new OllamaChatClient(ep, model), null),
            _ => (new OpenAiChatClient(providerId, ep, key, model), null),
        };
    }
}
