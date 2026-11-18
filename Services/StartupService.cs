using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace NotchWin.Services;

/// <summary>
/// Microsoft Store compliant Startup manager:
/// - When packaged as MSIX (Microsoft Store), uses Windows.ApplicationModel.StartupTask ("PillStartupTask").
/// - When running unpackaged (standalone Pill.exe), falls back to HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Pill";
    private const string LegacyValueName = "NotchWin";
    private const string StartupTaskId = "PillStartupTask";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    public static bool IsPackaged()
    {
        try
        {
            int len = 0;
            // ERROR_INSUFFICIENT_BUFFER (122) means we have a valid MSIX package identity.
            return GetCurrentPackageFullName(ref len, IntPtr.Zero) == 122;
        }
        catch { return false; }
    }

    public static bool IsEnabled()
    {
        if (IsPackaged())
        {
            try
            {
                var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        if (IsPackaged())
        {
            _ = SetPackagedStartupAsync(enabled);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            key.DeleteValue(LegacyValueName, false);
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }

    private static async Task SetPackagedStartupAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                if (task.State == StartupTaskState.Disabled)
                    await task.RequestEnableAsync();
            }
            else
            {
                task.Disable();
            }
        }
        catch { }
    }
}
