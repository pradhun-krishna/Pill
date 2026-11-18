using System.Windows.Media;

namespace NotchWin.UI;

/// <summary>
/// Crisp monochrome 24x24 Lucide-style vector geometry paths.
/// Zero emojis, zero blurry bitmaps, razor-sharp at any DPI.
/// </summary>
public static class Icons
{
    private static Geometry Make(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    // Media
    public static readonly Geometry Play = Make("M6 4.75a1.25 1.25 0 0 1 1.9-.107l11.5 6.85a1.25 1.25 0 0 1 0 2.14l-11.5 6.85A1.25 1.25 0 0 1 6 19.41V4.75Z");
    public static readonly Geometry Pause = Make("M6 5h4v14H6V5Zm8 0h4v14h-4V5Z");
    public static readonly Geometry SkipBack = Make("M19 20L9 12l10-8v16ZM5 19V5h2v14H5Z");
    public static readonly Geometry SkipForward = Make("M5 4l10 8-10 8V4Zm12 1h2v14h-2V5Z");
    public static readonly Geometry MusicNote = Make("M9 18V5l12-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0Zm12-2a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z");
    public static readonly Geometry Lyrics = Make("M4 6h16M4 12h11M4 18h7m9-4v6m0 0a2 2 0 1 1-2-2 2 2 0 0 1 2 2Z");

    // Audio & Display
    public static readonly Geometry Volume = Make("M11 5L6 9H2v6h4l5 4V5Zm4.54 3.46a5 5 0 0 1 0 7.07m2.83-9.9a9 9 0 0 1 0 12.73");
    public static readonly Geometry VolumeMuted = Make("M11 5L6 9H2v6h4l5 4V5Zm12 4l-6 6m0-6l6 6");
    public static readonly Geometry Sun = Make("M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm0-13v2m0 14v2M4.93 4.93l1.41 1.41m11.32 11.32l1.41 1.41M2 12h2m16 0h2M4.93 19.07l1.41-1.41m11.32-11.32l1.41-1.41");
    public static readonly Geometry Moon = Make("M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79Z");
    public static readonly Geometry Cloud = Make("M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z");
    public static readonly Geometry CloudRain = Make("M16 13v6m-4-4v6m-4-8v6m12-6.42A5 5 0 0 0 18 7h-1.26A8 8 0 1 0 4 15.25");
    public static readonly Geometry CloudStorm = Make("M19 16.9A5 5 0 0 0 18 7h-1.26a8 8 0 1 0-11.62 9M13 11l-4 6h6l-4 6");

    // Privacy & Hardware
    public static readonly Geometry Mic = Make("M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Zm7 10a7 7 0 0 1-14 0m7 7v3m-4 0h8");
    public static readonly Geometry MicOff = Make("M2 2l20 20M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V5a3 3 0 0 0-5.94-.6M17 16.95A7 7 0 0 1 5 12m14 0a7 7 0 0 1-.11 1.23M12 19v3m-4 0h8");
    public static readonly Geometry Wifi = Make("M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01");
    public static readonly Geometry Bluetooth = Make("M6.5 6.5l11 11L12 23V1l5.5 5.5-11 11");
    public static readonly Geometry Headphones = Make("M3 18v-6a9 9 0 0 1 18 0v6M21 19a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3v5ZM3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2H3v5Z");
    public static readonly Geometry Bolt = Make("M13 2L3 14h9l-1 8 10-12h-9l1-8Z");
    public static readonly Geometry Usb = Make("M10 7a2 2 0 1 1 0-4 2 2 0 0 1 0 4Zm0 0v11m0-4l5-3V7m-1-2h2v2h-2V5Zm-4 13a2 2 0 1 0 0 4 2 2 0 0 0 0-4Zm0-5l-5-3v2m0 0a1 1 0 1 0 0 2 1 1 0 0 0 0-2Z");
    public static readonly Geometry CapsLock = Make("M12 4l-7 7h4v5h6v-5h4l-7-7ZM7 20h10");

    // Navigation & Productivity
    public static readonly Geometry Home = Make("M3 10.5L12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1v-9.5Z");
    public static readonly Geometry Clipboard = Make("M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M9 5a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2M9 5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2");
    public static readonly Geometry Sliders = Make("M4 21v-7m0-4V3m8 18v-9m0-4V3m8 18v-5m0-4V3M1 14h6m2-6h6m2 8h6");
    public static readonly Geometry Timer = Make("M10 2h4m-2 12l3-3m5 2a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z");
    public static readonly Geometry Grid = Make("M3 3h7v7H3V3Zm11 0h7v7h-7V3ZM3 14h7v7H3v-7Zm11 0h7v7h-7v-7Z");
    public static readonly Geometry Settings = Make("M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.4-3a7.4 7.4 0 0 0-.1-1l2.1-1.6-2-3.5-2.5 1a7.4 7.4 0 0 0-1.7-1L14.8 3h-4l-.4 2.9a7.4 7.4 0 0 0-1.7 1l-2.5-1-2 3.5L6.3 11a7.4 7.4 0 0 0 0 2l-2.1 1.6 2 3.5 2.5-1a7.4 7.4 0 0 0 1.7 1l.4 2.9h4l.4-2.9a7.4 7.4 0 0 0 1.7-1l2.5 1 2-3.5-2.1-1.6c.1-.3.1-.7.1-1Z");
    public static readonly Geometry Pin = Make("M12 17v5M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16h14v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1v3.76Z");
    public static readonly Geometry Power = Make("M18.36 6.64a9 9 0 1 1-12.73 0M12 2v10");
    public static readonly Geometry Palette = Make("M12 2a10 10 0 0 0 0 20 2 2 0 0 0 2-2v-.5a1.5 1.5 0 0 1 1.5-1.5H17a5 5 0 0 0 5-5 10 10 0 0 0-10-10Zm-4.5 9a1 1 0 1 1 0-2 1 1 0 0 1 0 2Zm3-3.5a1 1 0 1 1 0-2 1 1 0 0 1 0 2Zm4.5 1a1 1 0 1 1 0-2 1 1 0 0 1 0 2Zm2 4a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z");
    public static readonly Geometry Globe = Make("M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20ZM2 12h20M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10Z");
    public static readonly Geometry Battery = Make("M2 7h16a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Zm20 4v2M11 9l-2 3h4l-2 3");
    public static readonly Geometry AppsDots = Make("M5 5h2v2H5V5Zm6 0h2v2h-2V5Zm6 0h2v2h-2V5ZM5 11h2v2H5v-2Zm6 0h2v2h-2v-2Zm6 0h2v2h-2v-2ZM5 17h2v2H5v-2Zm6 0h2v2h-2v-2Zm6 0h2v2h-2v-2Z");
    public static readonly Geometry RotateCcw = Make("M3 12a9 9 0 1 0 3-6.7L3 8m0-5v5h5");
    public static readonly Geometry Plus = Make("M12 5v14M5 12h14");
    public static readonly Geometry Minus = Make("M5 12h14");
    public static readonly Geometry Camera = Make("M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2v11ZM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z");
    public static readonly Geometry Scissors = Make("M6 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm0 12a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM20 4L8.12 15.88M14.47 14.48L20 20M8.12 8.12L12 12");
    public static readonly Geometry Layers = Make("M12 2L2 7l10 5 10-5-10-5ZM2 17l10 5 10-5M2 12l10 5 10-5");
    public static readonly Geometry Check = Make("M20 6L9 17l-5-5");
    public static readonly Geometry Trash = Make("M3 6h18M8 6V4h8v2m-9 0l1 14h8l1-14");
    public static readonly Geometry Link = Make("M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71");
    public static readonly Geometry Image = Make("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Zm3.5 5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3ZM21 15l-5-5L5 21");
    public static readonly Geometry FileText = Make("M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Zm0 0v6h6M8 13h8M8 17h5");

    public static Geometry ForWeather(string iconKey) => iconKey switch
    {
        "sun" => Sun,
        "moon" => Moon,
        "rain" => CloudRain,
        "storm" => CloudStorm,
        _ => Cloud
    };
}
