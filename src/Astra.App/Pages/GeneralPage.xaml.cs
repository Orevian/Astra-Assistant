using Astra.Core.Localization;
using Astra.Core.Settings;
using Astra.Core.SystemInfo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astra.App.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        var g = App.Store.Current.General;

        Bind.Text(NameBox, () => g.AssistantName, v => g.AssistantName = v);
        Bind.Choice(LangBox, new[] { ("Match Windows", "auto"), ("English", "en"), ("Türkçe", "tr") },
            () => g.UiLanguage, v => { g.UiLanguage = v; Loc.Set(v); });
        Bind.Toggle(MinimizedToggle, () => g.StartMinimized, v => g.StartMinimized = v);
        Bind.Toggle(TrayToggle, () => g.MinimizeToTray, v => g.MinimizeToTray = v);

        // The registry is the source of truth for autostart.
        StartupToggle.IsOn = StartupRegistration.IsEnabled();
        StartupToggle.Toggled += (_, _) =>
        {
            try
            {
                StartupRegistration.Set(StartupToggle.IsOn);
                g.StartWithWindows = StartupToggle.IsOn;
                App.Store.Commit();
            }
            catch
            {
                StartupToggle.IsOn = StartupRegistration.IsEnabled();
            }
        };

        DataCard.Description = SettingsStore.DataDirectory;
        var v = typeof(App).Assembly.GetName().Version;
        AboutCard.Description = Loc.F("AI desktop agent for Windows · version {0} · .NET {1} · WinUI 3", v?.ToString(3) ?? "0.1.0", Environment.Version.Major);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        System.Diagnostics.Process.Start("explorer.exe", SettingsStore.DataDirectory);
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Reset all settings?"),
            Content = Loc.T("Every option returns to its default value. Your saved API keys are not removed."),
            PrimaryButtonText = Loc.T("Reset"),
            CloseButtonText = Loc.T("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        App.Store.Reset();
        Frame.Navigate(typeof(GeneralPage));
    }
}
