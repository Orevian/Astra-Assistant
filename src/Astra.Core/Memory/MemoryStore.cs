using System.Text.RegularExpressions;
using Astra.Core.Index;
using Microsoft.Data.Sqlite;

namespace Astra.Core.Memory;

public sealed record MemoryEntry(long Id, string Text, DateTime Created);

/// <summary>Long-term memory. Local only, user-viewable and deletable, and it refuses obviously sensitive content.</summary>
public sealed class MemoryStore
{
    private readonly string _path;
    private readonly Settings.SettingsStore _settings;

    public MemoryStore(Settings.SettingsStore settings, string? path = null)
    {
        _settings = settings;
        _path = path ?? Path.Combine(Settings.SettingsStore.DataDirectory, "memory.db");
        using var c = Open();
        Exec(c, "CREATE TABLE IF NOT EXISTS memories(id INTEGER PRIMARY KEY, text TEXT NOT NULL, text_norm TEXT NOT NULL, created INTEGER NOT NULL)");
    }

    public bool Enabled => _settings.Current.Memory.Enabled;

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static readonly Regex[] Sensitive =
    {
        new(@"\b(password\w*|passwd|pwd|şifre\w*|sifre\w*|parola\w*|pin kodu\w*|cvv|cvc|otp|secret\w*|api[ _-]?key\w*|token\w*|private key|seed phrase|kredi kart\w*|credit card\w*|kart no\w*)\b", RegexOptions.IgnoreCase),
        new(@"\b(?:\d[ -]?){13,19}\b"),
        new(@"\b(sk|pk|ghp|gho|xox[bap]|AKIA|AIza)[-_A-Za-z0-9]{16,}\b"),
        new(@"\bTR\d{2}[ ]?(?:\d{4}[ ]?){5}\d{2}\b", RegexOptions.IgnoreCase),
        new(@"\b[A-Za-z0-9+/=_-]{32,}\b"),
    };

    public static bool LooksSensitive(string text) => Sensitive.Any(r => r.IsMatch(text));

    public (bool Ok, string Message) Add(string text)
    {
        if (!Enabled) return (false, "Memory is turned off in settings.");
        text = text.Trim();
        if (text.Length < 3) return (false, "Nothing to remember.");
        if (text.Length > 400) text = text[..400];
        if (LooksSensitive(text)) return (false, "That looks sensitive (password, key, card or ID number), so it was not saved.");
        var norm = TextNorm.Normalize(text);
        using var c = Open();
        using var chk = c.CreateCommand();
        chk.CommandText = "SELECT COUNT(*) FROM memories WHERE text_norm=$n";
        chk.Parameters.AddWithValue("$n", norm);
        if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return (true, "Already remembered.");
        Exec(c, "INSERT INTO memories(text,text_norm,created) VALUES($t,$n,$c)", ("$t", text), ("$n", norm), ("$c", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return (true, "Saved.");
    }

    public List<MemoryEntry> All()
    {
        var list = new List<MemoryEntry>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,text,created FROM memories ORDER BY created DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MemoryEntry(r.GetInt64(0), r.GetString(1), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2)).LocalDateTime));
        return list;
    }

    /// <summary>The few memories relevant to a request (token overlap), kept small to protect the context budget.</summary>
    public List<string> Relevant(string request, int limit = 3)
    {
        if (!Enabled) return new();
        var tokens = TextNorm.Tokens(request).Where(t => t.Length > 2).ToHashSet();
        if (tokens.Count == 0) return new();
        return All().Select(m => (m, score: TextNorm.Tokens(m.Text).Count(t => tokens.Contains(t))))
            .Where(x => x.score > 0).OrderByDescending(x => x.score).Take(limit).Select(x => x.m.Text).ToList();
    }

    public int Forget(string query)
    {
        var q = TextNorm.Normalize(query);
        if (q.Length == 0) return 0;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE text_norm LIKE $q";
        cmd.Parameters.AddWithValue("$q", "%" + q + "%");
        return cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = Open();
        Exec(c, "DELETE FROM memories WHERE id=$id", ("$id", id));
    }

    public void Clear()
    {
        using var c = Open();
        Exec(c, "DELETE FROM memories");
    }
}
