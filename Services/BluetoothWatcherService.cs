using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace NotchWin.Services;

public sealed record BluetoothDeviceEvent(
    string DeviceName,
    bool Connected,
    int? BatteryPercent,
    bool IsAudioDevice
);

/// <summary>
/// Dual-layer native WinRT Bluetooth watcher (0% idle CPU):
/// 1) Official paired Classic BluetoothDevice.ConnectionStatusChanged events (instant connect/disconnect).
/// 2) Official paired Classic &amp; BLE DeviceWatchers with System.Devices.Aep.IsConnected &amp; BatteryPercentage.
/// Suppresses startup spam until initial load finishes and deduplicates dual-mode (Classic + LE) devices.
/// </summary>
public sealed class BluetoothWatcherService : IDisposable
{
    private const string PropIsConnected = "System.Devices.Aep.IsConnected";
    private const string PropBattery = "System.Devices.BatteryPercentage";
    private const string PropCategory = "System.Devices.Aep.Category";

    private sealed class TrackedEntry
    {
        public string Name = "";
        public bool Connected;
        public int? BatteryPercent;
        public bool IsAudio;
        public BluetoothDevice? ClassicDevice;
        public BluetoothLEDevice? BleDevice;
    }

    private readonly ConcurrentDictionary<string, TrackedEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (bool State, DateTime At)> _recentByName = new(StringComparer.OrdinalIgnoreCase);
    private DeviceWatcher? _classicWatcher;
    private DeviceWatcher? _bleWatcher;
    private volatile bool _classicReady;
    private volatile bool _bleReady;

    public DateTime LastEventUtc { get; private set; } = DateTime.MinValue;
    public event Action<BluetoothDeviceEvent>? DeviceConnectionChanged;

    public void Start()
    {
        if (_classicWatcher != null || _bleWatcher != null) return;
        string[] props = { PropIsConnected, PropBattery, PropCategory };

        try
        {
            string classicAqs = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            _classicWatcher = DeviceInformation.CreateWatcher(classicAqs, props);
            _classicWatcher.Added += (_, info) => OnDeviceAdded(info, isBle: false);
            _classicWatcher.Updated += (_, upd) => OnDeviceUpdated(upd, isBle: false);
            _classicWatcher.Removed += (_, upd) => OnDeviceRemoved(upd.Id);
            _classicWatcher.EnumerationCompleted += (_, _) => _classicReady = true;
            _classicWatcher.Start();
        }
        catch { _classicReady = true; }

        try
        {
            string bleAqs = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            _bleWatcher = DeviceInformation.CreateWatcher(bleAqs, props);
            _bleWatcher.Added += (_, info) => OnDeviceAdded(info, isBle: true);
            _bleWatcher.Updated += (_, upd) => OnDeviceUpdated(upd, isBle: true);
            _bleWatcher.Removed += (_, upd) => OnDeviceRemoved(upd.Id);
            _bleWatcher.EnumerationCompleted += (_, _) => _bleReady = true;
            _bleWatcher.Start();
        }
        catch { _bleReady = true; }
    }

    private void OnDeviceAdded(DeviceInformation info, bool isBle)
    {
        try
        {
            string name = info.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            bool initialConnected = ReadBool(info.Properties, PropIsConnected);
            var entry = new TrackedEntry
            {
                Name = name,
                Connected = initialConnected,
                BatteryPercent = ReadBattery(info.Properties),
                IsAudio = DetectAudio(name, info.Properties)
            };
            _entries[info.Id] = entry;

            bool ready = isBle ? _bleReady : _classicReady;
            if (ready && initialConnected)
                EmitDebounced(entry);

            // Attach native WinRT ConnectionStatusChanged directly to the device object
            // so connect/disconnect fires instantaneously even if Aep.IsConnected lags.
            _ = AttachNativeDeviceAsync(info.Id, entry, isBle);
        }
        catch { }
    }

    private async Task AttachNativeDeviceAsync(string id, TrackedEntry entry, bool isBle)
    {
        try
        {
            if (!isBle)
            {
                var dev = await BluetoothDevice.FromIdAsync(id);
                if (dev == null) return;
                entry.ClassicDevice = dev;
                entry.Connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (!string.IsNullOrWhiteSpace(dev.Name)) entry.Name = dev.Name.Trim();
                dev.ConnectionStatusChanged += OnClassicStatusChanged;
            }
            else
            {
                var ble = await BluetoothLEDevice.FromIdAsync(id);
                if (ble == null) return;
                entry.BleDevice = ble;
                entry.Connected = ble.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (!string.IsNullOrWhiteSpace(ble.Name)) entry.Name = ble.Name.Trim();
                ble.ConnectionStatusChanged += OnBleStatusChanged;
            }
        }
        catch { }
    }

