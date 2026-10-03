namespace Astra.Core.Index;

/// <summary>Facade over the local computer index: database, scanner, search and the change watcher.</summary>
public sealed class IndexService : IDisposable
{
    public IndexDatabase Database { get; }
    public ComputerIndexer Indexer { get; }
    public SearchService Search { get; }
    public IndexWatcher Watcher { get; }

    public IndexService(Settings.SettingsStore store, string? dbPath = null)
    {
        Database = new IndexDatabase(dbPath);
        IReadOnlyCollection<string> Excluded() => store.Current.Index.ExcludedFolders;
        Indexer = new ComputerIndexer(Database, Excluded, () => store.Current.Index.IndexFileMetadata);
        Search = new SearchService(Database);
        Watcher = new IndexWatcher(Database, Indexer, Excluded);
    }

    public bool HasIndex => Database.Stats().Applications > 0;

    public async Task ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct = default)
    {
        Watcher.Stop();
        await Indexer.ScanAsync(progress, ct);
    }

    /// <summary>Removes everything the scanner stored. The next scan starts from scratch.</summary>
    public void Clear()
    {
        Watcher.Stop();
        using var c = Database.Open();
        IndexDatabase.Exec(c, "DELETE FROM applications_fts; DELETE FROM applications; DELETE FROM browser_profiles; DELETE FROM processes; DELETE FROM services; DELETE FROM drives; DELETE FROM metadata;");
        IndexDatabase.Exec(c, "DELETE FROM files_fts; DELETE FROM files; DELETE FROM folders_fts; DELETE FROM folders;");
        IndexDatabase.Exec(c, "VACUUM;");
    }

    public void Dispose() => Watcher.Dispose();
}
