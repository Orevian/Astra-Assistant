using System.Text;

namespace Astra.Core.Index;

/// <summary>
/// Spoken names rarely match installed names ("VS Code", "krom", "diskord"). This supplies common spoken forms,
/// initialisms, and a sound-alike key so speech-to-text mistakes still find the right app.
/// </summary>
public static class AppAliases
{
    // normalized app-name fragment → spoken forms (normalized)
    private static readonly (string Fragment, string[] Aliases)[] Table =
    {
        ("visual studio code", new[] { "vs code", "vscode", "vs kod", "visual studio kod", "vi es kod", "vs kodu", "vc kot", "vc kod", "vi si kot", "vsc" }),
        ("visual studio", new[] { "vs", "visual studio", "vizual studyo" }),
        ("google chrome", new[] { "chrome", "krom", "google krom", "gugil krom" }),
        ("microsoft edge", new[] { "edge", "ej", "edj" }),
        ("discord", new[] { "diskord", "discord" }),
        ("spotify", new[] { "spotifay", "spotify" }),
        ("steam", new[] { "stim", "steam" }),
        ("whatsapp", new[] { "vatsap", "votsap", "whatsapp" }),
        ("notepad", new[] { "not defteri", "notepad", "notpad" }),
        ("not defteri", new[] { "notepad", "notpad" }),
        ("hesap makinesi", new[] { "calculator", "hesap makinasi", "calc" }),
        ("calculator", new[] { "hesap makinesi", "calc" }),
        ("file explorer", new[] { "explorer", "dosya gezgini" }),
        ("dosya gezgini", new[] { "explorer", "file explorer" }),
        ("task manager", new[] { "gorev yoneticisi" }),
        ("gorev yoneticisi", new[] { "task manager" }),
        ("windows terminal", new[] { "terminal", "komut satiri" }),
        ("powershell", new[] { "power shell", "pavirsel" }),
        ("microsoft word", new[] { "word", "vord" }),
        ("microsoft excel", new[] { "excel", "eksel" }),
        ("microsoft powerpoint", new[] { "powerpoint", "pavirpoint" }),
        ("valorant", new[] { "valorant", "valorent", "valorant oyunu" }),
        ("avast secure browser", new[] { "avast", "avast tarayici", "avast browser" }),
        ("obs studio", new[] { "obs" }),
        ("telegram", new[] { "telegram", "telegram desktop" }),
        ("zoom", new[] { "zum", "zoom" }),
        ("teams", new[] { "timis", "tims" }),
    };

    /// <summary>Alternative spoken names for an installed app, including an initialism for multi-word names.</summary>
    public static IEnumerable<string> For(string nameNorm)
    {
        foreach (var (fragment, aliases) in Table)
            if (nameNorm.Contains(fragment))
                foreach (var a in aliases) yield return a;

        var words = nameNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && words.Length <= 4)
            yield return new string(words.Select(w => w[0]).ToArray()); // "visual studio code" → "vsc"
    }

    /// <summary>
    /// Sound-alike key: voicing and vowels collapsed, so "vc kot" and "vs code" both become "vkt"-like keys.
    /// Not linguistically exact, just good enough to rescue near-misses.
    /// </summary>
    public static string Phonetic(string text)
    {
        var s = TextNorm.Normalize(text).Replace(" ", "");
        s = s.Replace("ph", "f").Replace("kh", "k").Replace("ck", "k").Replace("sh", "s").Replace("ch", "c");
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i] switch
            {
                'w' => 'v', 'q' => 'k', 'x' => 'k', 'c' => 'k', 'g' => 'k', 'd' => 't', 'b' => 'p', 'z' => 's', 'j' => 's', 'y' => 'i',
                var other => other,
            };
            if ("aeiou".Contains(ch) && sb.Length > 0) continue;           // keep a leading vowel only
            if (sb.Length > 0 && sb[^1] == ch) continue;                   // collapse repeats
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>0..1 sound-alike similarity between what was heard and a candidate name.</summary>
    public static double PhoneticSimilarity(string query, string candidate)
    {
        var a = Phonetic(query);
        var b = Phonetic(candidate);
        if (a.Length < 3 || b.Length < 3) return 0;
        var dist = TextNorm.Levenshtein(a, b);
        return 1.0 - (double)dist / Math.Max(a.Length, b.Length);
    }

    /// <summary>Best score of the heard text against the app's name, its aliases and its sound.</summary>
    public static double Score(string queryNorm, string nameNorm)
    {
        var best = TextNorm.Similarity(queryNorm, nameNorm);
        foreach (var alias in For(nameNorm))
            best = Math.Max(best, TextNorm.Similarity(queryNorm, alias) * 0.97);
        // Sound-alike rescue; deliberately capped so a real text match always wins.
        var phon = Math.Max(PhoneticSimilarity(queryNorm, nameNorm), For(nameNorm).Select(a => PhoneticSimilarity(queryNorm, a)).DefaultIfEmpty(0).Max());
        if (phon >= 0.74) best = Math.Max(best, 0.5 + 0.34 * phon);
        return best;
    }
}
