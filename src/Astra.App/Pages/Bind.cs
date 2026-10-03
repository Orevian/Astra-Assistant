using Astra.Core.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Astra.App.Pages;

/// <summary>Two-way bindings between controls and <see cref="Astra.Core.Settings.AstraSettings"/>.
/// The initial value is applied before the handler is attached, so loading never writes back.</summary>
public static class Bind
{
    private static void Save() => App.Store.Commit();

    public static void Toggle(ToggleSwitch t, Func<bool> get, Action<bool> set)
    {
        t.IsOn = get();
        t.Toggled += (_, _) => { set(t.IsOn); Save(); };
    }

    public static void Range(Slider s, Func<double> get, Action<double> set, TextBlock? label = null, string format = "{0:0}")
    {
        s.Value = get();
        if (label is not null) label.Text = string.Format(Loc.T(format), s.Value);
        s.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            if (label is not null) label.Text = string.Format(Loc.T(format), e.NewValue);
            Save();
        };
    }

    public static void Number(NumberBox n, Func<double> get, Action<double> set)
    {
        n.Value = get();
        n.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) { n.Value = get(); return; }
            set(e.NewValue);
            Save();
        };
    }

    public static void Text(TextBox t, Func<string> get, Action<string> set)
    {
        t.Text = get();
        t.TextChanged += (_, _) => { set(t.Text); Save(); };
    }

    public static void Choice<T>(ComboBox c, IReadOnlyList<(string Label, T Value)> items, Func<T> get, Action<T> set)
        where T : notnull
    {
        c.Items.Clear();
        foreach (var (label, value) in items) c.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        var current = get();
        var idx = -1;
        for (var i = 0; i < items.Count; i++)
            if (EqualityComparer<T>.Default.Equals(items[i].Value, current)) idx = i;
        c.SelectedIndex = idx;
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is ComboBoxItem { Tag: T v }) { set(v); Save(); }
        };
    }

    public static IReadOnlyList<(string, T)> EnumItems<T>(Func<T, string>? label = null) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => (label?.Invoke(v) ?? Humanize(v.ToString()), v)).ToList();

    public static string Humanize(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in pascal)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(ch));
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
