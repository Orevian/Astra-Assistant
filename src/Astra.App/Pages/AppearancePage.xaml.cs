using Astra.Core.Localization;
using Astra.Core.Native;
using Astra.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Astra.App.Pages;

public sealed partial class AppearancePage : Page
{
    private readonly AppearanceSettings _a = App.Store.Current.Appearance;
    private bool _loading = true;

    private sealed record ThemeDef(ThemePreset Preset, string Accent, string From, string To);

    private static readonly ThemeDef[] Themes =
    {
        new(ThemePreset.Minimal, "#6B7280", "#F3F4F6", "#D1D5DB"),
        new(ThemePreset.Glass, "#7C5CFF", "#8EA2FF", "#C9A7FF"),
        new(ThemePreset.Cyber, "#00E5FF", "#02141F", "#00A6C4"),
        new(ThemePreset.Neon, "#FF2BD6", "#2A0A3A", "#FF2BD6"),
        new(ThemePreset.Classic, "#0067C0", "#E8F1FB", "#7FB2E5"),
        new(ThemePreset.Terminal, "#22C55E", "#050A06", "#0F3D1C"),
        new(ThemePreset.Custom, "", "#444444", "#999999"),
    };

    private static (string Id, string Name)[] Icons => (Overlays.IconFactory.HasBundledIcon
        ? new[] { ("app", "Astra icon") } : Array.Empty<(string, string)>())
        .Concat(new[] { ("orb", "Orb"), ("star", "Star"), ("ring", "Ring"), ("minimal", "Minimal"), ("abstract", "Abstract AI") }).ToArray();

    public AppearancePage()
    {
        InitializeComponent();

        BuildThemeGrid();
        BuildIconGrid();

        Bind.Choice(ModeBox, new[] { ("Match Windows", "system"), ("Light", "light"), ("Dark", "dark") },
            () => _a.AppTheme, v => _a.AppTheme = v);

        AccentSync();
        Picker.ColorChanged += (_, e) =>
        {
            if (_loading) return;
            _a.AccentColor = App.ToHex(e.NewColor);
            if (_a.Theme != ThemePreset.Custom) { _a.Theme = ThemePreset.Custom; SelectTheme(); }
            AccentSync();
            App.Store.Commit();
            UpdatePreview();
        };

        var displays = new List<(string, string)> { (Loc.T("Primary display (default)"), "") };
        displays.AddRange(Displays.All().Select(d => (d.Label, d.DeviceName)));
        Bind.Choice(HudDisplayBox, displays, () => Known(_a.HudDisplay, displays), v => _a.HudDisplay = v);
        Bind.Choice(NotifDisplayBox, displays, () => Known(_a.NotificationDisplay, displays), v => _a.NotificationDisplay = v);

        Bind.Toggle(HudToggle, () => _a.HudEnabled, v => { _a.HudEnabled = v; UpdateEnabledStates(); });
        Bind.Choice(HudPosBox, Corners(), () => _a.HudPosition, v => { _a.HudPosition = v; UpdatePreview(); });
        Bind.Range(HudOpacity, () => _a.HudOpacity, v => { _a.HudOpacity = (int)v; UpdatePreview(); }, HudOpacityLabel, "{0:0}%");

        Bind.Toggle(CursorToggle, () => _a.AiCursorEnabled, v => { _a.AiCursorEnabled = v; UpdateEnabledStates(); });
        Bind.Range(CursorSize, () => _a.CursorSize, v => _a.CursorSize = (int)v, CursorSizeLabel, "{0:0}px");

        Bind.Toggle(BubbleToggle, () => _a.SpeechBubbleEnabled, v => { _a.SpeechBubbleEnabled = v; UpdateEnabledStates(); });
        Bind.Choice(BubblePosBox, Corners(), () => _a.BubblePosition, v => _a.BubblePosition = v);

        Bind.Toggle(VizToggle, () => _a.VisualizerEnabled, v => { _a.VisualizerEnabled = v; UpdateEnabledStates(); });
        Bind.Choice(VizStyleBox, Bind.EnumItems<VisualizerStyle>(), () => _a.Visualizer, v => _a.Visualizer = v);
        Bind.Choice(VizPosBox, Corners(), () => _a.VisualizerPosition, v => _a.VisualizerPosition = v);
        Bind.Range(VizSize, () => _a.VisualizerSize, v => _a.VisualizerSize = (int)v, VizSizeLabel, "{0:0}%");
        Bind.Range(VizOpacity, () => _a.VisualizerOpacity, v => _a.VisualizerOpacity = (int)v, VizOpacityLabel, "{0:0}%");

        Bind.Toggle(NotifToggle, () => _a.NotificationsEnabled, v => { _a.NotificationsEnabled = v; UpdateEnabledStates(); });
        Bind.Choice(NotifPosBox, Corners(), () => _a.NotificationPosition, v => _a.NotificationPosition = v);
        Bind.Toggle(AnimToggle, () => _a.AnimationsEnabled, v => _a.AnimationsEnabled = v);

        _loading = false;
        UpdateEnabledStates();
        UpdatePreview();
    }

