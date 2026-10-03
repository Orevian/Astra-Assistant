using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Astra.App.Controls;

/// <summary>Fluent settings row: icon, title, description and one trailing control.</summary>
[ContentProperty(Name = nameof(CardContent))]
public sealed partial class SettingsCard : UserControl
{
    public SettingsCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard),
        new PropertyMetadata("", (d, e) => ((SettingsCard)d).HeaderEl.Text = (string)e.NewValue));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard),
        new PropertyMetadata("", (d, e) =>
        {
            var c = (SettingsCard)d;
            var s = (string)e.NewValue;
            c.DescEl.Text = s;
            c.DescEl.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard),
        new PropertyMetadata("", (d, e) =>
        {
            var c = (SettingsCard)d;
            var s = (string)e.NewValue;
            c.IconEl.Glyph = s;
            c.IconEl.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public static readonly DependencyProperty CardContentProperty = DependencyProperty.Register(
        nameof(CardContent), typeof(object), typeof(SettingsCard),
        new PropertyMetadata(null, (d, e) => ((SettingsCard)d).ContentEl.Content = e.NewValue));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public object? CardContent { get => GetValue(CardContentProperty); set => SetValue(CardContentProperty, value); }
}
