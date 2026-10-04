using Astra.App.Controls;
using Astra.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Astra.App;

/// <summary>Walks a UI tree and translates every user-visible string through <see cref="Loc"/>.</summary>
public static class Localizer
{
    public static void Apply(DependencyObject root)
    {
        Visit(root);
    }

    private static void Visit(DependencyObject node)
    {
        switch (node)
        {
            case SettingsCard c:
                c.Header = Loc.T(c.Header);
                c.Description = Loc.T(c.Description);
                break;
            case TextBlock t:
                t.Text = Loc.T(t.Text);
                break;
            case InfoBar i:
                i.Title = Loc.T(i.Title);
                i.Message = Loc.T(i.Message);
                break;
            case AutoSuggestBox a:
                a.PlaceholderText = Loc.T(a.PlaceholderText);
                break;
            case TextBox tb:
                tb.PlaceholderText = Loc.T(tb.PlaceholderText);
                break;
            case PasswordBox pb:
                pb.PlaceholderText = Loc.T(pb.PlaceholderText);
                break;
            case ComboBox cb:
                cb.PlaceholderText = Loc.T(cb.PlaceholderText);
                break;
        }

        if (node is ContentControl { Content: string s } cc)
            cc.Content = Loc.T(s);

        if (node is FrameworkElement fe && ToolTipService.GetToolTip(fe) is string tip)
            ToolTipService.SetToolTip(fe, Loc.T(tip));

        // Items live outside the visual tree until their popup opens, so translate them directly.
        if (node is ItemsControl ic)
            foreach (var item in ic.Items.OfType<DependencyObject>()) Visit(item);
        if (node is NavigationView nav)
        {
            foreach (var item in nav.MenuItems.OfType<DependencyObject>()) Visit(item);
            foreach (var item in nav.FooterMenuItems.OfType<DependencyObject>()) Visit(item);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var k = 0; k < count; k++) Visit(VisualTreeHelper.GetChild(node, k));
    }
}
