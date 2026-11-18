using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotchWin.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

public sealed record LyricsResult(
    bool Found,
    bool IsSynced,
    IReadOnlyList<LyricLine> Lines,
    string? PlainLyrics
);

/// <summary>
/// Fetches real-time synced LRC lyrics from LRCLIB (free, no API key) with
/// in-memory per-track caching and fast binary-search active-line lookup.
/// </summary>
public sealed class LyricsService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private static readonly Regex LrcRegex = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\](.*)", RegexOptions.Compiled);
    private readonly Dictionary<string, LyricsResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    static LyricsService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Pill/1.0 (+https://github.com)");
    }

    public async Task<LyricsResult> FetchAsync(string title, string artist, string album, double durationSeconds, CancellationToken ct = default)
    {
        string cleanTitle = CleanTrackTitle(title);
        string cleanArtist = artist.Trim();
        string key = $"{cleanTitle}|{cleanArtist}";

        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (string.IsNullOrWhiteSpace(cleanTitle)) return Empty();

        try
        {
            // 1. Exact match endpoint
            string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}"
                       + (string.IsNullOrWhiteSpace(cleanArtist) ? "" : $"&artist_name={Uri.EscapeDataString(cleanArtist)}")
                       + (string.IsNullOrWhiteSpace(album) ? "" : $"&album_name={Uri.EscapeDataString(album)}")
                       + (durationSeconds > 5 ? $"&duration={(int)Math.Round(durationSeconds)}" : "");

            using var resp = await Http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var parsed = ParseJson(await resp.Content.ReadAsStringAsync(ct));
                if (parsed.Found)
                {
                    _cache[key] = parsed;
                    return parsed;
                }
            }

            // 2. Fallback search endpoint (handles YouTube titles like "Artist - Song (Official Video)")
            string query = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanTitle} {cleanArtist}";
            using var searchResp = await Http.GetAsync($"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}", ct);
            if (searchResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Prefer a result with syncedLyrics
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var r = ParseElement(el);
                        if (r.Found && r.IsSynced)
                        {
                            _cache[key] = r;
                            return r;
                        }
                    }
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var r = ParseElement(el);
                        if (r.Found)
                        {
                            _cache[key] = r;
                            return r;
                        }
                    }
                }
            }
        }
        catch { }

        var none = Empty();
        _cache[key] = none;
        return none;
    }

    public static int GetActiveLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0) return -1;
        // Lead slightly by 180ms so the line feels crisp on beat.
        var target = position + TimeSpan.FromMilliseconds(180);
        int lo = 0, hi = lines.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= target)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    private static LyricsResult ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseElement(doc.RootElement);
    }

    private static LyricsResult ParseElement(JsonElement el)
    {
        string? synced = el.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        string? plain = el.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        if (!string.IsNullOrWhiteSpace(synced))
        {
            var lines = ParseLrc(synced);
            if (lines.Count > 0) return new LyricsResult(true, true, lines, plain);
        }

        if (!string.IsNullOrWhiteSpace(plain))
        {
            var plainLines = plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => new LyricLine(TimeSpan.Zero, l))
                .ToList();
            return new LyricsResult(true, false, plainLines, plain);
        }

        return Empty();
    }

    private static List<LyricLine> ParseLrc(string lrc)
    {
        var list = new List<LyricLine>();
        foreach (string raw in lrc.Split('\n'))
        {
            var m = LrcRegex.Match(raw);
            if (!m.Success) continue;
            int min = int.Parse(m.Groups[1].Value);
            int sec = int.Parse(m.Groups[2].Value);
            int ms = 0;
            if (m.Groups[3].Success)
            {
                string frac = m.Groups[3].Value;
                ms = frac.Length == 2 ? int.Parse(frac) * 10 : frac.Length == 1 ? int.Parse(frac) * 100 : int.Parse(frac);
            }
            string text = m.Groups[4].Value.Trim();
            if (string.IsNullOrEmpty(text)) text = "♪";
            list.Add(new LyricLine(new TimeSpan(0, 0, min, sec, ms), text));
        }
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    private static string CleanTrackTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string cleaned = Regex.Replace(raw, @"\s*[\(\[](?:official\s*(?:music\s*)?video|lyric\s*video|lyrics|audio|hd|4k|visualizer|remastered.*?)[\)\]]", "", RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private static LyricsResult Empty() => new(false, false, Array.Empty<LyricLine>(), null);
}
