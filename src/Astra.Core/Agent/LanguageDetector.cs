using System.Text.RegularExpressions;
using Astra.Core.Index;

namespace Astra.Core.Agent;

/// <summary>Cheap offline language guess (Turkish vs English) so replies match the speaker without an LLM call.</summary>
public static class LanguageDetector
{
    private static readonly HashSet<string> TurkishWords = new()
    {
        "ac", "kapat", "ve", "bir", "bu", "icin", "nasil", "lutfen", "merhaba", "evet", "hayir", "ne", "neden", "nerede", "ekran", "dosya",
        "klasor", "ara", "bul", "git", "gir", "tikla", "yaz", "sil", "tasi", "kopyala", "baslat", "calistir", "oynat", "ses", "kis", "artir",
        "bana", "benim", "senin", "mi", "mu", "misin", "musun", "sonra", "ardindan", "simdi", "bilgisayar", "tarayici", "uygulama", "kirmizi",
        "mavi", "yesil", "buton", "sarki", "video", "izle", "dinle", "belgeler", "indirilenler", "masaustu", "saat", "kac", "hava", "tamam",
        "iptal", "goster", "soyle", "anlat", "oku", "kaydet", "kapa", "kilitle", "yeniden", "uyku", "hatirla", "unut",
    };

    private static readonly HashSet<string> EnglishWords = new()
    {
        "the", "and", "open", "close", "please", "what", "how", "where", "search", "find", "go", "click", "type", "delete", "move", "copy",
        "start", "launch", "run", "play", "volume", "screen", "file", "folder", "my", "me", "you", "then", "now", "computer", "browser",
        "app", "application", "red", "blue", "green", "button", "song", "video", "watch", "listen", "downloads", "documents", "desktop",
        "time", "show", "tell", "read", "save", "lock", "restart", "sleep", "remember", "forget", "to", "in", "on", "of", "for", "is", "it",
    };

    public static string Detect(string text, string fallback = "en")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (Regex.IsMatch(text, "[çğıöşüÇĞİÖŞÜ]")) return "tr";
        var tokens = TextNorm.Tokens(text);
        var tr = tokens.Count(TurkishWords.Contains);
        var en = tokens.Count(EnglishWords.Contains);
        if (tr == en) return fallback;
        return tr > en ? "tr" : "en";
    }
}
