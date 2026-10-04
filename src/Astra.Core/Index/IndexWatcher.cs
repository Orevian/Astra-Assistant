using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Astra.Core.Index;

/// <summary>
/// Keeps the index current after the first scan: file/folder create, rename, delete, and app install/removal.
/// Events are coalesced for a moment, then applied in one transaction.
/// </summary>
public sealed class IndexWatcher : IDisposable
{
    private readonly IndexDatabase _db;
    private readonly ComputerIndexer _indexer;
    private readonly Func<IReadOnlyCollection<string>> _excluded;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentQueue<(WatcherChangeTypes Type, string Path, string? OldPath)> _events = new();
    private Timer? _timer;
    private long _lastAppRefreshTicks;
    private int _appRefreshPending;
    private int _busy;

    public event Action<string>? Log;

    public IndexWatcher(IndexDatabase db, ComputerIndexer indexer, Func<IReadOnlyCollection<string>>? excluded = null)
    {
        _db = db;
        _indexer = indexer;
        _excluded = excluded ?? (() => Array.Empty<string>());
    }

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (IsRunning) return;
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            try
            {
                var w = new FileSystemWatcher(d.RootDirectory.FullName)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                w.Created += (_, e) => Enqueue(WatcherChangeTypes.Created, e.FullPath, null);
                w.Deleted += (_, e) => Enqueue(WatcherChangeTypes.Deleted, e.FullPath, null);
                w.Changed += (_, e) => Enqueue(WatcherChangeTypes.Changed, e.FullPath, null);
                w.Renamed += (_, e) => Enqueue(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
                w.Error += (_, e) => Log?.Invoke("watcher overflow: " + e.GetException().Message);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) { Log?.Invoke($"watch {d.Name}: {ex.Message}"); }
        }

