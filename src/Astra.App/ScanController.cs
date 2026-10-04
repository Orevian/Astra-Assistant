using Astra.Core.Index;
using Astra.Core.Localization;

namespace Astra.App;

/// <summary>
/// Runs the Windows scan independently of any page, so progress survives navigating away
/// and the first-run wizard, Settings and the tray all see the same scan.
/// </summary>
public sealed class ScanController
{
    public static ScanController Instance { get; } = new();

    private CancellationTokenSource? _cts;
    private readonly Dictionary<ScanPhase, (double Fraction, bool Done)> _phases = new();

    public bool IsScanning { get; private set; }
    public ScanProgress? Last { get; private set; }
    public IReadOnlyDictionary<ScanPhase, (double Fraction, bool Done)> Phases => _phases;

    public event Action? Changed;

    public async Task StartAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        _phases.Clear();
        Last = null;
        _cts = new CancellationTokenSource();
        Changed?.Invoke();

        var index = App.Assistant.Runtime.Index;
        var progress = new Progress<ScanProgress>(p =>
        {
            lock (_phases) _phases[p.Phase] = (p.Fraction, p.PhaseDone);
            Last = p;
            Changed?.Invoke();
        });
        try
        {
            await index.ScanAsync(progress, _cts.Token);
            App.Store.Current.Index.Completed = true;
            App.Store.Commit();
            if (App.Store.Current.Index.WatchChanges) index.Watcher.Start();
            var stats = index.Database.Stats();
            App.Assistant.Announce(Loc.T("Windows scan complete"),
                Loc.F("{0} apps, {1} folders and {2} files are ready to search.", stats.Applications, stats.Folders.ToString("N0"), stats.Files.ToString("N0")));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Assistant.Announce(Loc.T("Windows scan failed"), ex.Message, true);
        }
        finally
        {
            IsScanning = false;
            Changed?.Invoke();
        }
    }

    public void Cancel() => _cts?.Cancel();
}
