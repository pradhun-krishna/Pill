using System.IO;
using System.Text.Json;

namespace NotchWin.Core;

public enum NotchShape
{
    Notch,
    Island
}

public enum NotchTheme
{
    LiquidGlass,
    AlbumSync,
    Obsidian,
    Graphite,
    Midnight,
    CandyRose,
    Frost
}

public enum RestingFace
{
    ClockWeather,
    ClockOnly,
    ClockDate,
    SystemStats,
    Battery,
    Minimal
}

public sealed class AppSettings
{
    public NotchShape Shape { get; set; } = NotchShape.Notch;
    public NotchTheme Theme { get; set; } = NotchTheme.LiquidGlass;
    public RestingFace RestingFace { get; set; } = RestingFace.ClockWeather;
    public double ExpandedCornerRadius { get; set; } = 26;
    public double IslandTopMargin { get; set; } = 8;
    public double SlimBarHeight { get; set; } = 5;
    public int HoverDelayMs { get; set; } = 60;
    public int MonitorIndex { get; set; } = 0;

    public bool SlimOnFullscreenOrMaximized { get; set; } = true;
    public bool GameStripOnFullscreen { get; set; } = false;
    public bool ShowVolumeHud { get; set; } = true;
    public bool ShowDeviceHuds { get; set; } = true;
    public bool ShowLockKeyHuds { get; set; } = true;
    public bool ShowTrackChangeHud { get; set; } = true;
    public bool ShowClipboardHud { get; set; } = true;
    public bool ShowNotchOutline { get; set; } = true;
    public bool ShowAlbumGlow { get; set; } = true;
    public bool ShowPrivacyDots { get; set; } = true;
    public bool LaunchOnStartup { get; set; } = false;

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pill");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
