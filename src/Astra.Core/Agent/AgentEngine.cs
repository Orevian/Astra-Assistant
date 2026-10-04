using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astra.Core.Assistant;
using Astra.Core.Index;
using Astra.Core.Llm;
using Astra.Core.Native;
using Astra.Core.Tools;

namespace Astra.Core.Agent;

public enum AgentEventKind { Understanding, Thinking, Step, StepDone, Completed, Failed }

public sealed record AgentEvent(AgentEventKind Kind, string Text, bool? Ok = null);

public sealed record AgentResult(bool Ok, string Message, string Language, bool UsedLlm, int Steps);

/// <summary>
/// Understand → plan → act → observe → verify → respond. Simple commands never reach the LLM; everything else
/// runs a tool-calling loop where each tool result goes back to the model, with a minimal context.
/// </summary>
public sealed class AgentEngine
{
    private readonly AstraRuntime _rt;
    private readonly IntentRouter _router;
    private readonly List<(string User, string Assistant)> _history = new();

    public AgentEngine(AstraRuntime rt)
    {
        _rt = rt;
        _router = new IntentRouter(rt.Index.Search);
    }

    public void ClearHistory() => _history.Clear();

    private static string L(string lang, string en, string tr) => lang == "tr" ? tr : en;

    public async Task<AgentResult> RunAsync(string rawText, string? languageHint, Action<AgentEvent> emit, CancellationToken ct)
    {
        var text = StripWakeWord(rawText.Trim());
        var settings = _rt.Settings.Current;
        var fallbackLang = Localization.Loc.Language;
        var lang = settings.Voice.ResponseLanguage == "auto"
            ? LanguageDetector.Detect(text, languageHint ?? fallbackLang)
            : settings.Voice.ResponseLanguage[..2];
        var task = new TaskState { Language = lang };
        _rt.Ui.ClearCache();
        emit(new AgentEvent(AgentEventKind.Understanding, text));

        // 1. Local-first: no LLM for simple app commands.
        if (settings.Automation.LocalFirst && _rt.Index.HasIndex)
        {
            var steps = _router.TryResolve(text);
            if (steps is not null)
            {
                var local = await RunLocalAsync(steps, task, lang, emit, ct);
                Remember(text, local.Message);
                return local;
            }
        }

        // 2. LLM tool loop.
        var (client, error) = _rt.CreateChatClient();
        if (client is null)
        {
            var msg = L(lang, error ?? "No AI model is configured.", $"Yapay zekâ modeli ayarlanmamış. {Localization.Loc.T(error ?? "")}");
            emit(new AgentEvent(AgentEventKind.Failed, msg));
            return new AgentResult(false, msg, lang, false, 0);
        }

        var result = await RunLlmAsync(client, text, lang, task, emit, ct);
        Remember(text, result.Message);
        return result;
    }

    private void Remember(string user, string assistant)
    {
        _history.Add((user, assistant));
        var max = Math.Max(0, _rt.Settings.Current.Memory.ShortTermTurns);
        while (_history.Count > max) _history.RemoveAt(0);
    }

    private static string StripWakeWord(string text)
    {
        var t = Regex.Replace(text, @"^\s*(hey\s+|hi\s+|hey,?\s+)?astra\s*[,.:!\-]*\s*", "", RegexOptions.IgnoreCase);
        return t.Length == 0 ? text : t;
    }

    // ---- Local path ---------------------------------------------------------

    private async Task<AgentResult> RunLocalAsync(List<LocalStep> steps, TaskState task, string lang, Action<AgentEvent> emit, CancellationToken ct)
    {
        var sentences = new List<string>();
        var allOk = true;
        foreach (var s in steps)
        {
            var tool = s.Kind == LocalIntentKind.Open ? "open_application" : "close_application";
            var args = new JsonObject { ["app_id"] = s.App.AppId };
            var r = await _rt.Tools.ExecuteAsync(tool, args, task, ct);
            emit(new AgentEvent(AgentEventKind.StepDone, r.Message, r.Ok));
            allOk &= r.Ok;
            var name = s.App.Name;
            sentences.Add(s.Kind == LocalIntentKind.Open
                ? r.Ok ? (r.Verified ? L(lang, $"{name} is open.", $"{name} açıldı.") : L(lang, $"I started {name}, but couldn't confirm it opened.", $"{name} başlatıldı ama açıldığını doğrulayamadım."))
                       : L(lang, $"I couldn't open {name}: {r.Message}", $"{name} açılamadı: {r.Message}")
                : r.Ok ? L(lang, $"{name} is closed.", $"{name} kapatıldı.")
                       : L(lang, $"I couldn't close {name}: {r.Message}", $"{name} kapatılamadı: {r.Message}"));
            if (task.Declined) break;
        }
        var message = string.Join(" ", sentences);
        emit(new AgentEvent(allOk ? AgentEventKind.Completed : AgentEventKind.Failed, message));
        return new AgentResult(allOk, message, lang, false, steps.Count);
    }

