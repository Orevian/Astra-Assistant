using System.Globalization;

namespace Astra.Core.Localization;

/// <summary>
/// Lightweight UI localization. English source strings are the keys; unknown strings fall back to English.
/// Translating works in both directions so already-translated UI can be switched back without rebuilding it.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> ToTr = Merge(TurkishStrings.Map, TurkishStringsExtra.Map);
    private static Dictionary<string, string> Merge(params Dictionary<string, string>[] maps)
    {
        var all = new Dictionary<string, string>();
        foreach (var m in maps) foreach (var (k, v) in m) all[k] = v;
        return all;
    }

    private static readonly Dictionary<string, string> ToEn = ToTr.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static string Language { get; private set; } = "en";
    public static bool IsTurkish => Language == "tr";
    public static event Action? Changed;

    /// <summary>Applies the user's setting: "auto" (match Windows), "en" or "tr".</summary>
    public static void Set(string setting)
    {
        var lang = setting switch
        {
            "tr" => "tr",
            "en" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en",
        };
        if (lang == Language) return;
        Language = lang;
        Changed?.Invoke();
    }

    /// <summary>Translates a UI string (English or already-Turkish) into the current language.</summary>
    public static string T(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (IsTurkish) return ToTr.TryGetValue(text, out var tr) ? tr : text;
        return ToEn.TryGetValue(text, out var en) ? en : text;
    }

    public static string F(string format, params object[] args) => string.Format(T(format), args);
}
