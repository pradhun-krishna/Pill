using System.Runtime.InteropServices;

namespace NotchWin.Services;

/// <summary>
/// Native Windows Core Audio COM integration for instant Master Volume control,
/// real-time Volume HUD notifications (zero polling), and Global Mic Mute (Ctrl+Alt+M).
/// </summary>
public sealed class AudioService : IDisposable
{
    private const int eRender = 0;
    private const int eCapture = 1;
    private const int eMultimedia = 1;
    private const int eCommunications = 2;
    private const uint CLSCTX_ALL = 23;

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        [MarshalAs(UnmanagedType.Bool)] public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
        public float afChannelVolumes;
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotifyData);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, IntPtr key);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public IntPtr p2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig]
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    private sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly Action<float, bool, Guid> _onNotify;
        public VolumeCallback(Action<float, bool, Guid> onNotify) => _onNotify = onNotify;

        public int OnNotify(IntPtr pNotifyData)
        {
            try
            {
                if (pNotifyData != IntPtr.Zero)
                {
                    var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotifyData);
                    _onNotify(data.fMasterVolume, data.bMuted, data.guidEventContext);
                }
            }
            catch { }
            return 0;
        }
    }

    private sealed class DeviceNotifyClient : IMMNotificationClient
    {
        private readonly Action _onDefaultRenderChanged;
        public DeviceNotifyClient(Action onDefaultRenderChanged) => _onDefaultRenderChanged = onDefaultRenderChanged;
        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;
        public int OnDeviceAdded(string pwstrDeviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            if (flow == eRender && role == eMultimedia)
                _onDefaultRenderChanged();
            return 0;
        }
        public int OnPropertyValueChanged(string pwstrDeviceId, IntPtr key) => 0;
    }

    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid SelfEventContext = new("91C73E11-5B44-4A3A-A14E-6E39051290AB");

    private readonly object _watchLock = new();
    private IMMDeviceEnumerator? _watchedEnumerator;
    private DeviceNotifyClient? _deviceNotify;
    private IAudioEndpointVolume? _watchedRender;
    private VolumeCallback? _renderCallback;

    public event Action<float, bool>? ExternalVolumeChanged;
    public event Action<string?>? DefaultOutputChanged;

    public string? GetDefaultOutputDeviceName()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IPropertyStore? store = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0 || device == null)
                return null;
            if (device.OpenPropertyStore(0, out store) != 0 || store == null)
                return null;
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0)
                return null;
            try
            {
                if (pv.vt == 31 && pv.p != IntPtr.Zero) // VT_LPWSTR
                    return Marshal.PtrToStringUni(pv.p);
                return null;
            }
            finally { PropVariantClear(ref pv); }
        }
        catch { return null; }
        finally
        {
            if (store != null) Marshal.ReleaseComObject(store);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public void StartVolumeWatch()
    {
        lock (_watchLock)
        {
            if (_watchedEnumerator == null)
            {
                try
                {
                    _watchedEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                    _deviceNotify = new DeviceNotifyClient(() =>
                    {
                        _ = Task.Run(() =>
                        {
                            ReattachRenderWatch();
                            string? name = GetDefaultOutputDeviceName();
                            DefaultOutputChanged?.Invoke(name);
                        });
                    });
                    _watchedEnumerator.RegisterEndpointNotificationCallback(_deviceNotify);
                }
                catch { }
            }
        }
        ReattachRenderWatch();
    }

    private void ReattachRenderWatch()
    {
        lock (_watchLock)
        {
            if (_watchedRender != null)
            {
                try
                {
                    if (_renderCallback != null)
                        _watchedRender.UnregisterControlChangeNotify(_renderCallback);
                    Marshal.ReleaseComObject(_watchedRender);
                }
                catch { }
                _watchedRender = null;
                _renderCallback = null;
            }

            try
            {
                _watchedRender = GetEndpoint(eRender, eMultimedia);
                if (_watchedRender == null) return;
                _renderCallback = new VolumeCallback((vol, muted, ctx) =>
                {
                    if (ctx == SelfEventContext) return;
                    ExternalVolumeChanged?.Invoke(vol, muted);
                });
                _watchedRender.RegisterControlChangeNotify(_renderCallback);
            }
            catch { }
        }
    }

    private static IAudioEndpointVolume? GetEndpoint(int dataFlow, int role)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, role, out device) != 0 || device == null)
                return null;
            Guid iid = IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object obj) != 0)
                return null;
            return obj as IAudioEndpointVolume;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public float GetMasterVolume()
    {
        var ep = GetEndpoint(eRender, eMultimedia);
        if (ep == null) return 0.5f;
        try
        {
            ep.GetMasterVolumeLevelScalar(out float level);
            return Math.Clamp(level, 0f, 1f);
        }
        catch { return 0.5f; }
        finally { Marshal.ReleaseComObject(ep); }
    }

    public bool GetMasterMute()
    {
        var ep = GetEndpoint(eRender, eMultimedia);
        if (ep == null) return false;
        try
        {
            ep.GetMute(out bool muted);
            return muted;
        }
        catch { return false; }
        finally { Marshal.ReleaseComObject(ep); }
    }

    public void SetMasterVolume(float scalar)
    {
        var ep = GetEndpoint(eRender, eMultimedia);
        if (ep == null) return;
        try
        {
            Guid ctx = SelfEventContext;
            float clamped = Math.Clamp(scalar, 0f, 1f);
            ep.SetMasterVolumeLevelScalar(clamped, ref ctx);
            if (clamped > 0.005f) ep.SetMute(false, ref ctx);
        }
        catch { }
        finally { Marshal.ReleaseComObject(ep); }
    }

    public bool ToggleMasterMute()
    {
        var ep = GetEndpoint(eRender, eMultimedia);
        if (ep == null) return false;
        try
        {
            ep.GetMute(out bool muted);
            Guid ctx = SelfEventContext;
            ep.SetMute(!muted, ref ctx);
            return !muted;
        }
        catch { return false; }
        finally { Marshal.ReleaseComObject(ep); }
    }

    public bool GetMicMuted()
    {
        var ep = GetEndpoint(eCapture, eCommunications) ?? GetEndpoint(eCapture, eMultimedia);
        if (ep == null) return false;
        try
        {
            ep.GetMute(out bool muted);
            return muted;
        }
        catch { return false; }
        finally { Marshal.ReleaseComObject(ep); }
    }

    public bool ToggleMicMute()
    {
        bool next = !GetMicMuted();
        SetMicMuted(next);
        return next;
    }

    public void SetMicMuted(bool muted)
    {
        foreach (int role in new[] { eCommunications, eMultimedia })
        {
            var ep = GetEndpoint(eCapture, role);
            if (ep == null) continue;
            try
            {
                Guid ctx = SelfEventContext;
                ep.SetMute(muted, ref ctx);
            }
            catch { }
            finally { Marshal.ReleaseComObject(ep); }
        }
    }

    public void Dispose()
    {
        lock (_watchLock)
        {
            if (_watchedRender != null)
            {
                try
                {
                    if (_renderCallback != null)
                        _watchedRender.UnregisterControlChangeNotify(_renderCallback);
                    Marshal.ReleaseComObject(_watchedRender);
                }
                catch { }
                _watchedRender = null;
                _renderCallback = null;
            }
            if (_watchedEnumerator != null)
            {
                try
                {
                    if (_deviceNotify != null)
                        _watchedEnumerator.UnregisterEndpointNotificationCallback(_deviceNotify);
                    Marshal.ReleaseComObject(_watchedEnumerator);
                }
                catch { }
                _watchedEnumerator = null;
                _deviceNotify = null;
            }
        }
    }
}
