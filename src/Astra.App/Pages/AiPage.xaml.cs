using Astra.Core.Localization;
using Astra.Core.Providers;
using Astra.Core.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astra.App.Pages;

public sealed partial class AiPage : Page
{
    private readonly Core.Settings.AiSettings _ai = App.Store.Current.Ai;
    private IAiProvider _provider;
    private bool _loading = true;
    private CancellationTokenSource? _cts;

    public AiPage()
    {
        InitializeComponent();
        _provider = ProviderCatalog.Get(_ai.ProviderId);

        foreach (var p in ProviderCatalog.All) ProviderBox.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.Id });
        ProviderBox.SelectedIndex = ProviderCatalog.All.ToList().FindIndex(p => p.Id == _provider.Id);

        VisionBox.Items.Add(new ComboBoxItem { Content = "Same as chat model", Tag = "same" });
        foreach (var p in ProviderCatalog.All.Where(p => p.Id != "custom"))
            VisionBox.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.Id });
        VisionBox.SelectedIndex = Math.Max(0, VisionBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _ai.VisionProviderId));

        ProviderBox.SelectionChanged += (_, _) =>
        {
            if (ProviderBox.SelectedItem is not ComboBoxItem { Tag: string id } || _loading) return;
            _ai.ProviderId = id;
            _ai.Model = "";
            _ai.CustomEndpoint = null;
            _provider = ProviderCatalog.Get(id);
            ResultBar.IsOpen = false;
            RefreshProviderUi();
            App.Store.Commit();
            if (id == "ollama") _ = TestAsync(silentFailure: false);
        };
        VisionBox.SelectionChanged += (_, _) =>
        {
            if (_loading || VisionBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            _ai.VisionProviderId = id;
            VisionModelCard.Visibility = id == "same" ? Visibility.Collapsed : Visibility.Visible;
            App.Store.Commit();
        };
        ModelBox.SelectionChanged += (_, _) =>
        {
            if (_loading || ModelBox.SelectedItem is not string m) return;
            _ai.Model = m;
            App.Store.Commit();
        };
        ModelBox.TextSubmitted += (_, e) =>
        {
            _ai.Model = e.Text.Trim();
            App.Store.Commit();
        };
        EndpointBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _ai.CustomEndpoint = string.IsNullOrWhiteSpace(EndpointBox.Text) ? null : EndpointBox.Text.Trim();
            App.Store.Commit();
        };

        VisionModelBox.Text = _ai.VisionModel;
        VisionModelBox.TextChanged += (_, _) => { if (!_loading) { _ai.VisionModel = VisionModelBox.Text.Trim(); App.Store.Commit(); } };
        VisionModelCard.Visibility = _ai.VisionProviderId == "same" ? Visibility.Collapsed : Visibility.Visible;

        RefreshProviderUi();
        _loading = false;
        if (_provider.Id == "ollama") _ = TestAsync(silentFailure: false);
    }

    private string KeyName => CredentialStore.ProviderKeyName(_provider.Id);

    private void RefreshProviderUi()
    {
        var wasLoading = _loading;
        _loading = true;
        KeyCard.Visibility = _provider.RequiresApiKey ? Visibility.Visible : Visibility.Collapsed;
        EndpointCard.Visibility = _provider.SupportsCustomEndpoint ? Visibility.Visible : Visibility.Collapsed;
        EndpointBox.PlaceholderText = _provider.DefaultEndpoint;
        EndpointBox.Text = _ai.CustomEndpoint ?? "";
        KeyBox.Password = "";
        UpdateKeyState();

        ModelBox.Items.Clear();
        foreach (var m in _provider.SuggestedModels) ModelBox.Items.Add(m);
        ModelBox.Text = _ai.Model;
        _loading = wasLoading;
    }

    private void UpdateKeyState()
    {
        var has = CredentialStore.Exists(KeyName);
        KeyBox.PlaceholderText = Loc.T(has ? "Key saved — paste a new one to replace" : "Paste your API key");
        RemoveKeyBtn.IsEnabled = has;
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) return;
        try
        {
            CredentialStore.Save(KeyName, key);
            KeyBox.Password = "";
            UpdateKeyState();
            Show(InfoBarSeverity.Success, Loc.T("API key saved to Windows Credential Manager."));
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, Loc.F("Could not store the key: {0}", ex.Message));
        }
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        CredentialStore.Delete(KeyName);
        UpdateKeyState();
        Show(InfoBarSeverity.Informational, Loc.T("API key removed."));
    }

    private async void Test_Click(object sender, RoutedEventArgs e) => await TestAsync(silentFailure: false);

    private async Task TestAsync(bool silentFailure)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var provider = _provider;
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) key = CredentialStore.Load(CredentialStore.ProviderKeyName(provider.Id)) ?? "";

        Ring.IsActive = true;
        TestBtn.IsEnabled = false;
        try
        {
            var r = await provider.TestAsync(key, _ai.CustomEndpoint, cts.Token);
            if (cts.IsCancellationRequested || provider != _provider) return;
            if (r.Success)
            {
                var current = ModelBox.Text;
                _loading = true;
                ModelBox.Items.Clear();
                foreach (var m in r.Models.Count > 0 ? r.Models : provider.SuggestedModels) ModelBox.Items.Add(m);
                ModelBox.Text = current;
                _loading = false;
                if (string.IsNullOrWhiteSpace(_ai.Model) && r.Models.Count > 0)
                {
                    _ai.Model = r.Models[0];
                    ModelBox.Text = _ai.Model;
                    App.Store.Commit();
                }
                Show(InfoBarSeverity.Success, r.Message);
            }
            else if (!silentFailure)
            {
                Show(InfoBarSeverity.Error, r.Message);
            }
        }
        finally
        {
            if (cts == _cts)
            {
                Ring.IsActive = false;
                TestBtn.IsEnabled = true;
            }
        }
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = severity switch
        {
            InfoBarSeverity.Success => Loc.T("Success"),
            InfoBarSeverity.Error => Loc.T("Connection problem"),
            _ => Loc.T("Info"),
        };
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) => _cts?.Cancel();
}
