using System.Windows.Threading;
using Microsoft.Win32;
using NotchWin.Core;

namespace NotchWin.Services;

public sealed record PrivacyBatterySnapshot(
    bool HasBattery,
    int BatteryPercent,
    bool IsCharging,
    bool MicInUse,
    bool CameraInUse,
    string? ActiveMicApp,
    string? ActiveCameraApp,
    DateTime? MicActiveSince
);

/// <summary>
/// Lightweight native Registry ConsentStore + Win32 GetSystemPowerStatus monitor,
/// including live Mic Call Timer tracking (0% CPU between 2s checks).
/// </summary>
public sealed class PrivacyBatteryService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private PrivacyBatterySnapshot _last = new(false, 100, true, false, false, null, null, null);
    private DateTime? _micStartedAt;

    public PrivacyBatterySnapshot Current => _last;
    public event Action<PrivacyBatterySnapshot>? Changed;

    public PrivacyBatteryService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Poll()
    {
        bool hasBat = false;
        int pct = 100;
        bool charging = true;

        if (NativeMethods.GetSystemPowerStatus(out var ps) && (ps.BatteryFlag & 128) == 0 && ps.BatteryLifePercent != 255)
        {
            hasBat = true;
            pct = ps.BatteryLifePercent;
            charging = ps.ACLineStatus == 1 || (ps.BatteryFlag & 8) != 0;
        }

        string? micApp = FindActiveConsentApp("microphone");
        string? camApp = FindActiveConsentApp("webcam");

        if (micApp != null) _micStartedAt ??= DateTime.UtcNow;
        else _micStartedAt = null;

        var next = new PrivacyBatterySnapshot(
            hasBat, pct, charging,
            micApp != null, camApp != null,
            micApp, camApp,
            _micStartedAt);

        if (next != _last)
        {
            _last = next;
            Changed?.Invoke(next);
        }
    }

    private static string? FindActiveConsentApp(string device)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + device);
            return root == null ? null : Scan(root);
        }
        catch { return null; }
    }

    private static string? Scan(RegistryKey key)
    {
        foreach (string subName in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null) continue;
                if (string.Equals(subName, "NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    var nested = Scan(sub);
                    if (nested != null) return nested;
                    continue;
                }
                object? stopObj = sub.GetValue("LastUsedTimeStop");
                object? startObj = sub.GetValue("LastUsedTimeStart");
                if (stopObj != null && startObj != null &&
                    Convert.ToInt64(stopObj) == 0 && Convert.ToInt64(startObj) > 0)
                {
                    return CleanAppName(subName);
                }
            }
            catch { }
        }
        return null;
    }

    private static string CleanAppName(string key)
    {
        string leaf = key.Split('#').LastOrDefault() ?? key;
        if (leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            leaf = leaf[..^4];
            return leaf.Length > 0 ? char.ToUpperInvariant(leaf[0]) + leaf[1..] : leaf;
        }
        string family = leaf.Split('_')[0];
        return family.Split('.').LastOrDefault() ?? family;
    }

    public void Dispose() => _timer.Stop();
}
