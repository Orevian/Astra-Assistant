using Astra.Core.Localization;
using Astra.Core.SystemInfo;
using Microsoft.UI.Xaml.Controls;

namespace Astra.App.Pages;

public sealed partial class AutomationPage : Page
{
    public AutomationPage()
    {
        InitializeComponent();
        var a = App.Store.Current.Automation;

        var browsers = BrowserDetector.Detect();
        var items = new List<(string, string)> { (Loc.T("System default"), "") };
        items.AddRange(browsers.Select(b => (b.Name, b.Id)));
        if (a.PreferredBrowserId is { Length: > 0 } saved && items.All(i => i.Item2 != saved))
            items.Add(($"{saved} (not found)", saved));
        Bind.Choice(BrowserBox, items, () => a.PreferredBrowserId ?? "", v => a.PreferredBrowserId = v.Length == 0 ? null : v);
        BrowserCard.Description = browsers.Count == 0
            ? Loc.T("No browsers detected. Astra will use the Windows default.")
            : Loc.F("{0} browser(s) detected. Used when you don’t name one.", browsers.Count);

        Bind.Toggle(UiaToggle, () => a.PreferUiAutomation, v => a.PreferUiAutomation = v);
        Bind.Toggle(MouseToggle, () => a.AllowMouse, v => a.AllowMouse = v);
        Bind.Toggle(KeyboardToggle, () => a.AllowKeyboard, v => a.AllowKeyboard = v);
        Bind.Toggle(VerifyToggle, () => a.VerifyActions, v => a.VerifyActions = v);
        Bind.Range(DelaySlider, () => a.ActionDelayMs, v => a.ActionDelayMs = (int)v, DelayLabel, "{0:0} ms");
        Bind.Toggle(LocalToggle, () => a.LocalFirst, v => a.LocalFirst = v);
        Bind.Number(StepsBox, () => a.MaxAgentSteps, v => a.MaxAgentSteps = (int)v);
    }
}
