using System.Runtime.InteropServices;

namespace NotchWin.Services;

public sealed record MonitorEntry(int Index, string Label, int Left, int Top, int Width, int Height, bool IsPrimary);

/// <summary>
/// Pure Win32 EnumDisplayMonitors wrapper for multi-monitor notch placement
/// without pulling in WinForms.
/// </summary>
public static class MonitorService
{
    private const uint MONITORINFOF_PRIMARY = 1;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public static List<MonitorEntry> GetMonitors()
    {
        var list = new List<MonitorEntry>();
        int idx = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                bool primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                string label = primary ? $"Display {idx + 1} (Primary · {w}×{h})" : $"Display {idx + 1} ({w}×{h})";
                list.Add(new MonitorEntry(idx, label, mi.rcMonitor.Left, mi.rcMonitor.Top, w, h, primary));
                idx++;
            }
            return true;
        }, IntPtr.Zero);

        // Put Primary first at index 0 for predictable default.
        list.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            string label = m.IsPrimary ? $"Display {i + 1} (Primary · {m.Width}×{m.Height})" : $"Display {i + 1} ({m.Width}×{m.Height})";
            list[i] = m with { Index = i, Label = label };
        }
        return list;
    }
}
