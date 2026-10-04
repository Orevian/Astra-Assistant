using System.Globalization;
using System.Text;

namespace Astra.Core.Index;

/// <summary>Search normalization: lower-case, Turkish-aware, diacritics stripped, punctuation → spaces.</summary>
public static class TextNorm
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Normalize(NormalizationForm.FormD))
        {
            var ch = raw switch
            {
                'İ' or 'I' or 'ı' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ç' or 'Ç' => 'c',
                'ö' or 'Ö' => 'o',
                'ü' or 'Ü' => 'u',
                _ => raw,
            };
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    public static string[] Tokens(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Builds an FTS5 MATCH expression: every token must match as a prefix.</summary>
    public static string ToFtsQuery(string? text)
    {
        var t = Tokens(text);
        return t.Length == 0 ? "" : string.Join(" ", t.Select(x => $"\"{x.Replace("\"", "")}\"*"));
    }

    public static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>0..1 similarity between a user query and a candidate name (both already normalized).</summary>
    public static double Similarity(string query, string candidate)
    {
        if (query.Length == 0 || candidate.Length == 0) return 0;
        if (query == candidate) return 1.0;
        if (candidate.StartsWith(query)) return 0.92;
        var qTokens = query.Split(' ');
        var cTokens = candidate.Split(' ');
        if (qTokens.All(q => cTokens.Any(c => c.StartsWith(q)))) return 0.85 - Math.Min(0.15, (cTokens.Length - qTokens.Length) * 0.03);
        // Turkish suffixes: "makinesini" should still match "makinesi".
        if (qTokens.All(q => cTokens.Any(c => c.StartsWith(q) || (c.Length >= 3 && q.StartsWith(c) && q.Length - c.Length <= 4)))
            && cTokens.Length <= qTokens.Length + 1) return 0.9;
        if (candidate.Contains(query)) return 0.7;
        var dist = Levenshtein(query, candidate.Length > query.Length ? candidate[..Math.Min(candidate.Length, query.Length + 2)] : candidate);
        return Math.Max(0, 1.0 - (double)dist / Math.Max(query.Length, 1)) * 0.65;
    }
}
