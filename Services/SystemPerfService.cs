using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NotchWin.Services;

public sealed record PerfSnapshot(
    int CpuPercent,
    int RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double NetDownBytesPerSec,
    double NetUpBytesPerSec,
    IReadOnlyList<int> CpuHistory,
    IReadOnlyList<int> RamHistory
);

/// <summary>
/// Ultra-lightweight Win32 CPU, RAM &amp; Network throughput reader (zero WMI/PowerShell).
/// Only sampled while Widgets, Game Strip, or SystemStats resting face is visible (0% idle CPU).
/// </summary>
public sealed class SystemPerfService
{
    private const int HistoryLen = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrev;
    private long _prevRx, _prevTx;
    private DateTime _prevNetAt = DateTime.MinValue;
    private readonly List<int> _cpuHist = new(HistoryLen);
    private readonly List<int> _ramHist = new(HistoryLen);

    public PerfSnapshot Sample()
    {
        int cpu = 0;
        if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            ulong idle = ToUInt64(idleFt);
            ulong kernel = ToUInt64(kernelFt);
            ulong user = ToUInt64(userFt);
            if (_hasPrev)
            {
                ulong dIdle = idle - _prevIdle;
                ulong dKernel = kernel - _prevKernel;
                ulong dUser = user - _prevUser;
                ulong total = dKernel + dUser;
                if (total > 0)
                    cpu = (int)Math.Clamp(Math.Round((total - dIdle) * 100.0 / total), 0, 100);
            }
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
            _hasPrev = true;
        }

        int ramPct = 0;
        double usedGb = 0, totalGb = 0;
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramPct = (int)mem.dwMemoryLoad;
            totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
        }

        double downBps = 0, upBps = 0;
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
            var now = DateTime.UtcNow;
            if (_prevNetAt != DateTime.MinValue)
            {
                double secs = (now - _prevNetAt).TotalSeconds;
                if (secs > 0.15)
                {
                    downBps = Math.Max(0, (rx - _prevRx) / secs);
                    upBps = Math.Max(0, (tx - _prevTx) / secs);
                }
            }
            _prevRx = rx; _prevTx = tx; _prevNetAt = now;
        }
        catch { }

        _cpuHist.Add(cpu); if (_cpuHist.Count > HistoryLen) _cpuHist.RemoveAt(0);
        _ramHist.Add(ramPct); if (_ramHist.Count > HistoryLen) _ramHist.RemoveAt(0);

        return new PerfSnapshot(cpu, ramPct, usedGb, totalGb, downBps, upBps, _cpuHist.ToArray(), _ramHist.ToArray());
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{(int)bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
}
