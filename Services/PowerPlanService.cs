using System.Runtime.InteropServices;

namespace NotchWin.Services;

public enum PowerPlanMode
{
    Battery,
    Balanced,
    Performance
}

/// <summary>
/// Native Win32 powrprof.dll integration for reading & switching Windows Power Plans
/// (Power Saver / Balanced / High Performance) with zero PowerShell or powercfg spawns.
/// </summary>
public static class PowerPlanService
{
    private static readonly Guid GuidPowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid GuidBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid GuidHighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static PowerPlanMode GetActive()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) == 0 && ptr != IntPtr.Zero)
            {
                try
                {
                    Guid g = Marshal.PtrToStructure<Guid>(ptr);
                    if (g == GuidPowerSaver) return PowerPlanMode.Battery;
                    if (g == GuidHighPerformance) return PowerPlanMode.Performance;
                    return PowerPlanMode.Balanced;
                }
                finally { LocalFree(ptr); }
            }
        }
        catch { }
        return PowerPlanMode.Balanced;
    }

    public static bool SetActive(PowerPlanMode mode)
    {
        try
        {
            Guid target = mode switch
            {
                PowerPlanMode.Battery => GuidPowerSaver,
                PowerPlanMode.Performance => GuidHighPerformance,
                _ => GuidBalanced
            };
            return PowerSetActiveScheme(IntPtr.Zero, ref target) == 0;
        }
        catch { return false; }
    }
}
