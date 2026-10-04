using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Astra.Core.Index;

public enum ScanPhase { Applications, Drives, Folders, Files, System, BuildingIndex }

public sealed record ScanProgress(ScanPhase Phase, double Fraction, bool PhaseDone, long Applications, long Folders, long Files);

/// <summary>
/// Full Windows scan into the local SQLite/FTS5 index. Metadata only; file contents are never read.
/// </summary>
public sealed class ComputerIndexer
{
    private readonly IndexDatabase _db;
    private readonly Func<IReadOnlyCollection<string>> _excluded;
    private readonly Func<bool> _includeFiles;

    public ComputerIndexer(IndexDatabase db, Func<IReadOnlyCollection<string>>? excluded = null, Func<bool>? includeFiles = null)
    {
        _db = db;
        _excluded = excluded ?? (() => Array.Empty<string>());
        _includeFiles = includeFiles ?? (() => true);
    }

    public bool IsScanning { get; private set; }

    // Directories that are never worth indexing.
    internal static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Windows.old", "node_modules", ".git", "__pycache__",
        ".gradle", "Code Cache", "GPUCache", "ShaderCache", "Service Worker", "CacheStorage", "$GetCurrent", "Recovery", "WinSxS",
    };

    public static string WindowsDir { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public async Task ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            await Task.Run(() => ScanCore(progress, ct), ct);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ScanCore(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        long apps = 0, folderCount = 0, fileCount = 0;
        void Report(ScanPhase p, double f, bool done = false) => progress?.Report(new ScanProgress(p, f, done, apps, folderCount, fileCount));

        // 1. Applications
        Report(ScanPhase.Applications, 0);
        apps = RefreshApplications(ct);
        Report(ScanPhase.Applications, 1, true);

        // 2. Drives
        Report(ScanPhase.Drives, 0);
        var drives = ScanDrives();
        Report(ScanPhase.Drives, 1, true);

        // 3 + 4. Folders & files
        Report(ScanPhase.Folders, 0);
        ScanFileSystem(drives, ct, (folders, files, fraction) =>
        {
            folderCount = folders;
            fileCount = files;
            Report(ScanPhase.Folders, fraction);
            Report(ScanPhase.Files, fraction);
        });
        Report(ScanPhase.Folders, 1, true);
        Report(ScanPhase.Files, 1, true);

        // 5. System
        Report(ScanPhase.System, 0);
        ScanSystem();
        Report(ScanPhase.System, 1, true);

        Report(ScanPhase.BuildingIndex, 0);
        using (var c = _db.Open())
        {
            IndexDatabase.Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
            IndexDatabase.Exec(c, "PRAGMA optimize;");
        }
        _db.SetMeta("last_scan", DateTime.UtcNow.ToString("O"));
        Report(ScanPhase.BuildingIndex, 1, true);
    }

    // ---- Applications ------------------------------------------------------

    /// <summary>Re-scans apps and upserts them, keeping existing ids. Removed apps disappear from the index.</summary>
    public long RefreshApplications(CancellationToken ct = default)
    {
        var records = ApplicationScanner.Scan(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        IndexDatabase.Exec(c, "UPDATE applications SET updated=0");
        foreach (var r in records)
        {
            var norm = TextNorm.Normalize(r.Name);
            long id;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO applications(name,name_norm,kind,launch_kind,launch,exe_path,publisher,version,source,updated)
                    VALUES($n,$nn,$k,$lk,$l,$e,$p,$v,$s,$u)
                    ON CONFLICT(launch_kind,launch) DO UPDATE SET name=$n,name_norm=$nn,kind=$k,exe_path=$e,publisher=$p,version=$v,source=$s,updated=$u
                    RETURNING id;
                    """;
                cmd.Parameters.AddWithValue("$n", r.Name);
                cmd.Parameters.AddWithValue("$nn", norm);
                cmd.Parameters.AddWithValue("$k", r.Kind);
                cmd.Parameters.AddWithValue("$lk", r.LaunchKind);
                cmd.Parameters.AddWithValue("$l", r.Launch);
                cmd.Parameters.AddWithValue("$e", (object?)r.ExePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$p", (object?)r.Publisher ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$v", (object?)r.Version ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$s", r.Source);
                cmd.Parameters.AddWithValue("$u", now);
                id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            IndexDatabase.Exec(c, "DELETE FROM applications_fts WHERE rowid=$id", ("$id", id));
            IndexDatabase.Exec(c, "INSERT INTO applications_fts(rowid,name_norm,publisher_norm) VALUES($id,$n,$p)",
                ("$id", id), ("$n", norm), ("$p", TextNorm.Normalize(r.Publisher)));
        }
        IndexDatabase.Exec(c, "DELETE FROM applications_fts WHERE rowid IN (SELECT id FROM applications WHERE updated=0)");
        IndexDatabase.Exec(c, "DELETE FROM applications WHERE updated=0");
        tx.Commit();

        // Browser profiles live next to apps.
        var profiles = ApplicationScanner.ScanBrowserProfiles();
        using var tx2 = c.BeginTransaction();
        IndexDatabase.Exec(c, "DELETE FROM browser_profiles");
        foreach (var p in profiles)
            IndexDatabase.Exec(c, "INSERT OR REPLACE INTO browser_profiles VALUES($i,$n,$e,$d,$pn)",
                ("$i", p.BrowserId), ("$n", p.BrowserName), ("$e", p.ExePath), ("$d", p.ProfileDir), ("$pn", p.ProfileName));
        tx2.Commit();
        return records.Count;
    }

    // ---- Drives ------------------------------------------------------------

    private List<DriveInfo> ScanDrives()
    {
        var result = new List<DriveInfo>();
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        IndexDatabase.Exec(c, "DELETE FROM drives");
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                IndexDatabase.Exec(c, "INSERT INTO drives VALUES($l,$lab,$f,$t,$tot,$free,$u)",
                    ("$l", d.Name), ("$lab", d.VolumeLabel), ("$f", d.DriveFormat), ("$t", d.DriveType.ToString()),
                    ("$tot", d.TotalSize), ("$free", d.AvailableFreeSpace), ("$u", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                if (d.DriveType is DriveType.Fixed or DriveType.Removable) result.Add(d);
            }
            catch { }
        }
        tx.Commit();
        return result;
    }

    // ---- Folders & files ---------------------------------------------------

    private sealed record FolderRow(long Id, string Path, string Name, long ParentId, long Modified);
    private sealed record FileRow(long Id, long FolderId, string Name, string Ext, long Size, long Created, long Modified);
    private sealed class Batch
    {
        public List<FolderRow> Folders { get; } = new();
        public List<FileRow> Files { get; } = new();
        public bool Full => Folders.Count + Files.Count >= 3000;
    }

    private void ScanFileSystem(List<DriveInfo> drives, CancellationToken ct, Action<long, long, double> onProgress)
    {
        using (var c = _db.Open())
        {
            IndexDatabase.Exec(c, "DROP TABLE IF EXISTS files_fts; DROP TABLE IF EXISTS files; DROP TABLE IF EXISTS folders_fts; DROP TABLE IF EXISTS folders;");
        }
        RecreateFileTables();

        var excluded = _excluded().Select(p => p.TrimEnd('\\') + "\\").ToArray();
        _filesOn = _includeFiles();
        long nextFolderId = 0, nextFileId = 0, folderTotal = 0, fileTotal = 0;
        var queue = new BlockingCollection<Batch>(boundedCapacity: 40);

        // Writer: single thread, one big transaction per few batches.
        var writer = Task.Run(() =>
        {
            using var conn = _db.Open();
            var tx = conn.BeginTransaction();
            var folderCmd = Prepare(conn, tx, "INSERT OR IGNORE INTO folders(id,path,name,name_norm,parent_id,modified) VALUES($id,$p,$n,$nn,$pid,$m)", "$id", "$p", "$n", "$nn", "$pid", "$m");
            var folderFts = Prepare(conn, tx, "INSERT INTO folders_fts(rowid,name_norm) VALUES($id,$nn)", "$id", "$nn");
            var fileCmd = Prepare(conn, tx, "INSERT OR IGNORE INTO files(id,folder_id,name,name_norm,ext,size,created,modified) VALUES($id,$f,$n,$nn,$e,$s,$c,$m)", "$id", "$f", "$n", "$nn", "$e", "$s", "$c", "$m");
            var fileFts = Prepare(conn, tx, "INSERT INTO files_fts(rowid,name_norm) VALUES($id,$nn)", "$id", "$nn");
            var sinceCommit = 0;
            foreach (var b in queue.GetConsumingEnumerable())
            {
                foreach (var f in b.Folders)
                {
                    var nn = TextNorm.Normalize(f.Name);
                    Set(folderCmd, f.Id, f.Path, f.Name, nn, f.ParentId, f.Modified); folderCmd.ExecuteNonQuery();
                    Set(folderFts, f.Id, nn); folderFts.ExecuteNonQuery();
                }
                foreach (var f in b.Files)
                {
                    var nn = TextNorm.Normalize(f.Name);
                    Set(fileCmd, f.Id, f.FolderId, f.Name, nn, f.Ext, f.Size, f.Created, f.Modified); fileCmd.ExecuteNonQuery();
                    Set(fileFts, f.Id, nn); fileFts.ExecuteNonQuery();
                }
                if (++sinceCommit >= 8)
                {
                    tx.Commit(); tx.Dispose();
                    tx = conn.BeginTransaction();
                    foreach (var cmd in new[] { folderCmd, folderFts, fileCmd, fileFts }) cmd.Transaction = tx;
                    sinceCommit = 0;
                }
            }
            tx.Commit();
            tx.Dispose();
        });

        try
        {
            // Build the work list: depth ≤ 2 directories become independent tasks so progress moves evenly.
            var tasks = new List<(string Path, long Id)>();
            var rootBatch = new Batch();
            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                var rootId = Interlocked.Increment(ref nextFolderId);
                rootBatch.Folders.Add(new FolderRow(rootId, drive.RootDirectory.FullName, drive.RootDirectory.FullName, 0, 0));
                SplitIntoTasks(drive.RootDirectory.FullName, rootId, 0, tasks, rootBatch, excluded, ref nextFolderId, ref nextFileId, ct);
            }
            folderTotal += rootBatch.Folders.Count; fileTotal += rootBatch.Files.Count;
            queue.Add(rootBatch, ct);

            var done = 0;
            var total = Math.Max(1, tasks.Count);
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6), CancellationToken = ct };
            Parallel.ForEach(tasks, options, task =>
            {
                var batch = new Batch();
                var stack = new Stack<(string Path, long Id)>();
                stack.Push(task);
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var (path, id) = stack.Pop();
                    EnumerateDir(path, id, stack, batch, excluded, ref nextFolderId, ref nextFileId);
                    if (batch.Full)
                    {
                        Interlocked.Add(ref folderTotal, batch.Folders.Count);
                        Interlocked.Add(ref fileTotal, batch.Files.Count);
                        queue.Add(batch, ct);
                        batch = new Batch();
                    }
                }
                Interlocked.Add(ref folderTotal, batch.Folders.Count);
                Interlocked.Add(ref fileTotal, batch.Files.Count);
                queue.Add(batch, ct);
                var d = Interlocked.Increment(ref done);
                onProgress(Interlocked.Read(ref folderTotal), Interlocked.Read(ref fileTotal), Math.Min(0.99, (double)d / total));
            });
        }
        finally
        {
            queue.CompleteAdding();
            writer.Wait();
        }
        onProgress(Interlocked.Read(ref folderTotal), Interlocked.Read(ref fileTotal), 1);
    }

    private void RecreateFileTables()
    {
        using var c = new SqliteConnection($"Data Source={_db.Path}");
        c.Open();
        IndexDatabase.Exec(c, """
            CREATE TABLE IF NOT EXISTS folders(id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, name TEXT NOT NULL, name_norm TEXT NOT NULL, parent_id INTEGER, modified INTEGER);
            CREATE INDEX IF NOT EXISTS ix_folders_parent ON folders(parent_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS folders_fts USING fts5(name_norm, tokenize='unicode61', prefix='2 3');
            CREATE TABLE IF NOT EXISTS files(id INTEGER PRIMARY KEY, folder_id INTEGER NOT NULL, name TEXT NOT NULL COLLATE NOCASE, name_norm TEXT NOT NULL, ext TEXT, size INTEGER, created INTEGER, modified INTEGER, UNIQUE(folder_id, name));
            CREATE INDEX IF NOT EXISTS ix_files_modified ON files(modified);
            CREATE INDEX IF NOT EXISTS ix_files_ext ON files(ext);
            CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(name_norm, tokenize='unicode61', prefix='2 3');
            """);
    }

    private static SqliteCommand Prepare(SqliteConnection c, SqliteTransaction tx, string sql, params string[] names)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var n in names) cmd.Parameters.Add(n, SqliteType.Text);
        cmd.Prepare();
        return cmd;
    }

    private static void Set(SqliteCommand cmd, params object[] values)
    {
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i];
    }

    internal static bool SkipDir(string fullPath, string name, FileAttributes attrs, string[] excluded)
    {
        if ((attrs & FileAttributes.ReparsePoint) != 0) return true;
        if (SkipDirNames.Contains(name)) return true;
        if (fullPath.StartsWith(WindowsDir + "\\", StringComparison.OrdinalIgnoreCase) || fullPath.Equals(WindowsDir, StringComparison.OrdinalIgnoreCase)) return true;
        var withSlash = fullPath.TrimEnd('\\') + "\\";
        foreach (var e in excluded)
            if (withSlash.StartsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Files are skipped (folders still indexed) in program/cache locations nobody searches for documents in.</summary>
    internal static bool SkipFilesIn(string dirPath)
    {
        var p = dirPath.ToLowerInvariant();
        return p.Contains("\\program files") || p.Contains("\\programdata\\") || p.Contains("\\appdata\\local\\");
    }

    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private void SplitIntoTasks(string path, long id, int depth, List<(string, long)> tasks, Batch batch, string[] excluded,
        ref long nextFolderId, ref long nextFileId, CancellationToken ct)
    {
        // depth 0 = drive root, depth 1 = top-level folders. Each depth-2 folder becomes a task.
        var stack = new Stack<(string, long)>();
        EnumerateDir(path, id, stack, batch, excluded, ref nextFolderId, ref nextFileId);
        foreach (var (childPath, childId) in stack.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (depth >= 1) tasks.Add((childPath, childId));
            else SplitIntoTasks(childPath, childId, depth + 1, tasks, batch, excluded, ref nextFolderId, ref nextFileId, ct);
        }
    }

    private static void EnumerateDir(string path, long folderId, Stack<(string Path, long Id)> subdirs, Batch batch, string[] excluded,
        ref long nextFolderId, ref long nextFileId)
    {
        IEnumerable<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(path).EnumerateFileSystemInfos("*", EnumOptions); }
        catch { return; }

        var indexFiles = _filesOn && !SkipFilesIn(path);
        try
        {
            foreach (var e in entries)
            {
                if (e is DirectoryInfo di)
                {
                    if (SkipDir(di.FullName, di.Name, di.Attributes, excluded)) continue;
                    var id = Interlocked.Increment(ref nextFolderId);
                    batch.Folders.Add(new FolderRow(id, di.FullName, di.Name, folderId, Unix(di.LastWriteTimeUtc)));
                    subdirs.Push((di.FullName, id));
                }
                else if (e is FileInfo fi && indexFiles)
                {
                    var id = Interlocked.Increment(ref nextFileId);
                    batch.Files.Add(new FileRow(id, folderId, fi.Name, fi.Extension.TrimStart('.').ToLowerInvariant(),
                        fi.Length, Unix(fi.CreationTimeUtc), Unix(fi.LastWriteTimeUtc)));
                }
            }
        }
        catch { /* directory vanished or access revoked mid-enumeration */ }
    }

    private static volatile bool _filesOn = true;

    public static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    // ---- System ------------------------------------------------------------

    private void ScanSystem()
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        IndexDatabase.Exec(c, "DELETE FROM processes; DELETE FROM services;");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var p in Process.GetProcesses())
        {
            string? path = null; long started = 0;
            try { path = p.MainModule?.FileName; started = new DateTimeOffset(p.StartTime).ToUnixTimeSeconds(); } catch { }
            IndexDatabase.Exec(c, "INSERT INTO processes VALUES($pid,$n,$p,$s,$t)", ("$pid", p.Id), ("$n", p.ProcessName), ("$p", path), ("$s", started), ("$t", now));
            p.Dispose();
        }
        try
        {
            foreach (var s in System.ServiceProcess.ServiceController.GetServices())
            {
                IndexDatabase.Exec(c, "INSERT OR REPLACE INTO services VALUES($n,$d,$st,$t)",
                    ("$n", s.ServiceName), ("$d", s.DisplayName), ("$st", s.Status.ToString()), ("$t", s.StartType.ToString()));
                s.Dispose();
            }
        }
        catch { }
        tx.Commit();

        _db.SetMeta("machine", Environment.MachineName);
        _db.SetMeta("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        _db.SetMeta("cpu_count", Environment.ProcessorCount.ToString());
        _db.SetMeta("ram_bytes", GC.GetGCMemoryInfo().TotalAvailableMemoryBytes.ToString());
        _db.SetMeta("user", Environment.UserName);
    }
}
