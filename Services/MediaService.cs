using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NotchWin.Services;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    string SourceApp,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdated,
    BitmapImage? AlbumArt,
    Color AccentColor,
    Color SecondaryColor
);

/// <summary>
/// Native WinRT GlobalSystemMediaTransportControls (SMTC) integration.
/// Fingerprints actual thumbnail bytes (instead of skipping on same title) and
/// does a 700ms follow-up check on track change so browsers (Brave/Chrome/Edge)
/// that update YouTube thumbnails after title changes never show stale artwork.
/// </summary>
public sealed class MediaService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _lastTrackKey = "";
    private string _lastArtHash = "";
    private BitmapImage? _cachedArt;
    private Color _cachedColor = Color.FromRgb(120, 120, 140);
    private Color _cachedSecondary = Color.FromRgb(54, 58, 86);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _delayedArtCts;

    public MediaSnapshot? Current { get; private set; }
    public event Action<MediaSnapshot?>? MediaChanged;

    public MediaService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnSessionsUpdated;
            _manager.SessionsChanged += OnSessionsUpdated;
            AttachSession(SelectBestSession());
            await RefreshAsync();
        }
        catch { }
    }

    private void OnSessionsUpdated(GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        AttachSession(SelectBestSession());
        _ = RefreshAsync();
    }

    private GlobalSystemMediaTransportControlsSession? SelectBestSession()
    {
        if (_manager == null) return null;
        var current = _manager.GetCurrentSession();
        if (current?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            return current;

        try
        {
            foreach (var s in _manager.GetSessions())
            {
                if (s?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return s;
            }
        }
        catch { }

        return current;
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? next)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }
        _session = next;
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var best = SelectBestSession();
            if (best != _session) AttachSession(best);
            var session = _session;
            if (session == null)
            {
                _lastTrackKey = "";
                _lastArtHash = "";
                _cachedArt = null;
                Publish(null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            string title = props?.Title?.Trim() ?? "";
            string artist = props?.Artist?.Trim() ?? "";
            string album = props?.AlbumTitle?.Trim() ?? "";

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            {
                Publish(null);
                return;
            }

            // Always inspect thumbnail bytes & hash them: browsers often emit the
            // new track title first and update the thumbnail 300-800ms later!
            await RefreshThumbnailIfChangedAsync(props?.Thumbnail);

            string trackKey = $"{session.SourceAppUserModelId}|{title}|{artist}|{album}";
            if (trackKey != _lastTrackKey)
            {
                _lastTrackKey = trackKey;
                ScheduleDelayedThumbnailCheck();
            }

            bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline != null && timeline.EndTime > timeline.StartTime
                ? timeline.EndTime - timeline.StartTime
                : TimeSpan.Zero;
            var position = timeline != null && timeline.Position >= timeline.StartTime
                ? timeline.Position - timeline.StartTime
                : TimeSpan.Zero;
            var updated = timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow;

            var snap = new MediaSnapshot(
                title,
                artist,
                album,
                FriendlySourceName(session.SourceAppUserModelId),
                isPlaying,
                position,
                duration,
                updated,
                _cachedArt,
                _cachedColor,
                _cachedSecondary);

            Publish(snap);
        }
        catch { }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void ScheduleDelayedThumbnailCheck()
    {
        _delayedArtCts?.Cancel();
        _delayedArtCts = new CancellationTokenSource();
        var ct = _delayedArtCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, ct);
                if (!ct.IsCancellationRequested) await RefreshAsync();
                await Task.Delay(800, ct);
                if (!ct.IsCancellationRequested) await RefreshAsync();
                await Task.Delay(1200, ct);
                if (!ct.IsCancellationRequested) await RefreshAsync();
            }
            catch { }
        }, ct);
    }

    private async Task RefreshThumbnailIfChangedAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef == null)
        {
            if (_lastArtHash != "")
            {
                _lastArtHash = "";
                _cachedArt = null;
                _cachedColor = Color.FromRgb(110, 110, 128);
                _cachedSecondary = Color.FromRgb(52, 56, 78);
            }
            return;
        }

        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            if (bytes.Length == 0) return;

            string hash = ComputeFastHash(bytes);
            if (hash == _lastArtHash && _cachedArt != null) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();

            _lastArtHash = hash;
            _cachedArt = bmp;
            (_cachedColor, _cachedSecondary) = ExtractPalette(bmp);
        }
        catch { }
    }

    private static string ComputeFastHash(byte[] bytes)
    {
        uint h = 2166136261;
        int step = Math.Max(1, bytes.Length / 128);
        for (int i = 0; i < bytes.Length; i += step)
        {
            h = (h ^ bytes[i]) * 16777619;
        }
        return $"{bytes.Length}:{h:X8}";
    }

    private void Publish(MediaSnapshot? snapshot)
    {
        Current = snapshot;
        _dispatcher.BeginInvoke(() => MediaChanged?.Invoke(snapshot));
    }

    public async Task TogglePlayPauseAsync() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async Task NextAsync() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async Task PreviousAsync() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    public async Task SeekAsync(TimeSpan position)
    {
        try
        {
            if (_session != null)
            {
                var timeline = _session.GetTimelineProperties();
                var target = (timeline?.StartTime ?? TimeSpan.Zero) + position;
                await _session.TryChangePlaybackPositionAsync(target.Ticks);
            }
        }
        catch { }
    }

    private static (Color Primary, Color Secondary) ExtractPalette(BitmapSource bmp)
    {
        try
        {
            var scaled = new TransformedBitmap(bmp, new ScaleTransform(16.0 / bmp.PixelWidth, 16.0 / bmp.PixelHeight));
            var fmt = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = fmt.PixelWidth, h = fmt.PixelHeight;
            byte[] pixels = new byte[w * h * 4];
            fmt.CopyPixels(pixels, w * 4, 0);

            // 12 hue buckets (30 deg each) for rich, non-muddy dual-tone album art palettes.
            double[] rB = new double[12], gB = new double[12], bB = new double[12], wB = new double[12];
            double fallR = 0, fallG = 0, fallB = 0, fallW = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
                double max = Math.Max(rf, Math.Max(gf, bf));
                double min = Math.Min(rf, Math.Min(gf, bf));
                double delta = max - min;
                double lum = rf * 0.299 + gf * 0.587 + bf * 0.114;
                double sat = max < 0.001 ? 0 : delta / max;

                if (lum >= 0.08 && lum <= 0.94)
                {
                    fallR += r; fallG += g; fallB += b; fallW += 1;
                }
                if (lum < 0.09 || lum > 0.93 || sat < 0.14) continue;

                double hue = 0;
                if (delta > 0.001)
                {
                    if (max == rf) hue = ((gf - bf) / delta) % 6.0;
                    else if (max == gf) hue = (bf - rf) / delta + 2.0;
                    else hue = (rf - gf) / delta + 4.0;
                    hue *= 60.0;
                    if (hue < 0) hue += 360.0;
                }

                int bucket = Math.Clamp((int)(hue / 30.0) % 12, 0, 11);
                double wgt = 0.2 + sat * sat * 3.0 * (1.0 - Math.Abs(lum - 0.52) * 0.9);
                rB[bucket] += r * wgt; gB[bucket] += g * wgt; bB[bucket] += b * wgt; wB[bucket] += wgt;
            }

            int best = -1;
            double bestW = 0;
            for (int i = 0; i < 12; i++)
            {
                if (wB[i] > bestW) { bestW = wB[i]; best = i; }
            }

            Color primary;
            if (best >= 0 && bestW > 0.1)
            {
                primary = Color.FromRgb(
                    (byte)Math.Clamp(rB[best] / bestW, 45, 245),
                    (byte)Math.Clamp(gB[best] / bestW, 45, 245),
                    (byte)Math.Clamp(bB[best] / bestW, 45, 245));
            }
            else if (fallW > 0)
            {
                primary = Color.FromRgb(
                    (byte)Math.Clamp(fallR / fallW, 55, 220),
                    (byte)Math.Clamp(fallG / fallW, 55, 220),
                    (byte)Math.Clamp(fallB / fallW, 55, 220));
            }
            else
            {
                return (Color.FromRgb(120, 130, 165), Color.FromRgb(48, 56, 84));
            }

            // Pick a distinct secondary hue bucket (>= 60 deg away) for a lush gradient.
            int second = -1;
            double secondW = 0;
            if (best >= 0)
            {
                for (int i = 0; i < 12; i++)
                {
                    int dist = Math.Min(Math.Abs(i - best), 12 - Math.Abs(i - best));
                    if (dist >= 2 && wB[i] > secondW) { secondW = wB[i]; second = i; }
                }
            }

            Color secondary = (second >= 0 && secondW > bestW * 0.14)
                ? Color.FromRgb(
                    (byte)Math.Clamp(rB[second] / secondW, 32, 225),
                    (byte)Math.Clamp(gB[second] / secondW, 32, 225),
                    (byte)Math.Clamp(bB[second] / secondW, 32, 225))
                : Color.FromRgb(
                    (byte)Math.Clamp(primary.R * 0.45 + 14, 18, 130),
                    (byte)Math.Clamp(primary.G * 0.40 + 16, 18, 130),
                    (byte)Math.Clamp(primary.B * 0.58 + 24, 24, 150));

            return (primary, secondary);
        }
        catch { return (Color.FromRgb(120, 130, 165), Color.FromRgb(48, 56, 84)); }
    }

    private static string FriendlySourceName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "Media";
        string lower = appId.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("applemusic")) return "Apple Music";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("zune") || lower.Contains("mediaplayer")) return "Media Player";
        string bare = appId.Split('!')[0].Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        int dot = bare.LastIndexOf('.');
        if (dot >= 0 && dot < bare.Length - 1) bare = bare[(dot + 1)..];
        return bare.Length > 0 ? char.ToUpperInvariant(bare[0]) + bare[1..] : "Media";
    }

    public void Dispose()
    {
        _delayedArtCts?.Cancel();
        AttachSession(null);
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnSessionsUpdated;
            _manager.SessionsChanged -= OnSessionsUpdated;
        }
        _refreshLock.Dispose();
    }
}