        // Start Menu changes mean an app was installed or removed.
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                 }.Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(root, "*.lnk") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            FileSystemEventHandler h = (_, _) => Interlocked.Exchange(ref _appRefreshPending, 1);
            w.Created += h; w.Deleted += h;
            w.Renamed += (_, _) => Interlocked.Exchange(ref _appRefreshPending, 1);
            _watchers.Add(w);
        }

        _lastAppRefreshTicks = Environment.TickCount64;
        _timer = new Timer(_ => Drain(), null, 2000, 2000);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    public void Dispose() => Stop();

    private void Enqueue(WatcherChangeTypes type, string path, string? old)
    {
        if (_events.Count > 200_000) return; // runaway burst: a rescan is the right answer, not an unbounded queue
        _events.Enqueue((type, path, old));
    }

    private void Drain()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (_indexer.IsScanning) { while (_events.TryDequeue(out _)) { } return; }

            var batch = new List<(WatcherChangeTypes Type, string Path, string? OldPath)>();
            while (batch.Count < 5000 && _events.TryDequeue(out var e)) batch.Add(e);
            if (batch.Count > 0) Apply(batch);

            var refreshDue = Interlocked.Exchange(ref _appRefreshPending, 0) == 1 && Environment.TickCount64 - _lastAppRefreshTicks > 5000;
            var periodic = Environment.TickCount64 - _lastAppRefreshTicks > 15 * 60_000;
            if (refreshDue || periodic)
            {
                _lastAppRefreshTicks = Environment.TickCount64;
                try { _indexer.RefreshApplications(); Log?.Invoke("applications refreshed"); } catch (Exception ex) { Log?.Invoke(ex.Message); }
            }
        }
        catch (Exception ex) { Log?.Invoke(ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    internal void Apply(List<(WatcherChangeTypes Type, string Path, string? OldPath)> batch)
    {
        var excluded = _excluded().Select(p => p.TrimEnd('\\') + "\\").ToArray();
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        // Last event per path wins, except renames which must run in order.
        foreach (var (type, path, old) in batch)
        {
            if (IsExcluded(path, excluded) && !(type == WatcherChangeTypes.Renamed && old is not null && !IsExcluded(old, excluded))) continue;
            switch (type)
            {
                case WatcherChangeTypes.Deleted: Delete(c, tx, path); break;
                case WatcherChangeTypes.Renamed when old is not null: Rename(c, tx, old, path); break;
                default: Upsert(c, tx, path); break;
            }
        }
        tx.Commit();
    }

    private static bool IsExcluded(string path, string[] excluded)
    {
        if (path.StartsWith(ComputerIndexer.WindowsDir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var seg in path.Split('\\'))
            if (ComputerIndexer.SkipDirNames.Contains(seg)) return true;
        foreach (var e in excluded)
            if (path.StartsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    private static long? FolderId(SqliteConnection c, SqliteTransaction tx, string path)
    {
        using var cmd = Cmd(c, tx, "SELECT id FROM folders WHERE path=$p", ("$p", path.Length > 3 ? path.TrimEnd('\\') : path));
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt64(r);
    }

    private static void Upsert(SqliteConnection c, SqliteTransaction tx, string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null) return;
        var parentId = FolderId(c, tx, parent);
        if (parentId is null) return; // parent isn't indexed (excluded or not yet scanned)

        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return;
            var nn = TextNorm.Normalize(di.Name);
            using var ins = Cmd(c, tx, "INSERT INTO folders(path,name,name_norm,parent_id,modified) VALUES($p,$n,$nn,$pid,$m) ON CONFLICT(path) DO UPDATE SET modified=$m RETURNING id",
                ("$p", di.FullName), ("$n", di.Name), ("$nn", nn), ("$pid", parentId), ("$m", ComputerIndexer.Unix(di.LastWriteTimeUtc)));
            var id = Convert.ToInt64(ins.ExecuteScalar());
            using var del = Cmd(c, tx, "DELETE FROM folders_fts WHERE rowid=$id", ("$id", id)); del.ExecuteNonQuery();
            using var fts = Cmd(c, tx, "INSERT INTO folders_fts(rowid,name_norm) VALUES($id,$nn)", ("$id", id), ("$nn", nn)); fts.ExecuteNonQuery();
        }
        else if (File.Exists(path))
        {
            if (ComputerIndexer.SkipFilesIn(parent)) return;
            var fi = new FileInfo(path);
            var nn = TextNorm.Normalize(fi.Name);
            using var ins = Cmd(c, tx, "INSERT INTO files(folder_id,name,name_norm,ext,size,created,modified) VALUES($f,$n,$nn,$e,$s,$c,$m) ON CONFLICT(folder_id,name) DO UPDATE SET size=$s,modified=$m RETURNING id",
                ("$f", parentId), ("$n", fi.Name), ("$nn", nn), ("$e", fi.Extension.TrimStart('.').ToLowerInvariant()),
                ("$s", fi.Length), ("$c", ComputerIndexer.Unix(fi.CreationTimeUtc)), ("$m", ComputerIndexer.Unix(fi.LastWriteTimeUtc)));
            var id = Convert.ToInt64(ins.ExecuteScalar());
            using var del = Cmd(c, tx, "DELETE FROM files_fts WHERE rowid=$id", ("$id", id)); del.ExecuteNonQuery();
            using var fts = Cmd(c, tx, "INSERT INTO files_fts(rowid,name_norm) VALUES($id,$nn)", ("$id", id), ("$nn", nn)); fts.ExecuteNonQuery();
        }
    }

    private static void Delete(SqliteConnection c, SqliteTransaction tx, string path)
    {
        var trimmed = path.TrimEnd('\\');
        // A deleted folder takes its whole subtree with it.
        var like = trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "\\\\%";
        var ids = new List<long>();
        using (var q = Cmd(c, tx, "SELECT id FROM folders WHERE path=$p COLLATE NOCASE OR path LIKE $l ESCAPE '\\'", ("$p", trimmed), ("$l", like)))
        using (var r = q.ExecuteReader())
            while (r.Read()) ids.Add(r.GetInt64(0));
        foreach (var id in ids)
        {
            Cmd(c, tx, "DELETE FROM files_fts WHERE rowid IN (SELECT id FROM files WHERE folder_id=$id)", ("$id", id)).ExecuteNonQuery();
            Cmd(c, tx, "DELETE FROM files WHERE folder_id=$id", ("$id", id)).ExecuteNonQuery();
            Cmd(c, tx, "DELETE FROM folders_fts WHERE rowid=$id", ("$id", id)).ExecuteNonQuery();
            Cmd(c, tx, "DELETE FROM folders WHERE id=$id", ("$id", id)).ExecuteNonQuery();
        }
        if (ids.Count > 0) return;

        var parent = Path.GetDirectoryName(trimmed);
        var name = Path.GetFileName(trimmed);
        if (parent is null) return;
        var parentId = FolderId(c, tx, parent);
        if (parentId is null) return;
        Cmd(c, tx, "DELETE FROM files_fts WHERE rowid IN (SELECT id FROM files WHERE folder_id=$f AND name=$n)", ("$f", parentId), ("$n", name)).ExecuteNonQuery();
        Cmd(c, tx, "DELETE FROM files WHERE folder_id=$f AND name=$n", ("$f", parentId), ("$n", name)).ExecuteNonQuery();
    }

    private static void Rename(SqliteConnection c, SqliteTransaction tx, string oldPath, string newPath)
    {
        var oldT = oldPath.TrimEnd('\\');
        var newT = newPath.TrimEnd('\\');
        var oldId = FolderId(c, tx, oldT);
        if (oldId is not null)
        {
            var like = oldT.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "\\\\%";
            Cmd(c, tx, "UPDATE folders SET path = $n || substr(path, length($o)+1) WHERE path LIKE $l ESCAPE '\\'", ("$n", newT), ("$o", oldT), ("$l", like)).ExecuteNonQuery();
            var name = Path.GetFileName(newT);
            var nn = TextNorm.Normalize(name);
            var newParent = Path.GetDirectoryName(newT);
            var newParentId = newParent is null ? null : FolderId(c, tx, newParent);
            Cmd(c, tx, "UPDATE folders SET path=$n, name=$nm, name_norm=$nn, parent_id=COALESCE($pid,parent_id) WHERE id=$id",
                ("$n", newT), ("$nm", name), ("$nn", nn), ("$pid", newParentId), ("$id", oldId)).ExecuteNonQuery();
            Cmd(c, tx, "DELETE FROM folders_fts WHERE rowid=$id", ("$id", oldId)).ExecuteNonQuery();
            Cmd(c, tx, "INSERT INTO folders_fts(rowid,name_norm) VALUES($id,$nn)", ("$id", oldId), ("$nn", nn)).ExecuteNonQuery();
            return;
        }
        Delete(c, tx, oldPath);
        Upsert(c, tx, newPath);
    }
}
