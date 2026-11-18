using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotchWin.Services;

/// <summary>
/// Native laptop display brightness control via Windows LCD IOCTL (\\.\LCD).
/// Zero NuGet packages, zero WMI/PowerShell overhead, sub-millisecond latency.
/// </summary>
public sealed class BrightnessService
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS = 0x00230498;
    private const uint IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS = 0x0023049C;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAY_BRIGHTNESS
    {
        public byte ucDisplayPolicy; // 1 = AC, 2 = DC, 3 = Both
        public byte ucACBrightness;
        public byte ucDCBrightness;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref DISPLAY_BRIGHTNESS lpInBuffer, int nInBufferSize,
        ref DISPLAY_BRIGHTNESS lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    public bool IsSupported { get; private set; }

    public BrightnessService()
    {
        IsSupported = TryGetBrightness(out _);
    }

    public bool TryGetBrightness(out int percent)
    {
        percent = 70;
        try
        {
            using var handle = CreateFile(@"\\.\LCD", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return false;
            var input = new DISPLAY_BRIGHTNESS();
            var output = new DISPLAY_BRIGHTNESS();
            int size = Marshal.SizeOf<DISPLAY_BRIGHTNESS>();
            if (!DeviceIoControl(handle, IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, ref input, 0, ref output, size, out _, IntPtr.Zero))
                return false;
            percent = output.ucDisplayPolicy == 2 ? output.ucDCBrightness : output.ucACBrightness;
            IsSupported = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetBrightness(int percent)
    {
        byte clamped = (byte)Math.Clamp(percent, 0, 100);
        try
        {
            using var handle = CreateFile(@"\\.\LCD", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return;
            var val = new DISPLAY_BRIGHTNESS
            {
                ucDisplayPolicy = 3,
                ucACBrightness = clamped,
                ucDCBrightness = clamped
            };
            var dummy = new DISPLAY_BRIGHTNESS();
            int size = Marshal.SizeOf<DISPLAY_BRIGHTNESS>();
            DeviceIoControl(handle, IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS, ref val, size, ref dummy, 0, out _, IntPtr.Zero);
        }
        catch { }
    }
}
