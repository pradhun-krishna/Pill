using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchWin.Services;

public enum ClipKind
{
    Text,
    Link,
    Image
}

public sealed class ClipboardItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string Preview { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
    /// <summary>Tiny 48px thumbnail for the UI list (~9 KB instead of 8 MB raw screenshot).</summary>
    public BitmapSource? Thumbnail { get; init; }
    /// <summary>Compressed PNG bytes so restoring an image works without keeping multi-MB raw bitmaps in RAM.</summary>
    public byte[]? PngBytes { get; init; }
}

/// <summary>
/// Low-memory, event-driven clipboard history via Win32 AddClipboardFormatListener (0% idle CPU).
/// Stores compressed PNG bytes (max 6 images) + 48px UI thumbnails to keep RAM minimal.
/// </summary>
public sealed class ClipboardService
{
    private const int MaxUnpinned = 24;
    private const int MaxImages = 6;
    private static readonly string PinnedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pill", "pinned-clips.json");

    private readonly List<ClipboardItem> _items = new();
    private bool _suppressNext;
    private string _lastImgFingerprint = "";
    private DateTime _lastImgAt = DateTime.MinValue;

    public IReadOnlyList<ClipboardItem> Items => _items;
    public event Action? Changed;
    public event Action<ClipboardItem>? ItemCopied;

    public ClipboardService() => LoadPinned();

    public void OnClipboardUpdate()
    {
        if (_suppressNext) { _suppressNext = false; return; }

        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return;

            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return;
            if (data.GetDataPresent("Clipboard Viewer Ignore")) return;
            if (data.GetDataPresent("CanIncludeInClipboardHistory"))
            {
                try
                {
                    if (data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4)
                    {
                        byte[] buf = new byte[4];
                        ms.Position = 0;
                        ms.Read(buf, 0, 4);
                        if (BitConverter.ToInt32(buf, 0) == 0) return;
                    }
                }
                catch { }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;

                string trimmed = text.Trim();
                bool isLink = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                              && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

                var existing = _items.FirstOrDefault(x => x.Kind != ClipKind.Image && x.Text == text);
                if (existing != null)
                {
                    existing.CopiedAt = DateTime.Now;
                    _items.Remove(existing);
                    _items.Insert(0, existing);
                    SortAndTrim();
                    Changed?.Invoke();
                    ItemCopied?.Invoke(existing);
                    return;
                }

                string preview = trimmed.Replace("\r\n", " ").Replace('\n', ' ');
                if (preview.Length > 140) preview = preview[..140] + "…";

                var textItem = new ClipboardItem
                {
                    Kind = isLink ? ClipKind.Link : ClipKind.Text,
                    Text = text,
                    Preview = preview,
                    CopiedAt = DateTime.Now
                };
                _items.Insert(0, textItem);
                SortAndTrim();
                Changed?.Invoke();
                ItemCopied?.Invoke(textItem);
            }
            else if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img == null) return;

                string fp = ComputeImageFingerprint(img);
                if (fp == _lastImgFingerprint && (DateTime.UtcNow - _lastImgAt).TotalSeconds < 3.0) return;
                _lastImgFingerprint = fp;
                _lastImgAt = DateTime.UtcNow;

                var dup = _items.FirstOrDefault(x => x.Kind == ClipKind.Image && x.Fingerprint == fp);
                if (dup != null)
                {
                    dup.CopiedAt = DateTime.Now;
                    SortAndTrim();
                    Changed?.Invoke();
                    return;
                }

                byte[]? pngBytes = EncodePng(img);
                var thumb = MakeThumbnail(img, 48);

