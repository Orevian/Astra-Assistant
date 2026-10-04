using Astra.App.Controls;
using Astra.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Astra.App.Pages;

public sealed partial class MemoryPage : Page
{
    private readonly Core.Memory.MemoryStore _memory = App.Assistant.Runtime.Memory;

    public MemoryPage()
    {
        InitializeComponent();
        var m = App.Store.Current.Memory;
        Bind.Toggle(EnabledToggle, () => m.Enabled, v => m.Enabled = v);
        Bind.Range(TurnsSlider, () => m.ShortTermTurns, v => m.ShortTermTurns = (int)v, TurnsLabel, "{0:0} turns");
        NewBox.PlaceholderText = Loc.T("e.g. My preferred browser is Avast Secure Browser");
        Refresh();
    }

    private void Refresh()
    {
        MemoryList.Children.Clear();
        var all = _memory.All();
        foreach (var entry in all)
        {
            var del = new Button { Content = Loc.T("Delete") };
            del.Click += (_, _) => { _memory.Delete(entry.Id); Refresh(); };
            MemoryList.Children.Add(new SettingsCard { Header = entry.Text, Description = entry.Created.ToString("g"), Glyph = "", CardContent = del });
        }
        if (all.Count == 0)
            MemoryList.Children.Add(new TextBlock
            {
                Text = Loc.T("Nothing saved yet."), Margin = new Thickness(2, 6, 0, 0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        ClearCard.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { AddMemory(); e.Handled = true; }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddMemory();

    private void AddMemory()
    {
        var text = NewBox.Text.Trim();
        if (text.Length == 0) return;
        var (ok, message) = _memory.Add(text);
        AddBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        AddBar.Title = ok ? Loc.T("Saved") : Loc.T("Not saved");
        AddBar.Message = Loc.T(message);
        AddBar.IsOpen = true;
        if (ok) NewBox.Text = "";
        Refresh();
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = Loc.T("Forget everything?"), Content = Loc.T("Every saved memory will be deleted."),
            PrimaryButtonText = Loc.T("Delete all"), CloseButtonText = Loc.T("Cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        _memory.Clear();
        Refresh();
    }
}