    // ---- LLM path -----------------------------------------------------------

    private async Task<AgentResult> RunLlmAsync(IChatClient client, string text, string lang, TaskState task, Action<AgentEvent> emit, CancellationToken ct)
    {
        var settings = _rt.Settings.Current;
        var maxSteps = Math.Clamp(settings.Automation.MaxAgentSteps, 3, 100);
        var tools = SelectTools(text);
        var system = BuildSystemPrompt(text, lang);

        var messages = new List<ChatMessage>();
        foreach (var (u, a) in _history) { messages.Add(ChatMessage.User(u)); messages.Add(new ChatMessage("assistant", a)); }
        messages.Add(ChatMessage.User(text));

        var seen = new Dictionary<string, int>();
        var steps = 0;
        var lastText = "";

        for (var turn = 0; turn < maxSteps + 1; turn++)
        {
            ct.ThrowIfCancellationRequested();
            emit(new AgentEvent(AgentEventKind.Thinking, ""));
            var res = await client.CompleteAsync(system, messages, tools, ct);
            if (!res.Ok)
            {
                emit(new AgentEvent(AgentEventKind.Failed, res.Error!));
                return new AgentResult(false, res.Error!, lang, true, steps);
            }
            if (!string.IsNullOrWhiteSpace(res.Text)) lastText = res.Text!;

            if (res.ToolCalls.Count == 0)
            {
                var final = string.IsNullOrWhiteSpace(res.Text) ? L(lang, "Done.", "Tamam.") : res.Text!;
                emit(new AgentEvent(AgentEventKind.Completed, final));
                return new AgentResult(true, final, lang, true, steps);
            }

            if (steps >= maxSteps)
            {
                var msg = L(lang, "That task needs more steps than the limit in your settings. Tell me if I should continue.",
                    "Bu görev ayarlardaki adım sınırından fazla adım gerektiriyor. Devam etmemi ister misin?");
                emit(new AgentEvent(AgentEventKind.Failed, msg));
                return new AgentResult(false, msg, lang, true, steps);
            }

            messages.Add(new ChatMessage("assistant", res.Text, res.ToolCalls, Raw: res.Raw));
            foreach (var call in res.ToolCalls)
            {
                steps++;
                var key = call.Name + call.Args.ToJsonString();
                seen[key] = seen.GetValueOrDefault(key) + 1;
                ToolResult r;
                if (seen[key] > 2) r = ToolResult.Fail("You already made this exact call twice. Try a different approach or answer the user.");
                else r = await _rt.Tools.ExecuteAsync(call.Name, call.Args, task, ct);
                emit(new AgentEvent(AgentEventKind.StepDone, r.Message, r.Ok));
                messages.Add(ChatMessage.Tool(call, r.ForModel(call.Name is "browser_read_page" ? 3200 : 1800)));
                if (task.Declined)
                {
                    var msg = L(lang, "Okay, I won't do that.", "Tamam, bunu yapmayacağım.");
                    emit(new AgentEvent(AgentEventKind.Completed, msg));
                    return new AgentResult(true, msg, lang, true, steps);
                }
            }
        }

        var fail = string.IsNullOrWhiteSpace(lastText) ? L(lang, "I couldn't finish that task.", "Bu görevi tamamlayamadım.") : lastText;
        emit(new AgentEvent(AgentEventKind.Failed, fail));
        return new AgentResult(false, fail, lang, true, steps);
    }