                var imgItem = new ClipboardItem
                {
                    Kind = ClipKind.Image,
                    Preview = $"Image · {img.PixelWidth}×{img.PixelHeight}",
                    Fingerprint = fp,
                    Thumbnail = thumb,
                    PngBytes = pngBytes,
                    CopiedAt = DateTime.Now
                };
                _items.Insert(0, imgItem);
                SortAndTrim();
                Changed?.Invoke();
                ItemCopied?.Invoke(imgItem);
            }
        }
        catch { }
    }

    private static byte[]? EncodePng(BitmapSource img)
    {
        try
        {
            using var ms = new MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static BitmapSource? MakeThumbnail(BitmapSource img, int targetPx)
    {
        try
        {
            double scale = Math.Min(1.0, (double)targetPx / Math.Max(img.PixelWidth, img.PixelHeight));
            var tb = new TransformedBitmap(img, new ScaleTransform(scale, scale));
            var rtb = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
            rtb.Freeze();
            return rtb;
        }
        catch { return null; }
    }

    private static string ComputeImageFingerprint(BitmapSource img)
    {
        try
        {
            var small = new TransformedBitmap(img, new ScaleTransform(8.0 / Math.Max(1, img.PixelWidth), 8.0 / Math.Max(1, img.PixelHeight)));
            var fmt = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            byte[] px = new byte[fmt.PixelWidth * fmt.PixelHeight * 4];
            fmt.CopyPixels(px, fmt.PixelWidth * 4, 0);
            uint hash = 2166136261;
            for (int i = 0; i < px.Length; i += 4)
            {
                hash = (hash ^ px[i]) * 16777619;
                hash = (hash ^ px[i + 1]) * 16777619;
                hash = (hash ^ px[i + 2]) * 16777619;
            }
            return $"{img.PixelWidth}x{img.PixelHeight}:{hash:X8}";
        }
        catch { return $"{img.PixelWidth}x{img.PixelHeight}"; }
    }

    public void CopyToClipboard(ClipboardItem item)
    {
        try
        {
            _suppressNext = true;
            if (item.Kind == ClipKind.Image && item.PngBytes != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(item.PngBytes);
                bmp.EndInit();
                bmp.Freeze();
                Clipboard.SetImage(bmp);
            }
            else
            {
                Clipboard.SetText(item.Text);
            }
            item.CopiedAt = DateTime.Now;
            SortAndTrim();
            Changed?.Invoke();
        }
        catch { _suppressNext = false; }
    }

    public void TogglePin(string id)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return;
        item.IsPinned = !item.IsPinned;
        SortAndTrim();
        SavePinned();
        Changed?.Invoke();
    }

    public void Remove(string id) { if (_items.RemoveAll(x => x.Id == id) > 0) { SavePinned(); Changed?.Invoke(); } }
    public void ClearUnpinned() { if (_items.RemoveAll(x => !x.IsPinned) > 0) Changed?.Invoke(); }

    private void SortAndTrim()
    {
        var pinned = _items.Where(x => x.IsPinned).OrderByDescending(x => x.CopiedAt).ToList();
        var unpinned = new List<ClipboardItem>();
        int imgCount = pinned.Count(x => x.Kind == ClipKind.Image);

        foreach (var item in _items.Where(x => !x.IsPinned).OrderByDescending(x => x.CopiedAt))
        {
            if (unpinned.Count >= MaxUnpinned) break;
            if (item.Kind == ClipKind.Image)
            {
                if (imgCount >= MaxImages) continue;
                imgCount++;
            }
            unpinned.Add(item);
        }

        _items.Clear();
        _items.AddRange(pinned);
        _items.AddRange(unpinned);
    }

    private sealed record SavedPin(ClipKind Kind, string Text, string Preview);

    private void SavePinned()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PinnedPath)!);
            var pins = _items.Where(x => x.IsPinned && x.Kind != ClipKind.Image)
                .Select(x => new SavedPin(x.Kind, x.Text, x.Preview)).ToList();
            File.WriteAllText(PinnedPath, JsonSerializer.Serialize(pins));
        }
        catch { }
    }

    private void LoadPinned()
    {
        try
        {
            if (!File.Exists(PinnedPath)) return;
            var pins = JsonSerializer.Deserialize<List<SavedPin>>(File.ReadAllText(PinnedPath));
            if (pins == null) return;
            foreach (var p in pins)
                _items.Add(new ClipboardItem { Kind = p.Kind, Text = p.Text, Preview = p.Preview, IsPinned = true });
        }
        catch { }
    }
}
