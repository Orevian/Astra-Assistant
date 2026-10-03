using Microsoft.Data.Sqlite;

namespace Astra.Core.Index;

/// <summary>SQLite + FTS5 store for the local computer index. Nothing here is ever sent to an LLM wholesale.</summary>
public sealed class IndexDatabase
{
    public string Path { get; }
    private static readonly object InitGate = new();
    private bool _initialized;

    public IndexDatabase(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(Settings.SettingsStore.DataDirectory, "index.db");
    }

    public SqliteConnection Open()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = true }.ToString());
        conn.Open();
        Exec(conn, "PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=15000;");
        EnsureSchema(conn);
        return conn;
    }

    private void EnsureSchema(SqliteConnection conn)
    {
        if (_initialized) return;
        lock (InitGate)
        {
            if (_initialized) return;
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, Schema);
            _initialized = true;
        }
    }

    public static void Exec(SqliteConnection conn, string sql, params (string, object?)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(SqliteConnection conn, string sql, params (string, object?)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        if (r is null || r is DBNull) return default;
        return (T)Convert.ChangeType(r, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public string? GetMeta(string key)
    {
        using var c = Open();
        return Scalar<string>(c, "SELECT value FROM metadata WHERE key=$k", ("$k", key));
    }

    public void SetMeta(string key, string value)
    {
        using var c = Open();
        Exec(c, "INSERT INTO metadata(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    }

    public IndexStats Stats()
    {
        using var c = Open();
        return new IndexStats(
            Scalar<long>(c, "SELECT COUNT(*) FROM applications"),
            Scalar<long>(c, "SELECT COUNT(*) FROM folders"),
            Scalar<long>(c, "SELECT COUNT(*) FROM files"),
            Scalar<long>(c, "SELECT COUNT(*) FROM drives"),
            Scalar<string>(c, "SELECT value FROM metadata WHERE key='last_scan'"));
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT);

        CREATE TABLE IF NOT EXISTS applications(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            name_norm TEXT NOT NULL,
            kind TEXT NOT NULL,            -- app | game | browser | system
            launch_kind TEXT NOT NULL,     -- file | uwp | uri
            launch TEXT NOT NULL,          -- path, AppUserModelID or URI
            exe_path TEXT,
            publisher TEXT,
            version TEXT,
            source TEXT,
            updated INTEGER,
            UNIQUE(launch_kind, launch)
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS applications_fts USING fts5(name_norm, publisher_norm, tokenize='unicode61', prefix='2 3');

        CREATE TABLE IF NOT EXISTS drives(
            letter TEXT PRIMARY KEY, label TEXT, format TEXT, type TEXT, total INTEGER, free INTEGER, updated INTEGER);

        CREATE TABLE IF NOT EXISTS folders(
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL UNIQUE COLLATE NOCASE,
            name TEXT NOT NULL,
            name_norm TEXT NOT NULL,
            parent_id INTEGER,
            modified INTEGER);
        CREATE INDEX IF NOT EXISTS ix_folders_parent ON folders(parent_id);
        CREATE VIRTUAL TABLE IF NOT EXISTS folders_fts USING fts5(name_norm, tokenize='unicode61', prefix='2 3');

        CREATE TABLE IF NOT EXISTS files(
            id INTEGER PRIMARY KEY,
            folder_id INTEGER NOT NULL,
            name TEXT NOT NULL COLLATE NOCASE,
            name_norm TEXT NOT NULL,
            ext TEXT,
            size INTEGER,
            created INTEGER,
            modified INTEGER,
            UNIQUE(folder_id, name));
        CREATE INDEX IF NOT EXISTS ix_files_modified ON files(modified);
        CREATE INDEX IF NOT EXISTS ix_files_ext ON files(ext);
        CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(name_norm, tokenize='unicode61', prefix='2 3');

        CREATE TABLE IF NOT EXISTS processes(pid INTEGER, name TEXT, path TEXT, started INTEGER, snapshot INTEGER);
        CREATE TABLE IF NOT EXISTS services(name TEXT PRIMARY KEY, display_name TEXT, state TEXT, start_type TEXT);
        CREATE TABLE IF NOT EXISTS browser_profiles(
            browser_id TEXT, browser_name TEXT, exe_path TEXT, profile_dir TEXT, profile_name TEXT,
            PRIMARY KEY(browser_id, profile_dir));
        """;
}

public sealed record IndexStats(long Applications, long Folders, long Files, long Drives, string? LastScan);