    private string BuildSystemPrompt(string request, string lang)
    {
        var s = _rt.Settings.Current;
        var sb = new StringBuilder();
        sb.AppendLine("You are Astra, a Windows desktop agent. You act on the user's PC only through the provided tools and never claim something happened unless a tool confirmed it.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Locate things with search_applications / search_files / search_folders; never guess ids or paths.");
        sb.AppendLine("- Use the most specific tool. Prefer UI Automation (find_ui_element) over coordinates; use vision tools only when needed.");
        sb.AppendLine("- Check every tool result (ok / verified). If a step fails, diagnose and try a different approach before giving up.");
        sb.AppendLine("- Do multi-step tasks in order. When a browser is named, pass it as the browser argument; browser tools start the browser themselves, so do not open it separately.");
        sb.AppendLine("- To play or open a video/song use play_youtube_video: it opens the first real result. browser_search only opens a results page and never plays anything.");
        sb.AppendLine("- Never say something was opened, played or done unless a tool result confirms it. If verified is false, say you could not confirm it.");
        sb.AppendLine("- The system asks the user to confirm risky actions; if a result says the user declined, stop.");
        sb.AppendLine($"- Finish with ONE short plain sentence of under 25 words (no markdown, no lists, no file paths unless asked) in {(lang == "tr" ? "Turkish" : "English")}. Ask one short question if you truly need information.");
        sb.Append($"Now: {DateTime.Now:yyyy-MM-dd HH:mm dddd}. User: {Environment.UserName}. Displays: {Displays.All().Count}.");
        var browser = _rt.Browser.ResolveBrowser(null)?.Name;
        if (browser is not null) sb.Append($" Preferred browser: {browser}.");
        sb.AppendLine();
        var memories = _rt.Memory.Relevant(request, 3);
        if (memories.Count > 0) sb.AppendLine("Known about the user: " + string.Join("; ", memories));
        return sb.ToString();
    }

    // ---- Tool selection: keep the prompt small ------------------------------

    private static readonly string[] CoreTools =
    {
        "search_applications", "open_application", "close_application", "launch_url", "browser_search", "browser_navigate", "play_youtube_video",
        "get_browser", "get_window", "window_action", "wait", "remember", "forget", "get_system_information", "get_processes",
        "search_files", "search_folders",
    };

    private static readonly string[] FileTools = { "create_file", "read_file", "list_directory", "move_file", "copy_file", "delete_file", "move_matching_files", "delete_matching_files", "open_path" };
    private static readonly string[] InputTools = { "find_ui_element", "click", "double_click", "move_mouse", "scroll", "type_text", "press_key", "screenshot", "analyze_screen", "click_element_by_description", "clipboard_get", "clipboard_set", "set_volume" };
    private static readonly string[] PageTools = { "browser_read_page", "browser_click", "browser_type", "browser_tabs", "browser_screenshot" };
    private static readonly string[] SystemOnly = { "run_command", "power_action" };

    private IReadOnlyList<ToolSpec> SelectTools(string request)
    {
        var t = " " + TextNorm.Normalize(request) + " ";
        bool Has(params string[] words) => words.Any(w => t.Contains(" " + w + " ") || t.Contains(" " + w));
        var names = new HashSet<string>(CoreTools);

        var files = Has("dosya", "klasor", "belge", "pdf", "tasi", "kopyala", "sil", "olustur", "file", "folder", "move", "copy", "delete", "create", "download", "indir", "masaustu", "desktop", "documents", "zip", "txt", "oku", "read", "yaz");
        var input = Has("tikla", "click", "yaz", "type", "bas", "press", "ekran", "screen", "mouse", "fare", "tus", "key", "kaydir", "scroll", "pano", "clipboard", "buton", "button", "dugme", "ses", "volume", "kis", "artir", "sessiz", "mute", "kaydet", "ctrl", "enter", "form");
        var page = Has("sayfa", "page", "sekme", "tab", "giris", "login", "form", "oku", "read", "icerik", "content", "site", "formu", "doldur", "fill", "tikla", "click");
        var sys = Has("komut", "command", "powershell", "cmd", "terminal", "yeniden", "restart", "shutdown", "kapat bilgisayar", "uyku", "sleep", "kilitle", "lock", "oturum", "signout", "sign");

        if (files) foreach (var n in FileTools) names.Add(n);
        if (input) foreach (var n in InputTools) names.Add(n);
        if (page) foreach (var n in PageTools) names.Add(n);
        if (sys) foreach (var n in SystemOnly) names.Add(n);
        // Ambiguous long requests get the broad set (never the dangerous system tools).
        if (!files && !input && !page && !sys && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 9)
            foreach (var n in InputTools.Concat(PageTools).Concat(FileTools)) names.Add(n);

        return _rt.Tools.Specs().Where(s => names.Contains(s.Name)).ToList();
    }
}
