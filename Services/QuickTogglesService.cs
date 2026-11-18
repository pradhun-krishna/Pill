using Windows.Devices.Radios;

namespace NotchWin.Services;

public sealed record RadioStatus(bool WifiAvailable, bool WifiOn, bool BluetoothAvailable, bool BluetoothOn);

/// <summary>
/// Native WinRT Windows.Devices.Radios integration for instant Wi-Fi and
/// Bluetooth toggling directly from the notch. Zero PowerShell, zero netsh.
/// </summary>
public sealed class QuickTogglesService
{
    public async Task<RadioStatus> GetStatusAsync()
    {
        try
        {
            await Radio.RequestAccessAsync();
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            return new RadioStatus(
                wifi != null, wifi?.State == RadioState.On,
                bt != null, bt?.State == RadioState.On);
        }
        catch
        {
            return new RadioStatus(false, false, false, false);
        }
    }

    public async Task<bool> ToggleRadioAsync(RadioKind kind)
    {
        try
        {
            await Radio.RequestAccessAsync();
            var radios = await Radio.GetRadiosAsync();
            var target = radios.FirstOrDefault(r => r.Kind == kind);
            if (target == null) return false;
            var next = target.State == RadioState.On ? RadioState.Off : RadioState.On;
            var res = await target.SetStateAsync(next);
            return res == RadioAccessStatus.Allowed && next == RadioState.On;
        }
        catch
        {
            return false;
        }
    }
}
