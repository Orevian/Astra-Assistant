using Astra.App.Controls;
using Astra.Core.Index;
using Astra.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Astra.App.Pages;

public sealed partial class IndexPage : Page
{
    private readonly Core.Settings.IndexSettings _i = App.Store.Current.Index;
    private readonly ScanController _scan = ScanController.Instance;
    private static readonly (ScanPhase Phase, string Label)[] Rows =
    {
        (ScanPhase.Applications, "Applications"), (ScanPhase.Drives, "Drives"), (ScanPhase.Folders, "Folders"),
        (ScanPhase.Files, "Files"), (ScanPhase.System, "System"),
    };
    private readonly Dictionary<ScanPhase, (TextBlock Value, ProgressBar Bar)> _rowViews = new();

    public IndexPage()
    {
        InitializeComponent();
        Bind.Toggle(WatchToggle, () => _i.WatchChanges, v =>
        {
            _i.WatchChanges = v;
            var watcher = App.Assistant.Runtime.Index.Watcher;
            if (v && _i.Completed && !_scan.IsScanning) watcher.Start(); else watcher.Stop();
        });
        Bind.Toggle(MetaToggle, () => _i.IndexFileMetadata, v => _i.IndexFileMetadata = v);

        BuildRows();
        RefreshExcluded();
        UpdateStatus();
        Loaded += (_, _) => _scan.Changed += OnScanChanged;
        Unloaded += (_, _) => _scan.Changed -= OnScanChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // The first-run wizard hosts this page with a welcome card and without the maintenance sections.
        if (e.Parameter as string == "wizard")
        {
            WelcomeCard.Visibility = Visibility.Visible;
            PageTitle.Visibility = PageSubtitle.Visibility = Visibility.Collapsed;
            DataGroup.Visibility = ClearCard.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildRows()
    {
        foreach (var (phase, label) in Rows)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var bar = new ProgressBar { Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2);
            grid.Children.Add(name); grid.Children.Add(bar); grid.Children.Add(value);
            PhaseRows.Children.Add(grid);
            _rowViews[phase] = (value, bar);
        }
    }

    private void OnScanChanged() => DispatcherQueue.TryEnqueue(UpdateStatus);

    private void UpdateStatus()
    {
        var scanning = _scan.IsScanning;
        ScanBtn.IsEnabled = !scanning;
        CancelBtn.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanBtn.Content = _i.Completed ? Loc.T("Rescan Windows") : Loc.T("Scan Windows");
        ProgressCard.Visibility = scanning || _scan.Phases.Count > 0 && !_i.Completed ? Visibility.Visible : Visibility.Collapsed;
        ScanHeader.Text = Loc.T("Scanning Windows…");

        foreach (var (phase, (value, bar)) in _rowViews)
        {
            if (_scan.Phases.TryGetValue(phase, out var p))
            {
                bar.Value = p.Done ? 1 : p.Fraction;
                value.Text = p.Done ? "✓" : $"{p.Fraction:P0}";
            }
            else { bar.Value = 0; value.Text = ""; }
        }
        var building = _scan.Phases.TryGetValue(ScanPhase.BuildingIndex, out _) && scanning;
        BuildingText.Visibility = scanning && _scan.Phases.ContainsKey(ScanPhase.System) && _scan.Phases[ScanPhase.System].Done ? Visibility.Visible : Visibility.Collapsed;

        if (scanning && _scan.Last is { } last)
            StatusCard.Description = Loc.F("{0} apps · {1} folders · {2} files found so far", last.Applications, last.Folders.ToString("N0"), last.Files.ToString("N0"));
        else
        {
            var s = App.Assistant.Runtime.Index.Database.Stats();
            StatusCard.Description = s.Applications == 0 && !_i.Completed
                ? Loc.T("Not scanned yet. Astra can’t find apps and files locally until it is.")
                : Loc.F("{0} apps · {1} folders · {2} files{3}", s.Applications, s.Folders.ToString("N0"), s.Files.ToString("N0"),
                    DateTime.TryParse(s.LastScan, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ? " · " + Loc.F("scanned {0}", when.ToLocalTime().ToString("g")) : "");
        }
        _ = building;
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => _ = _scan.StartAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scan.Cancel();

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = Loc.T("Delete the index?"),
            Content = Loc.T("Astra will not be able to find apps and files until you scan again."),
            PrimaryButtonText = Loc.T("Delete"), CloseButtonText = Loc.T("Cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        await Task.Run(() => App.Assistant.Runtime.Index.Clear());
        _i.Completed = false;
        App.Store.Commit();
        _scan.Phases.ToList(); // keep state object; clear visible rows below
        UpdateStatus();
    }

    private void RefreshExcluded()
    {
        ExcludedList.Children.Clear();
        foreach (var folder in _i.ExcludedFolders.ToList())
        {
            var remove = new Button { Content = Loc.T("Remove") };
            remove.Click += (_, _) =>
            {
                _i.ExcludedFolders.Remove(folder);
                App.Store.Commit();
                RefreshExcluded();
            };
            ExcludedList.Children.Add(new SettingsCard { Header = System.IO.Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } n ? n : folder, Description = folder, Glyph = "", CardContent = remove });
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || _i.ExcludedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase)) return;
        _i.ExcludedFolders.Add(folder.Path);
        App.Store.Commit();
        RefreshExcluded();
    }
}
