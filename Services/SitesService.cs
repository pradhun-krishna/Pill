using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace NotchWin.Services;

public sealed class QuickSite
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string AccentHex { get; set; } = "#4E7BFF";
}

/// <summary>
/// One-click Quick Sites launcher with local JSON persistence (inspired by NotchPixel's Globe tab).
/// </summary>
public sealed class SitesService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pill", "sites.json");

    private readonly List<QuickSite> _sites = new();
    public IReadOnlyList<QuickSite> Sites => _sites;
    public event Action? Changed;

    public SitesService()
    {
        Load();
        if (_sites.Count == 0)
        {
            _sites.Add(new QuickSite { Name = "YT Music", Url = "https://music.youtube.com", AccentHex = "#FF1744" });
            _sites.Add(new QuickSite { Name = "YouTube", Url = "https://youtube.com", AccentHex = "#FF0033" });
            _sites.Add(new QuickSite { Name = "Spotify", Url = "https://open.spotify.com", AccentHex = "#1DB954" });
            Save();
        }
    }

    public void AddSite(string name, string rawUrl)
    {
        if (_sites.Count >= 6) return;
        string url = rawUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

        string cleanName = name.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            string host = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
            string first = host.Split('.')[0];
            cleanName = first.Length > 0 ? char.ToUpperInvariant(first[0]) + first[1..] : host;
        }

        _sites.Add(new QuickSite
        {
            Name = cleanName,
            Url = url,
            AccentHex = PickAccentHex(uri.Host)
        });
        Save();
        Changed?.Invoke();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _sites.Count) return;
        _sites.RemoveAt(index);
        Save();
        Changed?.Invoke();
    }

    public static void Open(QuickSite site)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = site.Url, UseShellExecute = true });
        }
        catch { }
    }

    public static string ShortDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            return u.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        return url;
    }

    public static Color ParseAccent(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x4E, 0x7B, 0xFF); }
    }

    private static string PickAccentHex(string host)
    {
        string h = host.ToLowerInvariant();
        if (h.Contains("music.youtube")) return "#FF1744";
        if (h.Contains("youtube") || h.Contains("youtu.be")) return "#FF0033";
        if (h.Contains("spotify")) return "#1DB954";
        if (h.Contains("github")) return "#8B949E";
        if (h.Contains("discord")) return "#5865F2";
        if (h.Contains("reddit")) return "#FF4500";
        if (h.Contains("netflix")) return "#E50914";
        if (h.Contains("twitch")) return "#9146FF";
        if (h.Contains("whatsapp")) return "#25D366";
        if (h.Contains("mail.google") || h.Contains("gmail")) return "#EA4335";
        if (h.Contains("notion")) return "#D8DCE6";
        if (h.Contains("figma")) return "#A259FF";
        return "#0A84FF";
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var list = JsonSerializer.Deserialize<List<QuickSite>>(File.ReadAllText(StorePath));
            if (list != null) _sites.AddRange(list.Take(6));
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_sites, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
