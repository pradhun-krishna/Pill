using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchWin.Services;

public sealed class AppShortcut
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public ImageSource? Icon { get; set; }
}

/// <summary>
/// Quick App Launcher (up to 6 pinned apps, Ctrl+Alt+1..6 global hotkeys)
/// with native Win32 SHGetFileInfo icons and zero external dependencies.
/// </summary>
public sealed class LauncherService
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pill", "apps.json");

    public List<AppShortcut> Apps { get; } = new();
    public event Action? Changed;

    public LauncherService()
    {
        LoadOrSeed();
    }

    public void LaunchAt(int index)
    {
        if (index < 0 || index >= Apps.Count) return;
        Launch(Apps[index]);
    }

    public static void Launch(AppShortcut app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
        }
        catch { }
    }

    public void AddApp(string exePath)
    {
        if (!File.Exists(exePath) || Apps.Count >= 6) return;
        if (Apps.Any(a => string.Equals(a.Path, exePath, StringComparison.OrdinalIgnoreCase))) return;
        var item = new AppShortcut
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(exePath),
            Path = exePath,
            Icon = ExtractIcon(exePath)
        };
        Apps.Add(item);
        Save();
        Changed?.Invoke();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Apps.Count) return;
        Apps.RemoveAt(index);
        Save();
        Changed?.Invoke();
    }

    private void LoadOrSeed()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var loaded = JsonSerializer.Deserialize<List<AppShortcut>>(File.ReadAllText(StorePath));
                if (loaded != null && loaded.Count > 0)
                {
                    foreach (var a in loaded.Take(6))
                    {
                        a.Icon = ExtractIcon(a.Path);
                        Apps.Add(a);
                    }
                    return;
                }
            }
        }
        catch { }

        // Smart default seed of commonly installed Windows apps that actually exist on this PC.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        (string name, string[] paths)[] candidates =
        {
            ("Chrome", new[] { System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), System.IO.Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe") }),
            ("Edge", new[] { System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), System.IO.Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe") }),
            ("VS Code", new[] { System.IO.Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe") }),
            ("Spotify", new[] { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe") }),
            ("Terminal", new[] { System.IO.Path.Combine(local, @"Microsoft\WindowsApps\wt.exe") }),
            ("Explorer", new[] { System.IO.Path.Combine(win, "explorer.exe") }),
            ("Notepad", new[] { System.IO.Path.Combine(win, @"System32\notepad.exe") }),
            ("Calc", new[] { System.IO.Path.Combine(win, @"System32\calc.exe") }),
        };

        foreach (var (name, paths) in candidates)
        {
            if (Apps.Count >= 6) break;
            var hit = paths.FirstOrDefault(File.Exists);
            if (hit != null)
            {
                Apps.Add(new AppShortcut { Name = name, Path = hit, Icon = ExtractIcon(hit) });
            }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Apps));
        }
        catch { }
    }

    private static ImageSource? ExtractIcon(string path)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (shinfo.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch { return null; }
    }
}