    /// <summary>A saved monitor that is no longer connected falls back to the primary entry instead of showing a blank box.</summary>
    private static string Known(string saved, List<(string Label, string Value)> displays) =>
        displays.FirstOrDefault(d => d.Value.Equals(saved, StringComparison.OrdinalIgnoreCase)).Value ?? "";

    private void Preview_Click(object sender, RoutedEventArgs e) => App.Overlays.Preview();

    private static IReadOnlyList<(string, ScreenCorner)> Corners() => Bind.EnumItems<ScreenCorner>();

    // ---- Theme gallery ---------------------------------------------------

    private void BuildThemeGrid()
    {
        foreach (var t in Themes)
        {
            var swatch = new Border
            {
                Width = 132, Height = 70, CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush
                {
                    StartPoint = new(0, 0), EndPoint = new(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Hex(t.From), Offset = 0 },
                        new GradientStop { Color = Hex(t.To), Offset = 1 },
                    },
                },
            };
            if (t.Preset == ThemePreset.Custom)
                swatch.Child = new FontIcon { Glyph = "", Foreground = new SolidColorBrush(Colors.White) };
            var tile = new StackPanel { Spacing = 6, Padding = new Thickness(6), Tag = t };
            tile.Children.Add(swatch);
            tile.Children.Add(new TextBlock { Text = t.Preset.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
            ThemeGrid.Items.Add(tile);
        }
        SelectTheme();
        ThemeGrid.SelectionChanged += (_, _) =>
        {
            if (_loading || ThemeGrid.SelectedItem is not StackPanel { Tag: ThemeDef t }) return;
            _a.Theme = t.Preset;
            if (t.Accent.Length > 0)
            {
                _a.AccentColor = t.Accent;
                AccentSync();
            }
            App.Store.Commit();
            UpdatePreview();
        };
    }

    private void SelectTheme()
    {
        var was = _loading;
        _loading = true;
        ThemeGrid.SelectedIndex = Array.FindIndex(Themes, t => t.Preset == _a.Theme);
        _loading = was;
    }

    private void AccentSync()
    {
        var was = _loading;
        _loading = true;
        if (App.TryParseColor(_a.AccentColor, out var c))
        {
            Picker.Color = c;
            AccentBtn.Background = new SolidColorBrush(c);
        }
        AccentLabel.Text = _a.AccentColor.ToUpperInvariant();
        _loading = was;
    }

    // ---- Icon picker ------------------------------------------------------

    private void BuildIconGrid()
    {
        foreach (var (id, name) in Icons)
        {
            var tile = new StackPanel { Spacing = 6, Padding = new Thickness(10, 8, 10, 6), Tag = id };
            var holder = new Grid { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
            holder.Children.Add(BuiltInIcon(id, 40));
            tile.Children.Add(holder);
            tile.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            IconGrid.Items.Add(tile);
        }
        SelectIcon();
        IconGrid.SelectionChanged += (_, _) =>
        {
            if (_loading || IconGrid.SelectedItem is not StackPanel { Tag: string id }) return;
            _a.IconId = id;
            _a.CustomIconPath = null;
            UpdatePreview();
            App.Store.Commit();
        };
    }

    private void SelectIcon()
    {
        var was = _loading;
        _loading = true;
        IconGrid.SelectedIndex = _a.CustomIconPath is null ? Array.FindIndex(Icons, i => i.Id == _a.IconId) : -1;
        ClearIconBtn.IsEnabled = _a.CustomIconPath is not null;
        _loading = was;
    }

    private UIElement BuiltInIcon(string id, double size)
    {
        var accent = App.TryParseColor(_a.AccentColor, out var col) ? col : Colors.MediumPurple;
        return id == "app" && Overlays.IconFactory.HasBundledIcon
            ? Overlays.IconFactory.Create(new AppearanceSettings { IconId = "app" }, size, accent)
            : Overlays.IconFactory.BuiltIn(id == "app" ? "orb" : id, size, accent);
    }

    private async void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".svg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var dest = System.IO.Path.Combine(SettingsStore.DataDirectory, "custom-icon" + file.FileType.ToLowerInvariant());
        File.Copy(file.Path, dest, overwrite: true);
        _a.CustomIconPath = dest;
        SelectIcon();
        UpdatePreview();
        App.Store.Commit();
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e)
    {
        _a.CustomIconPath = null;
        SelectIcon();
        UpdatePreview();
        App.Store.Commit();
    }

    // ---- Preview & enablement --------------------------------------------

    private void UpdatePreview()
    {
        if (_loading && PreviewIcon is null) return;
        PreviewIcon.Children.Clear();
        if (_a.CustomIconPath is { } path && File.Exists(path))
        {
            PreviewIcon.Children.Add(path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new Image { Source = new SvgImageSource(new Uri(path)) }
                : new Image { Source = new BitmapImage(new Uri(path)) });
        }
        else
        {
            PreviewIcon.Children.Add(BuiltInIcon(_a.IconId, 20));
        }

        HudPreview.Visibility = _a.HudEnabled ? Visibility.Visible : Visibility.Collapsed;
        HudPreview.Opacity = _a.HudOpacity / 100.0;
        HudPreview.HorizontalAlignment = _a.HudPosition is ScreenCorner.TopLeft or ScreenCorner.BottomLeft ? HorizontalAlignment.Left
            : _a.HudPosition == ScreenCorner.Center ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        HudPreview.VerticalAlignment = _a.HudPosition is ScreenCorner.TopLeft or ScreenCorner.TopRight ? VerticalAlignment.Top
            : _a.HudPosition == ScreenCorner.Center ? VerticalAlignment.Center : VerticalAlignment.Bottom;
    }

    private void UpdateEnabledStates()
    {
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        HudDisplayCard.Visibility = HudPosCard.Visibility = HudOpacityCard.Visibility = V(_a.HudEnabled);
        CursorSizeCard.Visibility = V(_a.AiCursorEnabled);
        BubblePosCard.Visibility = V(_a.SpeechBubbleEnabled);
        VizStyleCard.Visibility = VizPosCard.Visibility = VizSizeCard.Visibility = VizOpacityCard.Visibility = V(_a.VisualizerEnabled);
        NotifDisplayCard.Visibility = NotifPosCard.Visibility = V(_a.NotificationsEnabled);
        UpdatePreview();
    }

    private static Windows.UI.Color Hex(string hex)
    {
        App.TryParseColor(hex, out var c);
        return c;
    }
}