    private void OnClassicStatusChanged(BluetoothDevice sender, object args)
    {
        try
        {
            if (!_entries.TryGetValue(sender.DeviceId, out var entry)) return;
            bool nowConn = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            if (entry.Connected == nowConn) return;
            entry.Connected = nowConn;
            if (_classicReady) EmitDebounced(entry);
        }
        catch { }
    }

    private void OnBleStatusChanged(BluetoothLEDevice sender, object args)
    {
        try
        {
            if (!_entries.TryGetValue(sender.DeviceId, out var entry)) return;
            bool nowConn = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            if (entry.Connected == nowConn) return;
            entry.Connected = nowConn;
            if (_bleReady) EmitDebounced(entry);
        }
        catch { }
    }

    private void OnDeviceUpdated(DeviceInformationUpdate update, bool isBle)
    {
        try
        {
            if (!_entries.TryGetValue(update.Id, out var entry)) return;
            int? bat = ReadBattery(update.Properties);
            if (bat.HasValue) entry.BatteryPercent = bat;

            if (update.Properties.TryGetValue(PropIsConnected, out var obj) && obj is bool nowConn)
            {
                if (entry.Connected != nowConn)
                {
                    entry.Connected = nowConn;
                    bool ready = isBle ? _bleReady : _classicReady;
                    if (ready) EmitDebounced(entry);
                }
            }
        }
        catch { }
    }

    private void OnDeviceRemoved(string id)
    {
        if (_entries.TryRemove(id, out var entry))
        {
            DisposeEntry(entry);
        }
    }

    private void EmitDebounced(TrackedEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name)) return;
        var now = DateTime.UtcNow;
        if (_recentByName.TryGetValue(entry.Name, out var prev) &&
            prev.State == entry.Connected &&
            (now - prev.At).TotalSeconds < 3.5)
        {
            return;
        }
        _recentByName[entry.Name] = (entry.Connected, now);
        LastEventUtc = now;

        DeviceConnectionChanged?.Invoke(new BluetoothDeviceEvent(
            entry.Name,
            entry.Connected,
            entry.BatteryPercent,
            entry.IsAudio));
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var val) && val is bool b && b;

    private static int? ReadBattery(IReadOnlyDictionary<string, object> props)
    {
        if (!props.TryGetValue(PropBattery, out var val) || val == null) return null;
        try
        {
            int pct = Convert.ToInt32(val);
            return pct is >= 0 and <= 100 ? pct : null;
        }
        catch { return null; }
    }

    private static bool DetectAudio(string name, IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue(PropCategory, out var catObj) && catObj is string[] cats)
        {
            foreach (var c in cats)
            {
                if (c.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("Headphone", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("Speaker", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        string lower = name.ToLowerInvariant();
        return lower.Contains("bud") || lower.Contains("pod") || lower.Contains("head") ||
               lower.Contains("ear") || lower.Contains("speaker") || lower.Contains("audio") ||
               lower.Contains("jbl") || lower.Contains("bose") || lower.Contains("sony") ||
               lower.Contains("wh-") || lower.Contains("wf-") || lower.Contains("airdopes") ||
               lower.Contains("rockerz") || lower.Contains("nirvana") || lower.Contains("oneplus") ||
               lower.Contains("nothing") || lower.Contains("beats") || lower.Contains("boat") ||
               lower.Contains("realme") || lower.Contains("oppo") || lower.Contains("sennheiser");
    }

    private void DisposeEntry(TrackedEntry entry)
    {
        try
        {
            if (entry.ClassicDevice != null)
            {
                entry.ClassicDevice.ConnectionStatusChanged -= OnClassicStatusChanged;
                entry.ClassicDevice.Dispose();
                entry.ClassicDevice = null;
            }
            if (entry.BleDevice != null)
            {
                entry.BleDevice.ConnectionStatusChanged -= OnBleStatusChanged;
                entry.BleDevice.Dispose();
                entry.BleDevice = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            if (_classicWatcher != null)
            {
                if (_classicWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    _classicWatcher.Stop();
                _classicWatcher = null;
            }
            if (_bleWatcher != null)
            {
                if (_bleWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    _bleWatcher.Stop();
                _bleWatcher = null;
            }
        }
        catch { }

        foreach (var kv in _entries) DisposeEntry(kv.Value);
        _entries.Clear();
    }
}
