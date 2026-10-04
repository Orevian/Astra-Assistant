using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Astra.Core.Index;

public sealed record AppHit(long Id, string Name, string Kind, string LaunchKind, string Launch, string? ExePath, double Score)
{
    public string AppId => $"app_{Id}";
}

public sealed record FileHit(string Path, string Name, string Ext, long Size, DateTime Modified, bool IsFolder);

/// <summary>Scoped local lookups. Each call returns only a handful of rows, which is all the LLM ever sees.</summary>
public sealed class SearchService
{
    private readonly IndexDatabase _db;
    public SearchService(IndexDatabase db) => _db = db;

    public IndexDatabase Database => _db;

    // ---- Applications ------------------------------------------------------

    public List<AppHit> SearchApplications(string query, int limit = 5, string? kind = null)
    {
        var qn = TextNorm.Normalize(query);
        if (qn.Length == 0) return new();
        var candidates = new Dictionary<long, AppHit>();

        using var c = _db.Open();
        // 1) FTS prefix match, 2) substring fallback, 3) fuzzy over the whole (small) app table.
        var fts = TextNorm.ToFtsQuery(query);
        if (fts.Length > 0)
            Collect(c, "SELECT a.id,a.name,a.name_norm,a.kind,a.launch_kind,a.launch,a.exe_path FROM applications_fts f JOIN applications a ON a.id=f.rowid WHERE applications_fts MATCH $q LIMIT 40",
                candidates, qn, ("$q", fts));
        Collect(c, "SELECT id,name,name_norm,kind,launch_kind,launch,exe_path FROM applications WHERE name_norm LIKE $l LIMIT 40",
            candidates, qn, ("$l", "%" + qn + "%"));
        if (candidates.Count == 0 || candidates.Values.Max(h => h.Score) < 0.8)
            Collect(c, "SELECT id,name,name_norm,kind,launch_kind,launch,exe_path FROM applications", candidates, qn);

        return candidates.Values
            .Where(h => h.Score >= 0.45 && (kind is null || h.Kind == kind))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => KindRank(h.Kind)).ThenBy(h => h.Name.Length)
            .Take(limit).ToList();
    }

    private static int KindRank(string kind) => kind switch { "browser" => 0, "game" => 1, "app" => 2, _ => 3 };

    private static void Collect(SqliteConnection c, string sql, Dictionary<long, AppHit> into, string qn, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var score = AppAliases.Score(qn, r.GetString(2));
            if (into.TryGetValue(id, out var old) && old.Score >= score) continue;
            into[id] = new AppHit(id, r.GetString(1), r.GetString(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), score);
        }
    }

    public AppHit? GetApplication(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,kind,launch_kind,launch,exe_path FROM applications WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new AppHit(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), 1) : null;
    }

    public static long? ParseAppId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var s = id.Trim();
        if (s.StartsWith("app_", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return long.TryParse(s, out var n) ? n : null;
    }

    public List<(string BrowserId, string Name, string? Exe, string ProfileDir, string ProfileName)> BrowserProfiles(string? browserQuery = null)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT browser_id,browser_name,exe_path,profile_dir,profile_name FROM browser_profiles";
        var list = new List<(string, string, string?, string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4)));
        if (string.IsNullOrWhiteSpace(browserQuery)) return list;
        var q = TextNorm.Normalize(browserQuery);
        return list.Where(p => TextNorm.Normalize(p.Item2).Contains(q) || p.Item1.Contains(q)).ToList();
    }

    // ---- Files & folders ---------------------------------------------------

    public static string? ResolveLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var l = location.Trim().Trim('"');
        if (Path.IsPathRooted(l)) return l;
        var map = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            ["downloads"] = Environment.SpecialFolder.UserProfile, // Downloads is resolved below
            ["documents"] = Environment.SpecialFolder.MyDocuments, ["belgeler"] = Environment.SpecialFolder.MyDocuments,
            ["desktop"] = Environment.SpecialFolder.DesktopDirectory, ["masaüstü"] = Environment.SpecialFolder.DesktopDirectory, ["masaustu"] = Environment.SpecialFolder.DesktopDirectory,
            ["pictures"] = Environment.SpecialFolder.MyPictures, ["resimler"] = Environment.SpecialFolder.MyPictures,
            ["music"] = Environment.SpecialFolder.MyMusic, ["müzik"] = Environment.SpecialFolder.MyMusic,
            ["videos"] = Environment.SpecialFolder.MyVideos, ["videolar"] = Environment.SpecialFolder.MyVideos,
        };
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (l.Equals("downloads", StringComparison.OrdinalIgnoreCase) || l.Equals("indirilenler", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(profile, "Downloads");
        if (l.Equals("home", StringComparison.OrdinalIgnoreCase) || l.Equals("user", StringComparison.OrdinalIgnoreCase)) return profile;
        return map.TryGetValue(l, out var sf) ? Environment.GetFolderPath(sf) : l;
    }

    public static (DateTime? From, DateTime? To) ParseDateRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return (null, null);
        var now = DateTime.Now;
        var today = now.Date;
        switch (range.Trim().ToLowerInvariant().Replace(' ', '_'))
        {
            case "today": return (today, null);
            case "yesterday": return (today.AddDays(-1), today);
            case "last_week" or "this_week": return (today.AddDays(-7), null);
            case "last_month" or "this_month": return (today.AddMonths(-1), null);
            case "last_year" or "this_year": return (today.AddYears(-1), null);
        }
        var parts = range.Split("..", StringSplitOptions.TrimEntries);
        DateTime? Parse(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
        return parts.Length == 2 ? (Parse(parts[0]), Parse(parts[1])) : (Parse(parts[0]), null);
    }

    public List<FileHit> SearchFiles(string? query, string? location = null, string? extension = null, string? dateRange = null, int limit = 10, bool recursive = true)
    {
        var results = new List<FileHit>();
        var fts = TextNorm.ToFtsQuery(query);
        var loc = ResolveLocation(location);
        var (from, to) = ParseDateRange(dateRange);

        var sql = new System.Text.StringBuilder();
        var args = new List<(string, object?)>();
        var where = new List<string>();
        sql.Append("SELECT fo.path, f.name, f.ext, f.size, f.modified FROM files f JOIN folders fo ON fo.id=f.folder_id ");
        if (fts.Length > 0) { where.Add("f.id IN (SELECT rowid FROM files_fts WHERE files_fts MATCH $q)"); args.Add(("$q", fts)); }
        if (!string.IsNullOrEmpty(loc))
        {
            args.Add(("$loc", loc.TrimEnd('\\')));
            if (recursive)
            {
                where.Add("(fo.path = $loc COLLATE NOCASE OR fo.path LIKE $locl ESCAPE '\\')");
                args.Add(("$locl", EscapeLike(loc.TrimEnd('\\')) + "\\\\%"));
            }
            else where.Add("fo.path = $loc COLLATE NOCASE");
        }
        if (!string.IsNullOrWhiteSpace(extension)) { where.Add("f.ext = $ext"); args.Add(("$ext", extension.TrimStart('.').ToLowerInvariant())); }
        if (from is not null) { where.Add("f.modified >= $from"); args.Add(("$from", new DateTimeOffset(from.Value).ToUnixTimeSeconds())); }
        if (to is not null) { where.Add("f.modified < $to"); args.Add(("$to", new DateTimeOffset(to.Value).ToUnixTimeSeconds())); }
        if (where.Count == 0) return results;
        sql.Append("WHERE ").Append(string.Join(" AND ", where)).Append(" ORDER BY f.modified DESC LIMIT $lim");
        args.Add(("$lim", limit > 100 ? Math.Clamp(limit, 1, 5000) : Math.Clamp(limit, 1, 50) * 4));

        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var qn = TextNorm.Normalize(query);
        var scored = new List<(FileHit Hit, double Score)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var name = r.GetString(1);
                var hit = new FileHit(Path.Combine(r.GetString(0), name), name, r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? 0 : r.GetInt64(3), DateTimeOffset.FromUnixTimeSeconds(r.IsDBNull(4) ? 0 : r.GetInt64(4)).LocalDateTime, false);
                scored.Add((hit, qn.Length > 0 ? TextNorm.Similarity(qn, TextNorm.Normalize(name)) : 0));
            }
        return scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Hit.Modified)
            .Take(Math.Clamp(limit, 1, 5000)).Select(s => s.Hit).ToList();
    }

    public List<FileHit> SearchFolders(string? query, string? location = null, int limit = 10)
    {
        var results = new List<FileHit>();
        var fts = TextNorm.ToFtsQuery(query);
        if (fts.Length == 0) return results;
        var loc = ResolveLocation(location);
        var sql = "SELECT fo.path, fo.name, fo.modified FROM folders fo WHERE fo.id IN (SELECT rowid FROM folders_fts WHERE folders_fts MATCH $q)";
        if (!string.IsNullOrEmpty(loc)) sql += " AND (fo.path = $loc COLLATE NOCASE OR fo.path LIKE $locl ESCAPE '\\')";
        sql += " LIMIT 200";
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$q", fts);
        if (!string.IsNullOrEmpty(loc))
        {
            cmd.Parameters.AddWithValue("$loc", loc.TrimEnd('\\'));
            cmd.Parameters.AddWithValue("$locl", EscapeLike(loc.TrimEnd('\\')) + "\\\\%");
        }
        var qn = TextNorm.Normalize(query);
        var scored = new List<(FileHit, double)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.GetString(1);
            scored.Add((new FileHit(r.GetString(0), name, "", 0, DateTimeOffset.FromUnixTimeSeconds(r.IsDBNull(2) ? 0 : r.GetInt64(2)).LocalDateTime, true),
                TextNorm.Similarity(qn, TextNorm.Normalize(name)) - r.GetString(0).Count(ch => ch == '\\') * 0.01));
        }
        return scored.OrderByDescending(s => s.Item2).Take(Math.Clamp(limit, 1, 50)).Select(s => s.Item1).ToList();
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
