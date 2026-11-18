using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NotchWin.Core;
using NotchWin.Services;

namespace NotchWin.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LauncherService _launcher;
    private readonly Action _onChanged;
    private readonly Action _onQuit;
    private readonly Action? _onTestToast;
    private bool _hydrating;

    public SettingsWindow(AppSettings settings, LauncherService launcher, Action onChanged, Action onQuit, Action? onTestToast = null)
    {
        // Assign fields & set _hydrating = true BEFORE InitializeComponent() so
        // Slider.Minimum coercion during XAML parsing never dereferences null!
        _hydrating = true;
        _settings = settings;
        _launcher = launcher;
        _onChanged = onChanged;
        _onQuit = onQuit;
        _onTestToast = onTestToast;

        InitializeComponent();

        SideAppearanceIco.Data = Icons.Palette;
        SideRestingIco.Data = Icons.Timer;
        SideBehaviorIco.Data = Icons.Sliders;
        SideShortcutsIco.Data = Icons.Grid;
        SideSystemIco.Data = Icons.Settings;

        HydrateControls();
        SelectPage("Appearance");
        UpdateRamBadge();
    }

    private void UpdateRamBadge()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            double mb = p.WorkingSet64 / (1024.0 * 1024.0);
            RamBadgeLbl.Text = $"RAM: {mb:0.0} MB";
        }
        catch { }
    }

    private void HydrateControls()
    {
        _hydrating = true;

        RadiusSlider.Value = _settings.ExpandedCornerRadius;
        RadiusValLbl.Text = $"{(int)_settings.ExpandedCornerRadius} px";

        MarginSlider.Value = _settings.IslandTopMargin;
        MarginValLbl.Text = $"{(int)_settings.IslandTopMargin} px (Floating Island mode)";

        HoverDelaySlider.Value = _settings.HoverDelayMs;
        HoverDelayLbl.Text = _settings.HoverDelayMs == 0
            ? "0 ms (instant on touch)"
            : $"{_settings.HoverDelayMs} ms (prevents accidental tab-switch opens)";

        ChkNotchOutline.IsChecked = _settings.ShowNotchOutline;
        ChkAlbumGlow.IsChecked = _settings.ShowAlbumGlow;
        ChkPrivacyDots.IsChecked = _settings.ShowPrivacyDots;
        ChkSlim.IsChecked = _settings.SlimOnFullscreenOrMaximized;
        ChkGameStrip.IsChecked = _settings.GameStripOnFullscreen;
        ChkVolumeHud.IsChecked = _settings.ShowVolumeHud;
        ChkDeviceHuds.IsChecked = _settings.ShowDeviceHuds;
        ChkLockKeyHuds.IsChecked = _settings.ShowLockKeyHuds;
        ChkTrackChangeHud.IsChecked = _settings.ShowTrackChangeHud;
        ChkClipboardHud.IsChecked = _settings.ShowClipboardHud;
        ChkStartup.IsChecked = _settings.LaunchOnStartup;

        HighlightShapeCards();
        HighlightThemeSwatches();
        HighlightRestingFaces();
        RebuildMonitors();
        RebuildAppsList();

        _hydrating = false;
    }

    private void SelectPage(string tag)
    {
        PageAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageResting.Visibility = tag == "Resting" ? Visibility.Visible : Visibility.Collapsed;
        PageBehavior.Visibility = tag == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        PageShortcuts.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;

        PageTitleLbl.Text = tag switch
        {
            "Appearance" => "Appearance",
            "Resting" => "Resting Face",
            "Behavior" => "Behavior & Display",
            "Shortcuts" => "Shortcuts & Apps",
            "System" => "System",
            _ => "Settings"
        };

        StyleSideBtn(NavAppearance, tag == "Appearance");
        StyleSideBtn(NavResting, tag == "Resting");
        StyleSideBtn(NavBehavior, tag == "Behavior");
        StyleSideBtn(NavShortcuts, tag == "Shortcuts");
        StyleSideBtn(NavSystem, tag == "System");
        UpdateRamBadge();
    }

    private static void StyleSideBtn(Button btn, bool active)
    {
        btn.Background = active ? new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        btn.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF));
    }

    private static void StyleSelectionCard(Button btn, bool active)
    {
        btn.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        btn.Background = active
            ? new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF));
    }

    private void HighlightShapeCards()
    {
        StyleSelectionCard(CardShapeNotch, _settings.Shape == NotchShape.Notch);
        StyleSelectionCard(CardShapeIsland, _settings.Shape == NotchShape.Island);
    }

    private void HighlightThemeSwatches()
    {
        StyleSelectionCard(SwatchAlbumSync, _settings.Theme == NotchTheme.AlbumSync);
        StyleSelectionCard(SwatchLiquid, _settings.Theme == NotchTheme.LiquidGlass);
        StyleSelectionCard(SwatchObsidian, _settings.Theme == NotchTheme.Obsidian);
        StyleSelectionCard(SwatchGraphite, _settings.Theme == NotchTheme.Graphite);
        StyleSelectionCard(SwatchMidnight, _settings.Theme == NotchTheme.Midnight);
        StyleSelectionCard(SwatchCandy, _settings.Theme == NotchTheme.CandyRose);
        StyleSelectionCard(SwatchFrost, _settings.Theme == NotchTheme.Frost);
    }

    private void HighlightRestingFaces()
    {
        StyleSelectionCard(FaceClockWeather, _settings.RestingFace == RestingFace.ClockWeather);
        StyleSelectionCard(FaceClockOnly, _settings.RestingFace == RestingFace.ClockOnly);
        StyleSelectionCard(FaceClockDate, _settings.RestingFace == RestingFace.ClockDate);
        StyleSelectionCard(FaceSystemStats, _settings.RestingFace == RestingFace.SystemStats);
        StyleSelectionCard(FaceBattery, _settings.RestingFace == RestingFace.Battery);
        StyleSelectionCard(FaceMinimal, _settings.RestingFace == RestingFace.Minimal);
    }

    private void RebuildMonitors()
    {
        MonitorListStack.Children.Clear();
        var monitors = MonitorService.GetMonitors();
        foreach (var m in monitors)
        {
            bool active = _settings.MonitorIndex == m.Index || (_settings.MonitorIndex >= monitors.Count && m.Index == 0);
            var btn = new Button
            {
                Style = (Style)FindResource("PillBtn"),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(4, 0, 0, 0),
                Background = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                Content = new TextBlock
                {
                    Text = $"Display {m.Index + 1}",
                    FontSize = 11,
                    Foreground = active ? Brushes.Black : Brushes.White
                },
                ToolTip = m.Label
            };
            int idx = m.Index;
            btn.Click += (_, _) =>
            {
                _settings.MonitorIndex = idx;
                SaveAndNotify();
                RebuildMonitors();
            };
            MonitorListStack.Children.Add(btn);
        }
    }

    private void RebuildAppsList()
    {
        SettingsAppsList.Children.Clear();
        if (_launcher.Apps.Count == 0)
        {
            SettingsAppsList.Children.Add(new TextBlock
            {
                Text = "No pinned apps yet. Click '+ Add Application' above.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(4)
            });
            return;
        }

        for (int i = 0; i < _launcher.Apps.Count; i++)
        {
            int idx = i;
            var app = _launcher.Apps[i];
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (app.Icon != null)
            {
                var img = new Image { Source = app.Icon, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(img);
            }

            var nameLbl = new TextBlock
            {
                Text = app.Name,
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameLbl, 1);
            row.Children.Add(nameLbl);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = $"Ctrl+Alt+{i + 1}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)) }
            });
            var rem = new Button
            {
                Style = (Style)FindResource("PillBtn"),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x45, 0x3A)),
                Padding = new Thickness(8, 3, 8, 3),
                Content = new TextBlock { Text = "Remove", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0x61)) }
            };
            rem.Click += (_, _) => { _launcher.RemoveAt(idx); RebuildAppsList(); };
            right.Children.Add(rem);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            SettingsAppsList.Children.Add(row);
        }
    }

    private void SaveAndNotify()
    {
        _settings.Save();
        _onChanged();
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) SelectPage(tag);
    }

    private void ShapeNotch_Click(object sender, RoutedEventArgs e)
    {
        _settings.Shape = NotchShape.Notch;
        HighlightShapeCards();
        SaveAndNotify();
    }

    private void ShapeIsland_Click(object sender, RoutedEventArgs e)
    {
        _settings.Shape = NotchShape.Island;
        HighlightShapeCards();
        SaveAndNotify();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && Enum.TryParse<NotchTheme>(s, out var theme))
        {
            _settings.Theme = theme;
            HighlightThemeSwatches();
            SaveAndNotify();
        }
    }

    private void Face_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && Enum.TryParse<RestingFace>(s, out var face))
        {
            _settings.RestingFace = face;
            HighlightRestingFaces();
            SaveAndNotify();
        }
    }

    private void RadiusSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_hydrating) return;
        _settings.ExpandedCornerRadius = Math.Round(e.NewValue);
        RadiusValLbl.Text = $"{(int)_settings.ExpandedCornerRadius} px";
        SaveAndNotify();
    }

    private void MarginSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_hydrating) return;
        _settings.IslandTopMargin = Math.Round(e.NewValue);
        MarginValLbl.Text = $"{(int)_settings.IslandTopMargin} px (Floating Island mode)";
        SaveAndNotify();
    }

    private void HoverDelaySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_hydrating) return;
        _settings.HoverDelayMs = (int)(Math.Round(e.NewValue / 10.0) * 10);
        HoverDelayLbl.Text = _settings.HoverDelayMs == 0
            ? "0 ms (instant on touch)"
            : $"{_settings.HoverDelayMs} ms (prevents accidental tab-switch opens)";
        SaveAndNotify();
    }

    private void ToggleSetting_Click(object sender, RoutedEventArgs e)
    {
        if (_hydrating) return;
        _settings.ShowNotchOutline = ChkNotchOutline.IsChecked == true;
        _settings.ShowAlbumGlow = ChkAlbumGlow.IsChecked == true;
        _settings.ShowPrivacyDots = ChkPrivacyDots.IsChecked == true;
        _settings.SlimOnFullscreenOrMaximized = ChkSlim.IsChecked == true;
        _settings.GameStripOnFullscreen = ChkGameStrip.IsChecked == true;
        _settings.ShowVolumeHud = ChkVolumeHud.IsChecked == true;
        _settings.ShowDeviceHuds = ChkDeviceHuds.IsChecked == true;
        _settings.ShowLockKeyHuds = ChkLockKeyHuds.IsChecked == true;
        _settings.ShowTrackChangeHud = ChkTrackChangeHud.IsChecked == true;
        _settings.ShowClipboardHud = ChkClipboardHud.IsChecked == true;
        bool startup = ChkStartup.IsChecked == true;
        if (startup != _settings.LaunchOnStartup)
        {
            _settings.LaunchOnStartup = startup;
            StartupService.SetEnabled(startup);
        }
        SaveAndNotify();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pin an application to Pill",
            Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _launcher.AddApp(dlg.FileName);
            RebuildAppsList();
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        _settings.Shape = def.Shape;
        _settings.Theme = def.Theme;
        _settings.RestingFace = def.RestingFace;
        _settings.ExpandedCornerRadius = def.ExpandedCornerRadius;
        _settings.IslandTopMargin = def.IslandTopMargin;
        _settings.HoverDelayMs = def.HoverDelayMs;
        _settings.SlimOnFullscreenOrMaximized = def.SlimOnFullscreenOrMaximized;
        _settings.GameStripOnFullscreen = def.GameStripOnFullscreen;
        _settings.ShowVolumeHud = def.ShowVolumeHud;
        _settings.ShowDeviceHuds = def.ShowDeviceHuds;
        _settings.ShowLockKeyHuds = def.ShowLockKeyHuds;
        _settings.ShowTrackChangeHud = def.ShowTrackChangeHud;
        _settings.ShowClipboardHud = def.ShowClipboardHud;
        _settings.ShowNotchOutline = def.ShowNotchOutline;
        _settings.ShowAlbumGlow = def.ShowAlbumGlow;
        _settings.ShowPrivacyDots = def.ShowPrivacyDots;
        HydrateControls();
        SaveAndNotify();
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e) => _onTestToast?.Invoke();

    private void QuitApp_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onQuit();
    }
}
