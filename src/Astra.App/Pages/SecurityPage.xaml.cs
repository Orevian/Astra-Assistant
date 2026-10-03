using Astra.App.Controls;
using Astra.Core.Localization;
using Astra.Core.Providers;
using Astra.Core.Security;
using Astra.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astra.App.Pages;

public sealed partial class SecurityPage : Page
{
    private static readonly (string Key, string Title, string Description, string Glyph)[] Categories =
    {
        ("applications", "Applications", "Open, close and list running programs.", ""),
        ("browser", "Browser", "Navigate, search and interact with web pages.", ""),
        ("files", "Files", "Create, move, copy and delete files and folders.", ""),
        ("input", "Mouse & keyboard", "Click, type and press keys on your behalf.", ""),
        ("terminal", "Terminal", "Run shell commands.", ""),
        ("system", "System settings", "Change Windows settings.", ""),
        ("power", "Shutdown / restart", "Turn off, restart, sleep or sign out.", ""),
    };

    public SecurityPage()
    {
        InitializeComponent();
        var sec = App.Store.Current.Security;

        var levels = new[] { ("Ask every time", PermissionLevel.Ask), ("Always allow", PermissionLevel.Allow), ("Never allow", PermissionLevel.Deny) };
        foreach (var (key, title, desc, glyph) in Categories)
        {
            var combo = new ComboBox { Width = 180 };
            Bind.Choice(combo, levels,
                () => sec.Permissions.TryGetValue(key, out var l) ? l : PermissionLevel.Ask,
                v => sec.Permissions[key] = v);
            PermissionList.Children.Add(new SettingsCard { Header = title, Description = desc, Glyph = glyph, CardContent = combo });
        }

        Bind.Toggle(DestructiveToggle, () => sec.AlwaysConfirmDestructive, v => sec.AlwaysConfirmDestructive = v);
        Bind.Number(BulkBox, () => sec.ConfirmDeleteOverFiles, v => sec.ConfirmDeleteOverFiles = (int)v);
        RefreshCredentials();
    }

    private void RefreshCredentials()
    {
        CredentialList.Children.Clear();
        var any = false;
        foreach (var p in ProviderCatalog.All.Where(p => p.RequiresApiKey))
        {
            var name = CredentialStore.ProviderKeyName(p.Id);
            if (!CredentialStore.Exists(name)) continue;
            any = true;
            var remove = new Button { Content = "Remove" };
            remove.Click += async (_, _) =>
            {
                var dlg = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = Loc.F("Remove {0} key?", Loc.T(p.DisplayName)),
                    Content = Loc.T("The key is deleted from Windows Credential Manager. You can add it again later."),
                    PrimaryButtonText = Loc.T("Remove"),
                    CloseButtonText = Loc.T("Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
                CredentialStore.Delete(name);
                RefreshCredentials();
            };
            CredentialList.Children.Add(new SettingsCard
            {
                Header = p.DisplayName,
                Description = "API key saved in Windows Credential Manager",
                Glyph = "",
                CardContent = remove,
            });
        }
        if (!any)
            CredentialList.Children.Add(new TextBlock
            {
                Text = "No API keys stored.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(2, 4, 0, 0),
            });
    }
}
