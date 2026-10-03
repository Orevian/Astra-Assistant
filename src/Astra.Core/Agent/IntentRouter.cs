using Astra.Core.Index;

namespace Astra.Core.Agent;

public enum LocalIntentKind { Open, Close }

public sealed record LocalStep(LocalIntentKind Kind, string Query, AppHit App);

/// <summary>
/// Resolves simple "open X / close Y" commands (Turkish and English) entirely on this PC.
/// Anything ambiguous, compound in other ways, or not clearly an app returns null so the LLM takes over.
/// </summary>
public sealed class IntentRouter
{
    private readonly SearchService _search;
    public IntentRouter(SearchService search) => _search = search;

    private static readonly HashSet<string> OpenVerbs = new() { "ac", "acar", "acarmisin", "baslat", "calistir", "open", "launch", "start", "run" };
    private static readonly HashSet<string> CloseVerbs = new() { "kapat", "kapatir", "kapa", "kapatirmisin", "close", "quit", "exit", "terminate", "kill" };
    private static readonly HashSet<string> Splitters = new() { "ve", "and", "sonra", "ardindan", "then", "ayrica", "also" };
    private static readonly HashSet<string> Filler = new()
    {
        "astra", "lutfen", "please", "can", "could", "you", "bana", "misin", "musun", "mi", "mu", "the", "my", "a", "an", "bir", "su", "bu",
        "uygulamasi", "uygulamasini", "uygulama", "programi", "program", "app", "application", "oyununu", "oyunu", "oyun", "game", "tarayicisini", "tarayici",
        "pls", "hey", "hadi", "bi", "yap", "et", "up",
    };
    // Turkish case suffixes that survive as separate tokens after the apostrophe ("Chrome'u" → chrome u).
    private static readonly HashSet<string> Suffixes = new() { "u", "i", "yi", "yu", "a", "e", "ya", "ye", "nu", "ni", "un", "in", "da", "de", "ta", "te", "dan", "den", "yle", "yla", "la", "le" };

    public List<LocalStep>? TryResolve(string text)
    {
        var tokens = TextNorm.Tokens(text);
        if (tokens.Length == 0 || tokens.Length > 14) return null;

        var clauses = new List<List<string>> { new() };
        foreach (var t in tokens)
        {
            if (Splitters.Contains(t)) { if (clauses[^1].Count > 0) clauses.Add(new()); }
            else clauses[^1].Add(t);
        }
        clauses.RemoveAll(c => c.Count == 0);
        if (clauses.Count == 0 || clauses.Count > 4) return null;

        var steps = new List<LocalStep>();
        foreach (var clause in clauses)
        {
            var verbIdx = clause.FindIndex(t => OpenVerbs.Contains(t) || CloseVerbs.Contains(t));
            if (verbIdx < 0) return null;
            var kind = OpenVerbs.Contains(clause[verbIdx]) ? LocalIntentKind.Open : LocalIntentKind.Close;

            // Anything after the verb in Turkish ("... aç") or before it in English ("open ...") is the object; take the side that has words.
            var before = clause.Take(verbIdx).ToList();
            var after = clause.Skip(verbIdx + 1).ToList();
            var obj = Clean(before.Count > 0 ? before : after);
            if (before.Count > 0 && after.Count > 0 && Clean(after).Count > 0) return null; // words on both sides: not a simple command
            if (obj.Count == 0 || obj.Count > 4) return null;

            var query = string.Join(' ', obj);
            var app = Resolve(query);
            if (app is null) return null;
            steps.Add(new LocalStep(kind, query, app));
        }
        return steps;
    }

    private static List<string> Clean(List<string> tokens) =>
        tokens.Where(t => !Filler.Contains(t) && !(t.Length <= 3 && Suffixes.Contains(t))).ToList();

    private AppHit? Resolve(string query)
    {
        var hits = _search.SearchApplications(query, 3);
        if (hits.Count == 0) return null;
        var top = hits[0];
        if (top.Score < 0.75) return null;
        if (hits.Count > 1 && top.Score < 0.99 && hits[1].Score >= top.Score - (top.Score < 0.8 ? 0.15 : 0.08)) return null; // ambiguous: let the LLM ask
        return top;
    }
}
