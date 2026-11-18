using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NotchWin.Services;

/// <summary>
/// Pure native Win32 GDI BitBlt full-screen screenshot + native Windows Snip &amp; Screen Record launchers.
/// Zero System.Drawing dependency, ~15ms capture time.
/// </summary>
public static class ScreenCaptureService
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    public static string? CapturePrimaryScreenToFileAndClipboard()
    {
        int w = GetSystemMetrics(SM_CXSCREEN);
        int h = GetSystemMetrics(SM_CYSCREEN);
        if (w <= 0 || h <= 0) return null;

        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldObj = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
            hBmp = CreateCompatibleBitmap(screenDc, w, h);
            oldObj = SelectObject(memDc, hBmp);
            BitBlt(memDc, 0, 0, w, h, screenDc, 0, 0, SRCCOPY);

            var bmpSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmpSource.Freeze();

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(dir);
            string fileName = $"Pill_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(dir, fileName);

            using (var fs = File.Create(fullPath))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmpSource));
                enc.Save(fs);
            }

            try { Clipboard.SetImage(bmpSource); } catch { }
            return fileName;
        }
        catch { return null; }
        finally
        {
            if (oldObj != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldObj);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static void LaunchSnip()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-screenclip:", UseShellExecute = true }); }
        catch { }
    }

    public static void LaunchScreenRecord()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-screenclip:?clippingMode=Video", UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo { FileName = "ms-screenclip:", UseShellExecute = true }); }
            catch { }
        }
    }
}
