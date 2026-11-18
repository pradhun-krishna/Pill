using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NotchWin.Core;
using NotchWin.Services;
using Windows.Devices.Radios;

namespace NotchWin.UI;

public partial class NotchWindow : Window
{
    private enum ActiveTab { Media, Sites, Widgets, Controls, Clipboard, Apps }

    private const int HotkeyMicMuteId = 9001;
    private const int HotkeyToggleNotchId = 9002;
    private const int HotkeyAppBaseId = 9101;
    private const uint TrayIconId = 1001;
    private const double CanvasCenterX = 330.0;

    private readonly AppSettings _settings;
    private readonly AudioService _audio = new();
    private readonly BrightnessService _brightness = new();
    private readonly LyricsService _lyrics = new();
    private readonly ClipboardService _clipboard = new();
    private readonly QuickTogglesService _toggles = new();
    private readonly LauncherService _launcher = new();
    private readonly SitesService _sites = new();
    private readonly SystemPerfService _perf = new();
    private readonly BluetoothWatcherService _bluetooth = new();
    private readonly MediaService _media;
    private readonly WeatherService _weather;
    private readonly PrivacyBatteryService _privacy;
    private readonly FocusNotesService _focus;
    private WindowMonitorService? _windowMonitor;
    private SettingsWindow? _settingsWindow;

    private readonly SpringGroup _springs;
    private readonly SpringValue _wSpring;
    private readonly SpringValue _hSpring;
    private readonly SpringValue _ySpring;
    private readonly SpringValue _rSpring;
    private readonly SpringValue _earSpring;
    private readonly SpringValue _expandOpacity;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _hoverIntentTimer;
    private readonly DispatcherTimer _hoverWatchTimer;
    private readonly DispatcherTimer _edgeProbeTimer;
    private readonly DispatcherTimer _brightnessCheckTimer;
    private readonly DispatcherTimer _eqTimer;
    private readonly DispatcherTimer _timelineTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _perfTimer;
    private readonly DispatcherTimer _callTimer;
    private readonly DispatcherTimer _memTrimTimer;

    private IntPtr _hwnd;
    private IntPtr _trayIconHandle;
    private bool _trayAdded;
    private bool _isHovered;
    private bool _isPinned;
    private bool _isWindowSlim;
    private bool _isTrueFullscreen;
    private bool _toastActive;
    private bool _toastIsTrackPeek;
    private double _toastWidth = 286;
    private double _toastHeight = 38;
    private string _lastAudioOutputName = "";
    private bool _lyricsOpen;
    private bool _isTypingNote;
    private bool _isTypingSite;
    private bool _widgetsAltPage;
    private DateTime? _leaveStartedAt;
    private DateTime? _edgeEnteredAt;
    private bool _hasInitialPowerState;
    private bool _lastChargingState;
    private bool _lowBatNotified20;
    private bool _lowBatNotified10;
    private int _lastKnownBrightness = -1;
    private string _lastNotifiedTrackKey = "";
    private bool _mediaBootstrapped;
    private ActiveTab _activeTab = ActiveTab.Media;
    private string _clipFilter = "All";

    private MediaSnapshot? _currentMedia;
    private LyricsResult? _currentLyrics;
    private string _lyricsTrackKey = "";
    private CancellationTokenSource? _lyricsCts;
    private WeatherSnapshot? _currentWeather;
    private PrivacyBatterySnapshot _currentPrivacy = new(false, 100, true, false, false, null, null, null);
    private RadioStatus _radioStatus = new(false, false, false, false);
    private PowerPlanMode _powerPlan = PowerPlanMode.Balanced;

    private bool _isMicMuted;
    private bool _isVolMuted;
    private bool _draggingVol;
    private bool _draggingMiniVol;
    private bool _draggingBright;
    private double _eqPhase;
    private double _wavePhase;

    private bool IsOpen => _isHovered || _isPinned || _isTypingNote || _isTypingSite;
    private bool ShowGameStrip => _settings.GameStripOnFullscreen && _isTrueFullscreen && !IsOpen && !_toastActive;
    private bool IsSlim => _settings.SlimOnFullscreenOrMaximized && _isWindowSlim && !ShowGameStrip && !IsOpen && !_toastActive;

    public NotchWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _settings.LaunchOnStartup = StartupService.IsEnabled();

        // Bind crisp monochrome vector icons.
        NavMediaIco.Data = Icons.MusicNote;
        NavSitesIco.Data = Icons.Globe;
        NavWidgetsIco.Data = Icons.Grid;
        NavControlsIco.Data = Icons.Sliders;
        NavClipIco.Data = Icons.Clipboard;
        NavAppsIco.Data = Icons.AppsDots;
        PinIcon.Data = Icons.Pin;
        SettingsGearIco.Data = Icons.Settings;
        ThemePaletteIco.Data = Icons.Palette;

        PrevIcon.Data = Icons.SkipBack;
        NextIcon.Data = Icons.SkipForward;
        PlayPauseIcon.Data = Icons.Play;
        ArtPlaceholderPath.Data = Icons.MusicNote;
        SourceMiniIcon.Data = Icons.MusicNote;
        LyricsBtnIcon.Data = Icons.Lyrics;
        QuickMicIcon.Data = Icons.Mic;
        QuickVolIcon.Data = Icons.Volume;

        WorldClockGlobe.Data = Icons.Globe;
        TodoPlusIcon.Data = Icons.Plus;
        WeatherCardIcon.Data = Icons.Cloud;
        CollapsedWeatherIcon.Data = Icons.Cloud;
        FocusResetIcon.Data = Icons.RotateCcw;
        MiniFocusPlayIcon.Data = Icons.Play;
        FocusMinusIcon.Data = Icons.Minus;
        FocusPlusIcon.Data = Icons.Plus;
        WidgetFlipIcon.Data = Icons.Layers;
        CaptureShotIcon.Data = Icons.Camera;
        CaptureSnipIcon.Data = Icons.Scissors;

        BrightSliderIcon.Data = Icons.Sun;
        VolSliderIcon.Data = Icons.Volume;
        WifiIcon.Data = Icons.Wifi;
        BtIcon.Data = Icons.Bluetooth;
        CtrlMicIcon.Data = Icons.Mic;
        CtrlFocusIcon.Data = Icons.Timer;
        CtrlMuteIcon.Data = Icons.Volume;
        CtrlBatIcon.Data = Icons.Battery;
        CollapsedMicMutedPath.Data = Icons.MicOff;

        _media = new MediaService(Dispatcher);
        _media.MediaChanged += OnMediaChanged;

        _weather = new WeatherService(Dispatcher);
        _weather.Changed += OnWeatherChanged;

        _privacy = new PrivacyBatteryService(Dispatcher);
        _privacy.Changed += OnPrivacyChanged;

        _focus = new FocusNotesService(Dispatcher);
        _focus.TimerChanged += UpdateFocusUi;
        _focus.TimerCompleted += OnFocusCompleted;
        _focus.NotesChanged += RebuildNotesList;

        _clipboard.Changed += () => Dispatcher.BeginInvoke(RebuildClipboardList);
        _clipboard.ItemCopied += item => Dispatcher.BeginInvoke(() => OnClipboardItemCopied(item));
        _launcher.Changed += () => Dispatcher.BeginInvoke(RebuildAppsGrid);
        _sites.Changed += () => Dispatcher.BeginInvoke(RebuildSitesGrid);

        _audio.ExternalVolumeChanged += (vol, muted) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isVolMuted = muted;
                UpdateVolumeVisuals(muted ? 0f : vol);
                if (_settings.ShowVolumeHud && !IsOpen) ShowVolumeHud(vol, muted);
            });
        };

        _audio.DefaultOutputChanged += deviceName => Dispatcher.BeginInvoke(() => OnDefaultAudioOutputChanged(deviceName));
        _bluetooth.DeviceConnectionChanged += ev => Dispatcher.BeginInvoke(() => OnBluetoothDeviceChanged(ev));

        _springs = new SpringGroup(RenderSpringFrame);
        _wSpring = _springs.Add(244, 420, 32, 0.15);
        _hSpring = _springs.Add(32, 420, 32, 0.15);
        _ySpring = _springs.Add(_settings.Shape == NotchShape.Island ? _settings.IslandTopMargin : 0, 420, 32, 0.1);
        _rSpring = _springs.Add(14, 420, 32, 0.1);
        _earSpring = _springs.Add(_settings.Shape == NotchShape.Notch ? 9 : 0, 420, 32, 0.1);
        _expandOpacity = _springs.Add(0, 380, 32, 0.01);

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _clockTimer.Tick += (_, _) => UpdateClock();

        _hoverIntentTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
        _hoverIntentTimer.Tick += (_, _) => { _hoverIntentTimer.Stop(); OpenFromHover(); };

        _hoverWatchTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(45) };
        _hoverWatchTimer.Tick += HoverWatch_Tick;

        // Global top-edge cursor probe so hovering opens NotchWin cleanly EVEN when another
        // topmost overlay (e.g. Lenovo/Motorola Smart Connect) sits on top of our slim bar.
        _edgeProbeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(85) };
        _edgeProbeTimer.Tick += EdgeProbe_Tick;

        _brightnessCheckTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(140) };
        _brightnessCheckTimer.Tick += BrightnessCheck_Tick;

        _eqTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(90) };
        _eqTimer.Tick += EqTimer_Tick;

        _timelineTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(160) };
        _timelineTimer.Tick += (_, _) => UpdateTimelineAndLyricsUi();

        _toastTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
        _toastTimer.Tick += (_, _) => HideToast();

        _perfTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        _perfTimer.Tick += (_, _) => UpdatePerfDisplay();

        _callTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _callTimer.Tick += (_, _) => UpdateCallTimerText();

        _memTrimTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2.5) };
        _memTrimTimer.Tick += (_, _) => { _memTrimTimer.Stop(); if (!IsOpen) NativeMethods.TrimWorkingSet(); };
        _memTrimTimer.Start();

        NoteInput.TextChanged += (_, _) =>
        {
            NotePlaceholder.Visibility = string.IsNullOrEmpty(NoteInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        };
        SiteNameInput.TextChanged += (_, _) =>
        {
            SiteNamePlaceholder.Visibility = string.IsNullOrEmpty(SiteNameInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        };
        SiteUrlInput.TextChanged += (_, _) =>
        {
            SiteUrlPlaceholder.Visibility = string.IsNullOrEmpty(SiteUrlInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        };

        _powerPlan = PowerPlanService.GetActive();
        UpdateClock();
        ApplyTheme();
        SyncAudioUi();
        SyncBrightnessUi();
        UpdatePowerPlanUi();
        UpdateFocusUi();
        RebuildNotesList();
        RebuildClipboardList();
        RebuildAppsGrid();
        RebuildSitesGrid();
        UpdateTabUi(animate: false);
        UpdateWavyScrubBar(0);
        RenderSpringFrame();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        PositionOnSelectedMonitor();
        SetNoActivate(true);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        NativeMethods.RegisterHotKey(_hwnd, HotkeyMicMuteId, mods, 0x4D);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyToggleNotchId, mods, 0x4E);
        for (int i = 0; i < 6; i++)
            NativeMethods.RegisterHotKey(_hwnd, HotkeyAppBaseId + i, mods, (uint)(0x31 + i));

        NativeMethods.AddClipboardFormatListener(_hwnd);
        RegisterRawKeyboardInput();
        RegisterTrayIcon();

        _windowMonitor = new WindowMonitorService(_hwnd, Dispatcher);
        _windowMonitor.SlimStateChanged += OnSlimStateChanged;
        _windowMonitor.Start();

        if (_brightness.TryGetBrightness(out int initBright)) _lastKnownBrightness = initBright;
        _privacy.Start();
        _weather.Start();
        _lastAudioOutputName = _audio.GetDefaultOutputDeviceName() ?? "";
        _audio.StartVolumeWatch();
        _bluetooth.Start();
        _ = _media.StartAsync();
        _ = RefreshRadiosAsync();
        _edgeProbeTimer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void BringToTopmostNoActivate(bool forceReassert = false)
    {
        if (_hwnd == IntPtr.Zero) return;
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        if (forceReassert)
        {
            // Toggle NOTOPMOST -> TOPMOST so Windows DWM promotes Pill above fullscreen apps & Smart Connect.
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags | NativeMethods.SWP_NOREDRAW);
        }
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private void RegisterRawKeyboardInput()
    {
        try
        {
            var devices = new[]
            {
                new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop Controls
                    usUsage = 0x06,     // Keyboard
                    dwFlags = NativeMethods.RIDEV_INPUTSINK,
                    hwndTarget = _hwnd
                }
            };
            NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }
        catch { }
    }

    private void SetNoActivate(bool noActivate)
    {
        if (_hwnd == IntPtr.Zero) return;
        IntPtr ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        long style = ex.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        if (noActivate) style |= NativeMethods.WS_EX_NOACTIVATE;
        else style &= ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _settingsWindow?.Close();
        _clockTimer.Stop(); _hoverIntentTimer.Stop(); _hoverWatchTimer.Stop();
        _edgeProbeTimer.Stop(); _brightnessCheckTimer.Stop();
        _eqTimer.Stop(); _timelineTimer.Stop(); _toastTimer.Stop();
        _perfTimer.Stop(); _callTimer.Stop(); _memTrimTimer.Stop();
        _lyricsCts?.Cancel();
        _springs.Dispose();
        _windowMonitor?.Dispose();
        _bluetooth.Dispose();
        _media.Dispose(); _weather.Dispose(); _privacy.Dispose(); _focus.Dispose(); _audio.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyMicMuteId);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyToggleNotchId);
            for (int i = 0; i < 6; i++) NativeMethods.UnregisterHotKey(_hwnd, HotkeyAppBaseId + i);
            RemoveTrayIcon();
        }
        base.OnClosed(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PositionOnSelectedMonitor);

    private void PositionOnSelectedMonitor()
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0) { Left = Math.Round((SystemParameters.PrimaryScreenWidth - Width) / 2.0); Top = 0; return; }
        var mon = monitors[Math.Clamp(_settings.MonitorIndex, 0, monitors.Count - 1)];
        double sx = 1.0, sy = 1.0;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null) { sx = src.CompositionTarget.TransformToDevice.M11; sy = src.CompositionTarget.TransformToDevice.M22; }
        Left = Math.Round((mon.Left / sx) + ((mon.Width / sx) - Width) / 2.0);
        Top = Math.Round(mon.Top / sy);
    }

    // ── Standalone Settings Window ──────────────────────────────────────────

    private void OpenSettingsWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, _launcher, ApplyAllSettingsLive, Close, TriggerTestNotification);
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; NativeMethods.TrimWorkingSet(); };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyAllSettingsLive()
    {
        PositionOnSelectedMonitor();
        ApplyTheme();
        UpdateCollapsedSlots();
        UpdateTargets();
    }

    // ── Win32 WndProc ───────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE) { _clipboard.OnClipboardUpdate(); handled = true; }
        else if (msg == NativeMethods.WM_POWERBROADCAST && wParam.ToInt32() == NativeMethods.PBT_APMPOWERSTATUSCHANGE)
        {
            Dispatcher.BeginInvoke(() => _privacy.Poll());
        }
        else if (msg == NativeMethods.WM_DEVICECHANGE)
        {
            int evt = wParam.ToInt32();
            if ((evt == NativeMethods.DBT_DEVICEARRIVAL || evt == NativeMethods.DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero)
                HandleDeviceChange(evt == NativeMethods.DBT_DEVICEARRIVAL, lParam);
        }
        else if (msg == NativeMethods.WM_INPUT)
        {
            HandleRawKeyboardInput(lParam);
        }
        else if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyMicMuteId) { ToggleGlobalMicMute(true); handled = true; }
            else if (id == HotkeyToggleNotchId)
            {
                _isPinned = !_isPinned;
                if (_isPinned) { BringToTopmostNoActivate(); _edgeProbeTimer.Stop(); }
                else { _isHovered = false; _edgeProbeTimer.Start(); }
                UpdatePinUi(); UpdateTargets(); handled = true;
            }
            else if (id >= HotkeyAppBaseId && id < HotkeyAppBaseId + 6)
            {
                int idx = id - HotkeyAppBaseId;
                if (idx < _launcher.Apps.Count) { var a = _launcher.Apps[idx]; LauncherService.Launch(a); ShowRichToast(Icons.Grid, a.Name, "Launching", "App", Colors.White, 1600); }
                handled = true;
            }
        }
        else if (msg == NativeMethods.WM_TRAYICON)
        {
            int m = lParam.ToInt32() & 0xFFFF;
            if (m == NativeMethods.WM_RBUTTONUP) { ShowTrayMenu(); handled = true; }
            else if (m == NativeMethods.WM_LBUTTONDBLCLK) { OpenSettingsWindow(); handled = true; }
        }
        return IntPtr.Zero;
    }

    private void HandleDeviceChange(bool arrived, IntPtr lParam)
    {
        if (!_settings.ShowDeviceHuds || IsOpen) return;
        try
        {
            var hdr = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype != NativeMethods.DBT_DEVTYP_VOLUME) return;

            var vol = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_VOLUME>(lParam);
            char letter = FirstDriveLetter(vol.dbcv_unitmask);
            if (letter == '\0') return;

            string title = $"Drive {letter}:";
            if (arrived)
            {
                try
                {
                    var di = new System.IO.DriveInfo(letter.ToString());
                    if (!string.IsNullOrWhiteSpace(di.VolumeLabel)) title = $"{di.VolumeLabel} ({letter}:)";
                }
                catch { }
                ShowRichToast(Icons.Usb, title, "USB Connected", "USB", Color.FromRgb(0x30, 0xD1, 0x58), 2600);
            }
            else
            {
                ShowRichToast(Icons.Usb, title, "USB Ejected", "", Color.FromRgb(0x9E, 0xA4, 0xB2), 2100);
            }
        }
        catch { }
    }

    private static char FirstDriveLetter(uint mask)
    {
        for (int i = 0; i < 26; i++)
            if ((mask & (1u << i)) != 0) return (char)('A' + i);
        return '\0';
    }

    private void HandleRawKeyboardInput(IntPtr hRawInput)
    {
        try
        {
            uint hdrSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            byte[] buf = new byte[64];
            uint size = (uint)buf.Length;
            uint res = NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, buf, ref size, hdrSize);
            if (res == uint.MaxValue || res < hdrSize + 8) return;

            uint dwType = BitConverter.ToUInt32(buf, 0);
            if (dwType != NativeMethods.RIM_TYPEKEYBOARD) return;

            ushort flags = BitConverter.ToUInt16(buf, (int)hdrSize + 2);
            if ((flags & NativeMethods.RI_KEY_BREAK) != 0) return; // key-down only

            ushort vk = BitConverter.ToUInt16(buf, (int)hdrSize + 6);
            if (vk == NativeMethods.VK_CAPITAL && _settings.ShowLockKeyHuds && !IsOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    bool on = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;
                    ShowRichToast(Icons.CapsLock, "Caps Lock", on ? "Uppercase active" : "Lowercase", on ? "ON" : "OFF",
                        on ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x9A, 0xA0, 0xAE), 1450);
                });
            }
            else if (vk == NativeMethods.VK_NUMLOCK && _settings.ShowLockKeyHuds && !IsOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    bool on = (NativeMethods.GetKeyState(NativeMethods.VK_NUMLOCK) & 1) != 0;
                    ShowRichToast(Icons.CapsLock, "Num Lock", on ? "Keypad numbers active" : "Navigation mode", on ? "ON" : "OFF",
                        on ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x9A, 0xA0, 0xAE), 1450);
                });
            }
            else if (_settings.ShowVolumeHud && !IsOpen && (vk == 0xFF || (vk >= 0x70 && vk <= 0x87)))
            {
                // One-shot deferred brightness check after Fn / F-key presses (0% idle CPU!).
                _brightnessCheckTimer.Stop();
                _brightnessCheckTimer.Start();
            }
        }
        catch { }
    }

    private void BrightnessCheck_Tick(object? sender, EventArgs e)
    {
        _brightnessCheckTimer.Stop();
        if (!_brightness.TryGetBrightness(out int pct)) return;
        if (_lastKnownBrightness >= 0 && pct != _lastKnownBrightness)
        {
            _lastKnownBrightness = pct;
            UpdateBrightnessVisual(pct / 100.0);
            if (_settings.ShowVolumeHud && !IsOpen)
                ShowBarHud(Icons.Sun, pct / 100.0, $"{pct}%", Color.FromRgb(0xFF, 0xD6, 0x0A));
        }
        else
        {
            _lastKnownBrightness = pct;
        }
    }

    private void RegisterTrayIcon()
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                IntPtr[] small = new IntPtr[1];
                if (NativeMethods.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    icon = small[0];
                    _trayIconHandle = icon;
                }
            }
        }
        catch { }

        if (icon == IntPtr.Zero) icon = NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(), hWnd = _hwnd, uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON, hIcon = icon,
            szTip = "Pill — Double-click for Settings"
        };
        _trayAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (_trayAdded)
        {
            var nid = new NativeMethods.NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(), hWnd = _hwnd, uID = TrayIconId };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
            _trayAdded = false;
        }
        if (_trayIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
    }

    private void ShowTrayMenu()
    {
        IntPtr menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "Open Settings…");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (_isPinned ? NativeMethods.MF_CHECKED : 0), 2, "Pin Notch Open");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (_settings.Shape == NotchShape.Island ? NativeMethods.MF_CHECKED : 0), 3, "Floating Island Mode");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (_settings.SlimOnFullscreenOrMaximized ? NativeMethods.MF_CHECKED : 0), 4, "Slim Line on Fullscreen/Maximized");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 5, "Quit Pill");

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetForegroundWindow(_hwnd);
        uint cmd = NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        switch (cmd)
        {
            case 1: OpenSettingsWindow(); break;
            case 2: _isPinned = !_isPinned; UpdatePinUi(); UpdateTargets(); break;
            case 3: _settings.Shape = _settings.Shape == NotchShape.Notch ? NotchShape.Island : NotchShape.Notch; _settings.Save(); ApplyAllSettingsLive(); break;
            case 4: _settings.SlimOnFullscreenOrMaximized = !_settings.SlimOnFullscreenOrMaximized; _settings.Save(); ApplyAllSettingsLive(); break;
            case 5: Close(); break;
        }
    }

    // ── Hover State Machine ─────────────────────────────────────────────────

    private void Shell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isHovered) return;
        if (_settings.HoverDelayMs <= 0) OpenFromHover();
        else { _hoverIntentTimer.Interval = TimeSpan.FromMilliseconds(_settings.HoverDelayMs); _hoverIntentTimer.Stop(); _hoverIntentTimer.Start(); }
    }

    private void Shell_MouseMove(object sender, MouseEventArgs e) { if (_isHovered) _leaveStartedAt = null; else if (!_hoverIntentTimer.IsEnabled) Shell_MouseEnter(sender, e); }
    private void Shell_MouseLeave(object sender, MouseEventArgs e) { if (!_isHovered && _hoverIntentTimer.IsEnabled) _hoverIntentTimer.Stop(); }

    private void OpenFromHover()
    {
        if (_isHovered) return;
        _isHovered = true;
        _leaveStartedAt = null;
        _edgeEnteredAt = null;
        _edgeProbeTimer.Stop();
        if (_toastActive) { _toastTimer.Stop(); _toastActive = false; _toastIsTrackPeek = false; ToastLayer.Visibility = Visibility.Collapsed; CollapsedMain.Visibility = Visibility.Visible; }
        BringToTopmostNoActivate(forceReassert: true);
        SyncAudioUi();
        _ = _media.RefreshAsync();
        if (_activeTab == ActiveTab.Controls) { SyncBrightnessUi(); _powerPlan = PowerPlanService.GetActive(); UpdatePowerPlanUi(); _ = RefreshRadiosAsync(); }
        if (_settings.Theme == NotchTheme.AlbumSync) ApplyAlbumSyncShell();
        UpdateTargets();
        if (!_isPinned) _hoverWatchTimer.Start();
    }

    private void EdgeProbe_Tick(object? sender, EventArgs e)
    {
        if (IsOpen) { _edgeProbeTimer.Stop(); return; }
        // Ignore while dragging a window, browser tab, or selecting text.
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) < 0 ||
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) < 0)
        {
            _edgeEnteredAt = null;
            return;
        }
        if (!NativeMethods.GetCursorPos(out var screenPt)) return;

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null) return;
        Point dip = src.CompositionTarget.TransformFromDevice.Transform(new Point(screenPt.X, screenPt.Y));

        double relX = dip.X - Left, relY = dip.Y - Top;
        double hitMaxY = _isTrueFullscreen ? 5.0
            : IsSlim ? 14.0
            : Math.Max(_ySpring.Value + _hSpring.Value + 4.0, 38.0);
        if (relY < -2.0 || relY > hitMaxY) { _edgeEnteredAt = null; return; }

        double hitHalfW = Math.Max(_wSpring.Value, 178.0) / 2.0;
        if (Math.Abs(relX - CanvasCenterX) > hitHalfW) { _edgeEnteredAt = null; return; }

        _edgeEnteredAt ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _edgeEnteredAt.Value).TotalMilliseconds >= _settings.HoverDelayMs)
        {
            _edgeEnteredAt = null;
            OpenFromHover();
        }
    }

    private void HoverWatch_Tick(object? sender, EventArgs e)
    {
        if (!_isHovered || _isPinned || _isTypingNote || _isTypingSite) { if (!_isHovered) _hoverWatchTimer.Stop(); return; }
        if (_draggingVol || _draggingBright || _draggingMiniVol) { _leaveStartedAt = null; return; }
        if (!NativeMethods.GetCursorPos(out var screenPt)) return;

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null) return;
        Point dip = src.CompositionTarget.TransformFromDevice.Transform(new Point(screenPt.X, screenPt.Y));

        double relX = dip.X - Left, relY = dip.Y - Top;
        double w = Math.Max(_wSpring.Value, _wSpring.Target), h = Math.Max(_hSpring.Value, _hSpring.Target), y = _ySpring.Value;
        double dx = Math.Max((CanvasCenterX - w / 2.0) - relX, Math.Max(0, relX - (CanvasCenterX + w / 2.0)));
        double dy = Math.Max(y - relY, Math.Max(0, relY - (y + h)));
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist <= 34.0) { _leaveStartedAt = null; return; }
        if (dist > 95.0) { CollapseFromHover(); return; }
        _leaveStartedAt ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _leaveStartedAt.Value).TotalMilliseconds >= 200) CollapseFromHover();
    }

    private void CollapseFromHover()
    {
        _isHovered = false;
        _leaveStartedAt = null;
        _edgeEnteredAt = null;
        _hoverWatchTimer.Stop();
        if (!IsOpen) _edgeProbeTimer.Start();
        if (_settings.Theme == NotchTheme.AlbumSync) ApplyAlbumSyncShell();
        UpdateTargets();
        _memTrimTimer.Interval = TimeSpan.FromSeconds(8);
        _memTrimTimer.Stop();
        _memTrimTimer.Start();
    }
    private void OnSlimStateChanged(bool slim, bool trueFullscreen)
    {
        _isWindowSlim = slim;
        _isTrueFullscreen = trueFullscreen;
        BringToTopmostNoActivate();
        UpdateTargets();
    }

    // ── Spring Targets & Unified Shell Geometry ─────────────────────────────

    private void UpdateTargets()
    {
        bool island = _settings.Shape == NotchShape.Island;
        double islandY = _settings.IslandTopMargin;

        if (IsOpen)
        {
            (double tw, double th) = _activeTab switch
            {
                ActiveTab.Media => (524, _lyricsOpen ? 280 : 200),
                ActiveTab.Sites => (524, 214),
                ActiveTab.Widgets => (536, 266),
                ActiveTab.Controls => (548, 264),
                ActiveTab.Clipboard => (516, 244),
                ActiveTab.Apps => (518, 200),
                _ => (524, 200)
            };
            _wSpring.SetTarget(tw);
            _hSpring.SetTarget(th);
            _ySpring.SetTarget(island ? islandY : 0);
            _rSpring.SetTarget(_settings.ExpandedCornerRadius);
            _earSpring.SetTarget(island ? 0 : 12);
            _expandOpacity.SetTarget(1);
        }
        else if (_toastActive)
        {
            _wSpring.SetTarget(_toastWidth);
            _hSpring.SetTarget(_toastHeight);
            _ySpring.SetTarget(island ? islandY : 0);
            _rSpring.SetTarget(island ? _toastHeight / 2.0 : Math.Min(18, _toastHeight / 2.0));
            _earSpring.SetTarget(island ? 0 : 9);
            _expandOpacity.SetTarget(0);
        }
        else if (ShowGameStrip)
        {
            _wSpring.SetTarget(174); _hSpring.SetTarget(22); _ySpring.SetTarget(island ? Math.Min(6, islandY) : 0); _rSpring.SetTarget(11); _earSpring.SetTarget(island ? 0 : 6); _expandOpacity.SetTarget(0);
        }
        else if (IsSlim)
        {
            _wSpring.SetTarget(138); _hSpring.SetTarget(_settings.SlimBarHeight); _ySpring.SetTarget(island ? 3 : 0); _rSpring.SetTarget(4); _earSpring.SetTarget(0); _expandOpacity.SetTarget(0);
        }
        else
        {
            bool playing = _currentMedia?.IsPlaying == true;
            bool focusing = _focus.IsRunning;
            bool inCall = _settings.ShowPrivacyDots && _currentPrivacy.MicInUse && !_isMicMuted;
            bool extraRight = (_currentPrivacy.HasBattery && (!_currentPrivacy.IsCharging || _currentPrivacy.BatteryPercent <= 20))
                              || inCall || (_settings.ShowPrivacyDots && _currentPrivacy.CameraInUse) || _isMicMuted;

            double collapsedW =
                _settings.RestingFace == RestingFace.Minimal && !playing && !focusing && !extraRight ? 184 :
                playing ? (extraRight ? 310 : 284) :
                focusing ? (extraRight ? 278 : 246) :
                _settings.RestingFace == RestingFace.SystemStats ? 282 :
                _settings.RestingFace == RestingFace.ClockDate ? 256 :
                (extraRight ? 278 : 248);

            _wSpring.SetTarget(collapsedW); _hSpring.SetTarget(36); _ySpring.SetTarget(island ? islandY : 0); _rSpring.SetTarget(island ? 18 : 16); _earSpring.SetTarget(island ? 0 : 9); _expandOpacity.SetTarget(0);
        }

        UpdateActiveTimers();
        _springs.Wake();
    }

    private void RenderSpringFrame()
    {
        double w = Math.Max(40, _wSpring.Value), h = Math.Max(4, _hSpring.Value), y = Math.Max(0, _ySpring.Value);
        double r = Math.Min(_rSpring.Value, Math.Min(w / 2.0, h / 2.0)), ear = Math.Max(0, _earSpring.Value);
        double exp = Math.Clamp(_expandOpacity.Value, 0, 1);

        bool island = _settings.Shape == NotchShape.Island || y > 1.0;
        var (fillGeo, borderGeo) = BuildShellGeometry(CanvasCenterX, y, w, h, r, island ? 0 : ear, island);
        ShellOuterHalo.Data = borderGeo; ShellFill.Data = fillGeo; ShellGlow.Data = fillGeo; ShellSheen.Data = fillGeo; ShellBorder.Data = borderGeo;

        double left = CanvasCenterX - w / 2.0;
        ContentHost.Margin = new Thickness(left, y, 0, 0);
        ContentHost.Width = w; ContentHost.Height = h;
        ContentHost.Clip = BuildContentClip(w, h, r, island);

        bool showSlim = h < 14 && exp < 0.08;
        bool showGame = !showSlim && ShowGameStrip && exp < 0.08;
        SlimLayer.Visibility = showSlim ? Visibility.Visible : Visibility.Collapsed;
        GameStripLayer.Visibility = showGame ? Visibility.Visible : Visibility.Collapsed;

        double collapsedAlpha = (showSlim || showGame) ? 0 : Math.Clamp(1.0 - exp * 2.4, 0, 1);
        CollapsedLayer.Opacity = collapsedAlpha;
        CollapsedLayer.Visibility = collapsedAlpha > 0.01 ? Visibility.Visible : Visibility.Collapsed;

        double expandedAlpha = Math.Clamp((exp - 0.16) / 0.84, 0, 1);
        ExpandedLayer.Opacity = expandedAlpha;
        ExpandedLayer.Visibility = expandedAlpha > 0.01 ? Visibility.Visible : Visibility.Collapsed;
        double scale = 0.968 + 0.032 * expandedAlpha;
        ExpandedScale.ScaleX = scale; ExpandedScale.ScaleY = scale;

        if (_currentMedia != null)
        {
            if (_settings.Theme == NotchTheme.AlbumSync)
            {
                double expandedGlow = _activeTab == ActiveTab.Media ? 0.56 : 0.24;
                double pillGlow = (showSlim || showGame) ? 0 : (_currentMedia.IsPlaying ? 0.44 : 0.22);
                ShellGlow.Opacity = expandedAlpha * expandedGlow + collapsedAlpha * pillGlow;
            }
            else if (_settings.ShowAlbumGlow)
            {
                double expandedGlow = _activeTab == ActiveTab.Media ? (_currentMedia.IsPlaying ? 0.36 : 0.16) : 0;
                double pillGlow = (!showSlim && !showGame && _currentMedia.IsPlaying) ? 0.22 : 0;
                ShellGlow.Opacity = expandedAlpha * expandedGlow + collapsedAlpha * pillGlow;
            }
            else ShellGlow.Opacity = 0;
        }
        else ShellGlow.Opacity = 0;
    }

    private static Geometry BuildContentClip(double w, double h, double r, bool island)
    {
        double topR = island ? r : 0;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(topR, 0), true, true);
            ctx.LineTo(new Point(w - topR, 0), true, false);
            if (topR > 0.1) ctx.ArcTo(new Point(w, topR), new Size(topR, topR), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w, h - r), true, false);
            if (r > 0.1) ctx.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(r, h), true, false);
            if (r > 0.1) ctx.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, topR), true, false);
            if (topR > 0.1) ctx.ArcTo(new Point(topR, 0), new Size(topR, topR), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static (Geometry fill, Geometry border) BuildShellGeometry(double cx, double topY, double w, double h, double r, double ear, bool island)
    {
        double left = cx - w / 2.0, right = cx + w / 2.0, bottom = topY + h;
        r = Math.Clamp(r, 0, Math.Min(w / 2.0, h / 2.0));

        if (island || ear < 0.3)
        {
            double topR = island ? r : 0;
            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(left + topR, topY), true, true);
                ctx.LineTo(new Point(right - topR, topY), true, false);
                if (topR > 0.1) ctx.ArcTo(new Point(right, topY + topR), new Size(topR, topR), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(right, bottom - r), true, false);
                if (r > 0.1) ctx.ArcTo(new Point(right - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(left + r, bottom), true, false);
                if (r > 0.1) ctx.ArcTo(new Point(left, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(left, topY + topR), true, false);
                if (topR > 0.1) ctx.ArcTo(new Point(left + topR, topY), new Size(topR, topR), 0, false, SweepDirection.Clockwise, true, false);
            }
            fill.Freeze();
            if (island) return (fill, fill);

            var border = new StreamGeometry();
            using (var ctx = border.Open())
            {
                ctx.BeginFigure(new Point(left, topY), false, false);
                ctx.LineTo(new Point(left, bottom - r), true, false);
                if (r > 0.1) ctx.ArcTo(new Point(left + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.LineTo(new Point(right - r, bottom), true, false);
                if (r > 0.1) ctx.ArcTo(new Point(right, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.LineTo(new Point(right, topY), true, false);
            }
            border.Freeze();
            return (fill, border);
        }

        double safeEar = Math.Min(ear, Math.Max(0, h - r));
        var notchFill = new StreamGeometry();
        using (var ctx = notchFill.Open())
        {
            ctx.BeginFigure(new Point(left - safeEar, topY), true, true);
            ctx.QuadraticBezierTo(new Point(left, topY), new Point(left, topY + safeEar), true, false);
            ctx.LineTo(new Point(left, bottom - r), true, false);
            ctx.ArcTo(new Point(left + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(right - r, bottom), true, false);
            ctx.ArcTo(new Point(right, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(right, topY + safeEar), true, false);
            ctx.QuadraticBezierTo(new Point(right, topY), new Point(right + safeEar, topY), true, false);
        }
        notchFill.Freeze();

        var notchBorder = new StreamGeometry();
        using (var ctx = notchBorder.Open())
        {
            ctx.BeginFigure(new Point(left - safeEar, topY), false, false);
            ctx.QuadraticBezierTo(new Point(left, topY), new Point(left, topY + safeEar), true, false);
            ctx.LineTo(new Point(left, bottom - r), true, false);
            ctx.ArcTo(new Point(left + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(right - r, bottom), true, false);
            ctx.ArcTo(new Point(right, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(right, topY + safeEar), true, false);
            ctx.QuadraticBezierTo(new Point(right, topY), new Point(right + safeEar, topY), true, false);
        }
        notchBorder.Freeze();
        return (notchFill, notchBorder);
    }

    // ── Iconic NotchPixel Wavy Sine-Wave Scrub Bar ──────────────────────────

    private void UpdateWavyScrubBar(double ratio)
    {
        double trackW = ScrubTrack.ActualWidth > 20 ? ScrubTrack.ActualWidth : 492;
        double midY = 11.0;
        double clamped = Math.Clamp(ratio, 0, 1);
        double thumbX = Math.Clamp(trackW * clamped, 0, trackW - 4);

        WaveThumb.Margin = new Thickness(thumbX, 0, 0, 0);
        WaveRemLine.X1 = Math.Min(trackW, thumbX + 7);
        WaveRemLine.X2 = trackW;
        WaveRemLine.Y1 = midY;
        WaveRemLine.Y2 = midY;

        if (thumbX < 4)
        {
            WavePlayedPath.Data = null;
            return;
        }

        double amp = _currentMedia?.IsPlaying == true ? 3.2 : 1.0;
        const double wavelength = 22.0;
        const double step = 2.0;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double xEnd = Math.Max(0, thumbX - 3);
            double y0 = midY + Math.Sin(_wavePhase) * amp;
            ctx.BeginFigure(new Point(0, y0), false, false);
            for (double x = step; x <= xEnd; x += step)
            {
                double y = midY + Math.Sin((x / wavelength) * Math.PI * 2.0 - _wavePhase) * amp;
                ctx.LineTo(new Point(x, y), true, true);
            }
        }
        geo.Freeze();
        WavePlayedPath.Data = geo;
    }

    // ── Dynamic Island Live Notifications & Inline HUDs ─────────────────────

    private void OnDefaultAudioOutputChanged(string? deviceName)
    {
        SyncAudioUi();
        if (!_settings.ShowDeviceHuds || IsOpen || string.IsNullOrWhiteSpace(deviceName)) return;
        string clean = deviceName.Trim();
        if (string.Equals(clean, _lastAudioOutputName, StringComparison.OrdinalIgnoreCase)) return;
        _lastAudioOutputName = clean;

        // Avoid double-notifying if BluetoothWatcher already showed a toast for this device moments ago.
        if ((DateTime.UtcNow - _bluetooth.LastEventUtc).TotalSeconds < 3.5) return;

        bool isHeadphone = clean.Contains("head", StringComparison.OrdinalIgnoreCase) ||
                           clean.Contains("ear", StringComparison.OrdinalIgnoreCase) ||
                           clean.Contains("bud", StringComparison.OrdinalIgnoreCase) ||
                           clean.Contains("pod", StringComparison.OrdinalIgnoreCase) ||
                           clean.Contains("hands-free", StringComparison.OrdinalIgnoreCase);

        ShowRichToast(isHeadphone ? Icons.Headphones : Icons.Volume, clean, "Audio Output", "OUT",
            Color.FromRgb(0x30, 0xD1, 0x58), 2300);
    }

    private void OnBluetoothDeviceChanged(BluetoothDeviceEvent ev)
    {
        if (!_settings.ShowDeviceHuds || IsOpen) return;
        Geometry ico = ev.IsAudioDevice ? Icons.Headphones : Icons.Bluetooth;
        if (ev.Connected)
        {
            Color accent = ev.IsAudioDevice ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x0A, 0x84, 0xFF);
            string badge = ev.BatteryPercent.HasValue ? $"{ev.BatteryPercent}%" : "BT";
            ShowRichToast(ico, ev.DeviceName, "Connected", badge, accent, 2700);
        }
        else
        {
            ShowRichToast(ico, ev.DeviceName, "Disconnected", "", Color.FromRgb(0x9A, 0xA0, 0xAE), 2100);
        }
    }

    private void ShowTextToast(Geometry icon, string message, Color iconColor) =>
        ShowRichToast(icon, message, "", "", iconColor, 1800);

    private void ShowRichToast(Geometry icon, string title, string subtitle, string rightPill, Color accent, int durationMs = 2200)
    {
        if (IsOpen) return;
        _toastActive = true;
        _toastIsTrackPeek = false;
        bool hasSub = !string.IsNullOrWhiteSpace(subtitle);
        _toastHeight = hasSub ? 42 : 36;
        _toastWidth = hasSub ? 306 : 272;

        ToastThumbBorder.Visibility = Visibility.Collapsed;
        ToastIconBadge.Visibility = Visibility.Visible;
        ToastIconBadge.Background = new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
        ToastIcon.Data = icon;
        ToastIcon.Stroke = new SolidColorBrush(accent);

        ToastTextStack.Visibility = Visibility.Visible;
        ToastBarTrack.Visibility = Visibility.Collapsed;
        ToastText.Text = title;
        ToastText.Foreground = GetPrimaryBrush();
        ToastSubText.Text = subtitle;
        ToastSubText.Foreground = GetSecondaryBrush();
        ToastSubText.Visibility = hasSub ? Visibility.Visible : Visibility.Collapsed;

        bool hasPill = !string.IsNullOrWhiteSpace(rightPill);
        ToastRightBadge.Visibility = hasPill ? Visibility.Visible : Visibility.Collapsed;
        if (hasPill)
        {
            ToastRightBadge.Background = new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B));
            ToastRightBadge.Padding = new Thickness(7, 2.5, 7, 2.5);
            ToastValueLbl.Text = rightPill;
            ToastValueLbl.Foreground = new SolidColorBrush(accent);
        }

        CollapsedMain.Visibility = Visibility.Collapsed;
        CollapsedProgressTrack.Visibility = Visibility.Collapsed;
        ToastLayer.Visibility = Visibility.Visible;
        BringToTopmostNoActivate(forceReassert: true);
        _toastTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _toastTimer.Stop(); _toastTimer.Start();
        UpdateTargets();
    }

    private void ShowTrackPeekToast(MediaSnapshot snap)
    {
        if (IsOpen) return;
        _toastActive = true;
        _toastIsTrackPeek = true;
        _toastHeight = 42;
        _toastWidth = 310;

        if (snap.AlbumArt != null)
        {
            ToastIconBadge.Visibility = Visibility.Collapsed;
            ToastThumbBorder.Visibility = Visibility.Visible;
            ToastThumbImg.Source = snap.AlbumArt;
        }
        else
        {
            ToastThumbBorder.Visibility = Visibility.Collapsed;
            ToastIconBadge.Visibility = Visibility.Visible;
            ToastIconBadge.Background = new SolidColorBrush(Color.FromArgb(0x2E, snap.AccentColor.R, snap.AccentColor.G, snap.AccentColor.B));
            ToastIcon.Data = Icons.MusicNote;
            ToastIcon.Stroke = GetPrimaryBrush();
        }

        ToastTextStack.Visibility = Visibility.Visible;
        ToastBarTrack.Visibility = Visibility.Collapsed;
        ToastText.Text = snap.Title;
        ToastText.Foreground = GetPrimaryBrush();
        string artist = string.IsNullOrWhiteSpace(snap.Artist) ? snap.SourceApp : snap.Artist;
        ToastSubText.Text = artist;
        ToastSubText.Foreground = GetSecondaryBrush();
        ToastSubText.Visibility = Visibility.Visible;

        ToastRightBadge.Visibility = Visibility.Visible;
        ToastRightBadge.Background = GetSubtleSurfaceBrush();
        ToastRightBadge.Padding = new Thickness(7, 2.5, 7, 2.5);
        ToastValueLbl.Text = snap.SourceApp;
        ToastValueLbl.Foreground = GetSecondaryBrush();

        CollapsedMain.Visibility = Visibility.Collapsed;
        CollapsedProgressTrack.Visibility = Visibility.Collapsed;
        ToastLayer.Visibility = Visibility.Visible;
        BringToTopmostNoActivate(forceReassert: true);
        _toastTimer.Interval = TimeSpan.FromMilliseconds(2200);
        _toastTimer.Stop(); _toastTimer.Start();
        UpdateTargets();
    }

    private void ShowVolumeHud(float volume, bool muted)
    {
        int pct = muted ? 0 : (int)Math.Round(volume * 100);
        Color accent = muted ? Color.FromRgb(0xFF, 0x45, 0x3A) : (IsLight ? Color.FromRgb(0x14, 0x15, 0x19) : Colors.White);
        ShowBarHud(muted || pct == 0 ? Icons.VolumeMuted : Icons.Volume, pct / 100.0, muted ? "Muted" : $"{pct}%", accent);
    }

    private void ShowBarHud(Geometry icon, double ratio, string label, Color accent)
    {
        if (IsOpen) return;
        _toastActive = true;
        _toastIsTrackPeek = false;
        _toastHeight = 36;
        _toastWidth = 278;

        double clamped = Math.Clamp(ratio, 0, 1);
        ToastThumbBorder.Visibility = Visibility.Collapsed;
        ToastIconBadge.Visibility = Visibility.Visible;
        ToastIconBadge.Background = new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B));
        ToastIcon.Data = icon;
        ToastIcon.Stroke = new SolidColorBrush(accent);

        ToastTextStack.Visibility = Visibility.Collapsed;
        ToastBarTrack.Visibility = Visibility.Visible;
        ToastBarFill.Width = Math.Max(0, 152 * clamped);
        ToastBarFill.Background = new SolidColorBrush(accent);

        ToastRightBadge.Visibility = Visibility.Visible;
        ToastRightBadge.Background = Brushes.Transparent;
        ToastRightBadge.Padding = new Thickness(0);
        ToastValueLbl.Text = label;
        ToastValueLbl.Foreground = GetPrimaryBrush();

        CollapsedMain.Visibility = Visibility.Collapsed;
        CollapsedProgressTrack.Visibility = Visibility.Collapsed;
        ToastLayer.Visibility = Visibility.Visible;
        BringToTopmostNoActivate(forceReassert: true);
        _toastTimer.Interval = TimeSpan.FromMilliseconds(1550);
        _toastTimer.Stop(); _toastTimer.Start();
        UpdateTargets();
    }

    private void HideToast()
    {
        _toastTimer.Stop(); _toastActive = false; _toastIsTrackPeek = false;
        ToastLayer.Visibility = Visibility.Collapsed; CollapsedMain.Visibility = Visibility.Visible;
        UpdateCollapsedSlots();
        UpdateTargets();
    }

    // ── Clock, World Clock, Weather, Media, Lyrics, Privacy ─────────────────

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string full = now.ToString("h:mm tt");
        CollapsedClockLeft.Text = now.ToString("h:mm");
        CollapsedClockMeridiem.Text = now.ToString("tt");
        CollapsedClockRight.Text = full;
        WidgetTimeNum.Text = now.ToString("h:mm");
        WidgetTimeMeridiem.Text = now.ToString("tt");
        WidgetDayLbl.Text = now.ToString("ddd").ToUpperInvariant();
        RebuildCalendarStrip(now);

        // World Clock (London / UTC)
        try
        {
            var gmt = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "GMT Standard Time");
            WorldClockCity.Text = "London";
            WorldClockTime.Text = gmt.ToString("h:mm tt");
        }
        catch
        {
            WorldClockCity.Text = "UTC";
            WorldClockTime.Text = DateTime.UtcNow.ToString("h:mm tt");
        }

        if (_settings.RestingFace == RestingFace.ClockDate)
            CollapsedRightDetail.Text = now.ToString("ddd d");

        int ms = Math.Max(500, (60 - now.Second) * 1000 - now.Millisecond + 40);
        _clockTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _clockTimer.Start();
    }

    private void RebuildCalendarStrip(DateTime now)
    {
        CalBigDayLbl.Text = now.Day.ToString();
        CalDowLbl.Text = now.ToString("ddd").ToUpperInvariant();
        CalMonthLbl.Text = now.ToString("MMM").ToUpperInvariant();
        CalWeekLbl.Text = $"Week {System.Globalization.ISOWeek.GetWeekOfYear(now)}";

        CalWeekStrip.Children.Clear();
        DateTime sunday = now.Date.AddDays(-(int)now.DayOfWeek);
        string[] dayLetters = { "S", "M", "T", "W", "T", "F", "S" };
        var primary = GetPrimaryBrush();
        var secondary = GetSecondaryBrush();

        for (int i = 0; i < 7; i++)
        {
            DateTime d = sunday.AddDays(i);
            bool isToday = d.Date == now.Date;
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = dayLetters[i],
                FontSize = 9.5,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isToday ? primary : secondary,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = d.Day.ToString(),
                FontSize = 11.5,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Medium,
                Foreground = isToday ? primary : secondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            });

            var cell = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(1, 0, 1, 0),
                Background = isToday ? new SolidColorBrush(IsLight ? Color.FromArgb(0x22, 0x00, 0x00, 0x00) : Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
                Child = stack
            };
            CalWeekStrip.Children.Add(cell);
        }
    }

    private void WidgetFlip_Click(object sender, RoutedEventArgs e)
    {
        _widgetsAltPage = !_widgetsAltPage;
        WidgetsPageMain.Visibility = _widgetsAltPage ? Visibility.Collapsed : Visibility.Visible;
        WidgetsPageAlt.Visibility = _widgetsAltPage ? Visibility.Visible : Visibility.Collapsed;
        UpdatePerfDisplay();
    }

    private void OnWeatherChanged(WeatherSnapshot? snap)
    {
        _currentWeather = snap;
        if (snap == null) return;
        var ico = Icons.ForWeather(snap.IconKey);
        WeatherCardIcon.Data = ico;
        CollapsedWeatherIcon.Data = ico;
        WeatherTempLbl.Text = $"{snap.TempC}°";
        WeatherCondLbl.Text = $"{snap.Condition} · {snap.City}";
        WeatherHiLoLbl.Text = $"H {snap.HighC}°  L {snap.LowC}°";
        CollapsedWeatherLbl.Text = $"{snap.TempC}°";
        UpdateCollapsedSlots();
        if (!IsOpen) UpdateTargets();
    }

    private void OnMediaChanged(MediaSnapshot? snap)
    {
        _currentMedia = snap;
        bool playing = snap?.IsPlaying == true;

        if (snap == null)
        {
            MediaTitleLbl.Text = "Nothing Playing";
            MediaArtistLbl.Text = "Play music from Spotify, YouTube, etc.";
            MediaArtistLbl.Foreground = GetSecondaryBrush();
            CollapsedTrackLbl.Text = "Now Playing";
            ExpandedArt.Source = null; CollapsedArt.Source = null;
            ArtPlaceholder.Visibility = Visibility.Visible;
            PlayPauseIcon.Data = Icons.Play;
            TimeElapsedLbl.Text = "0:00"; TimeDurationLbl.Text = "-0:00";
            ArtScale.ScaleX = 0.94; ArtScale.ScaleY = 0.94;
            CollapsedProgressFill.Width = 0;
            _currentLyrics = null; _lyricsTrackKey = "";
            UpdateWavyScrubBar(0);
            UpdateLyricsDisplay(TimeSpan.Zero);
            if (_settings.Theme == NotchTheme.AlbumSync) ApplyAlbumSyncShell();
        }
        else
        {
            CollapsedArt.Source = snap.AlbumArt;
            CollapsedTrackLbl.Text = snap.Title;
            MediaTitleLbl.Text = snap.Title;
            MediaArtistLbl.Text = string.IsNullOrWhiteSpace(snap.Artist) ? snap.SourceApp : snap.Artist;
            ExpandedArt.Source = snap.AlbumArt;
            ArtPlaceholder.Visibility = snap.AlbumArt == null ? Visibility.Visible : Visibility.Collapsed;
            PlayPauseIcon.Data = playing ? Icons.Pause : Icons.Play;
            ArtScale.ScaleX = playing ? 1.0 : 0.94; ArtScale.ScaleY = playing ? 1.0 : 0.94;

            var c = snap.AccentColor;
            // Soften accent slightly for tasteful NotchPixel readability.
            var softAccent = Color.FromRgb(
                (byte)Math.Min(255, c.R * 0.72 + 72),
                (byte)Math.Min(255, c.G * 0.72 + 72),
                (byte)Math.Min(255, c.B * 0.72 + 72));
            var accentBrush = new SolidColorBrush(softAccent); accentBrush.Freeze();

            MediaArtistLbl.Foreground = IsLight ? GetSecondaryBrush() : accentBrush;
            WavePlayedPath.Stroke = accentBrush;
            SourceMiniBadge.Background = accentBrush;
            CollapsedProgressFill.Background = accentBrush;
            EqBar1.Background = accentBrush; EqBar2.Background = accentBrush; EqBar3.Background = accentBrush;
            EqBar4.Background = accentBrush; EqBar5.Background = accentBrush;

            var glow = new RadialGradientBrush { Center = new Point(0.16, 0.48), GradientOrigin = new Point(0.16, 0.48), RadiusX = 0.62, RadiusY = 1.0 };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(205, c.R, c.G, c.B), 0.0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(65, snap.SecondaryColor.R, snap.SecondaryColor.G, snap.SecondaryColor.B), 0.55));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, snap.SecondaryColor.R, snap.SecondaryColor.G, snap.SecondaryColor.B), 1.0));
            glow.Freeze();
            ShellGlow.Fill = glow;

            if (_settings.Theme == NotchTheme.AlbumSync) ApplyAlbumSyncShell();

            string key = $"{snap.Title}|{snap.Artist}";
            if (key != _lyricsTrackKey)
            {
                _lyricsTrackKey = key; _currentLyrics = null;
                if (_lyricsOpen) _ = LoadLyricsForCurrentTrackAsync();
            }

            if (_mediaBootstrapped && _settings.ShowTrackChangeHud && snap.IsPlaying && key != _lastNotifiedTrackKey && !IsOpen)
            {
                _lastNotifiedTrackKey = key;
                ShowTrackPeekToast(snap);
            }
            else if (_toastActive && _toastIsTrackPeek && snap.AlbumArt != null)
            {
                // Live-refresh artwork on an active track-change pill when browser thumbnail arrives 500ms later.
                ToastIconBadge.Visibility = Visibility.Collapsed;
                ToastThumbBorder.Visibility = Visibility.Visible;
                ToastThumbImg.Source = snap.AlbumArt;
            }
            else if (!_mediaBootstrapped)
            {
                _lastNotifiedTrackKey = key;
                _mediaBootstrapped = true;
            }

            UpdateTimelineAndLyricsUi();
        }

        UpdateCollapsedSlots();
        UpdateTargets();
    }

    private async Task LoadLyricsForCurrentTrackAsync()
    {
        var snap = _currentMedia;
        if (snap == null) { LyricPrevLbl.Text = ""; LyricCurrLbl.Text = "Play a song to view synced lyrics"; LyricNextLbl.Text = ""; return; }
        _lyricsCts?.Cancel(); _lyricsCts = new CancellationTokenSource();
        var ct = _lyricsCts.Token;
        LyricPrevLbl.Text = ""; LyricCurrLbl.Text = "Searching synced lyrics…"; LyricNextLbl.Text = "";
        var res = await _lyrics.FetchAsync(snap.Title, snap.Artist, snap.Album, snap.Duration.TotalSeconds, ct);
        if (ct.IsCancellationRequested) return;
        _currentLyrics = res;
        UpdateTimelineAndLyricsUi();
    }

    private void UpdateTimelineAndLyricsUi()
    {
        var snap = _currentMedia;
        if (snap == null) return;

        TimeSpan pos = snap.Position;
        if (snap.IsPlaying)
        {
            pos += DateTimeOffset.UtcNow - snap.LastUpdated;
            _wavePhase += 0.28;
        }
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (snap.Duration > TimeSpan.Zero && pos > snap.Duration) pos = snap.Duration;

        if (snap.Duration.TotalSeconds > 0.5)
        {
            double ratio = Math.Clamp(pos.TotalSeconds / snap.Duration.TotalSeconds, 0, 1);
            UpdateWavyScrubBar(ratio);
            TimeElapsedLbl.Text = FormatTime(pos);
            TimeDurationLbl.Text = "-" + FormatTime(snap.Duration - pos);
        }

        if (_lyricsOpen) UpdateLyricsDisplay(pos);
    }

    private void UpdateLyricsDisplay(TimeSpan pos)
    {
        if (_currentMedia == null) { LyricPrevLbl.Text = ""; LyricCurrLbl.Text = "Play a song to view synced lyrics"; LyricNextLbl.Text = ""; return; }
        if (_currentLyrics == null) return;
        if (!_currentLyrics.Found || _currentLyrics.Lines.Count == 0) { LyricPrevLbl.Text = ""; LyricCurrLbl.Text = "No lyrics found for this track"; LyricNextLbl.Text = ""; return; }

        var lines = _currentLyrics.Lines;
        int idx = _currentLyrics.IsSynced ? LyricsService.GetActiveLineIndex(lines, pos) : 0;
        if (idx < 0) { LyricPrevLbl.Text = ""; LyricCurrLbl.Text = "♪"; LyricNextLbl.Text = lines[0].Text; return; }

        LyricPrevLbl.Text = idx > 0 ? lines[idx - 1].Text : "";
        LyricCurrLbl.Text = lines[idx].Text;
        LyricNextLbl.Text = idx + 1 < lines.Count ? lines[idx + 1].Text : "";
    }

    private void UpdateCollapsedSlots()
    {
        bool focusing = _focus.IsRunning;
        bool playing = !focusing && _currentMedia?.IsPlaying == true;
        bool idle = !focusing && !playing;
        bool stealth = idle && _settings.RestingFace == RestingFace.Minimal;

        CollapsedLeftFocus.Visibility = focusing ? Visibility.Visible : Visibility.Collapsed;
        CollapsedLeftMedia.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        CollapsedMediaViz.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        CollapsedProgressTrack.Visibility = playing && !_toastActive ? Visibility.Visible : Visibility.Collapsed;
        CollapsedLeftIdle.Visibility = idle && !stealth ? Visibility.Visible : Visibility.Collapsed;

        CollapsedClockRight.Visibility = focusing ? Visibility.Visible : Visibility.Collapsed;
        CollapsedWeather.Visibility = idle && _settings.RestingFace == RestingFace.ClockWeather && _currentWeather != null ? Visibility.Visible : Visibility.Collapsed;

        bool showDetail = idle && (_settings.RestingFace == RestingFace.ClockDate || _settings.RestingFace == RestingFace.SystemStats);
        CollapsedRightDetailWrap.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.RestingFace == RestingFace.ClockDate) CollapsedRightDetail.Text = DateTime.Now.ToString("ddd d");

        bool showBat = _currentPrivacy.HasBattery && (_settings.RestingFace == RestingFace.Battery || !_currentPrivacy.IsCharging || _currentPrivacy.BatteryPercent <= 20);
        CollapsedBatteryWrap.Visibility = showBat && !stealth && !playing ? Visibility.Visible : Visibility.Collapsed;

        bool showMicDot = _settings.ShowPrivacyDots && _currentPrivacy.MicInUse && !_isMicMuted;
        CollapsedCallWrap.Visibility = showMicDot ? Visibility.Visible : Visibility.Collapsed;
        CamDot.Visibility = _settings.ShowPrivacyDots && _currentPrivacy.CameraInUse ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPrivacyChanged(PrivacyBatterySnapshot snap)
    {
        _currentPrivacy = snap;
        int pct = snap.HasBattery ? snap.BatteryPercent : 100;
        if (snap.HasBattery)
        {
            CollapsedBattery.Text = $"{pct}%";
            bool low = pct <= 20 && !snap.IsCharging;
            CollapsedBattery.Foreground = low
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x52))
                : snap.IsCharging ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : GetPrimaryBrush();
            CollapsedBatteryWrap.Background = low
                ? new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0x45, 0x3A))
                : snap.IsCharging ? new SolidColorBrush(Color.FromArgb(0x24, 0x30, 0xD1, 0x58)) : GetSubtleSurfaceBrush();

            if (!_hasInitialPowerState)
            {
                _hasInitialPowerState = true;
                _lastChargingState = snap.IsCharging;
            }
            else if (_settings.ShowDeviceHuds && !IsOpen)
            {
                if (snap.IsCharging != _lastChargingState)
                {
                    _lastChargingState = snap.IsCharging;
                    if (snap.IsCharging)
                    {
                        _lowBatNotified20 = false; _lowBatNotified10 = false;
                        ShowRichToast(Icons.Bolt, "Charging", "AC Power connected", $"{pct}%", Color.FromRgb(0x30, 0xD1, 0x58), 2400);
                    }
                    else
                    {
                        ShowRichToast(Icons.Battery, "On Battery", "Charger unplugged", $"{pct}%", Color.FromRgb(0xFF, 0x9F, 0x0A), 2100);
                    }
                }
                else if (!snap.IsCharging)
                {
                    if (pct <= 10 && !_lowBatNotified10)
                    {
                        _lowBatNotified10 = true; _lowBatNotified20 = true;
                        ShowRichToast(Icons.Battery, "Critical Battery", "Plug in charger now", $"{pct}%", Color.FromRgb(0xFF, 0x45, 0x3A), 3400);
                    }
                    else if (pct <= 20 && !_lowBatNotified20)
                    {
                        _lowBatNotified20 = true;
                        ShowRichToast(Icons.Battery, "Low Battery", "Connect charger soon", $"{pct}%", Color.FromRgb(0xFF, 0x9F, 0x0A), 3000);
                    }
                }
            }
            else
            {
                _lastChargingState = snap.IsCharging;
            }
        }

        CtrlBatPctLbl.Text = snap.HasBattery ? $"{pct}%" : "AC";
        CtrlStatusLbl.Text = snap.HasBattery ? (snap.IsCharging ? "Plugged in" : "On battery") : "Desktop";
        WidgetBatPctLbl.Text = snap.HasBattery ? $"{pct}%" : "AC";
        SparkBatVal.Text = snap.HasBattery ? $"{pct}%" : "AC";
        var batColor = pct <= 20 && !snap.IsCharging ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x30, 0xD1, 0x58);
        var batBrush = new SolidColorBrush(batColor);
        BatRingArc.Stroke = batBrush;
        BatRingArc.Data = BuildRingArc(snap.HasBattery ? pct : 100);
        WidgetBatPctLbl.Foreground = batBrush;
        SparkBatFill.Background = batBrush;
        SparkBatFill.Width = Math.Max(4, 134.0 * ((snap.HasBattery ? pct : 100) / 100.0));
        SparkBatVal.Foreground = batBrush;

        UpdateMicUi(); UpdateCollapsedSlots(); UpdateCallTimerText(); UpdateActiveTimers();
        if (!IsOpen) UpdateTargets();
    }

    private void UpdateCallTimerText()
    {
        CollapsedCallTimer.Text = (_currentPrivacy.MicInUse && _currentPrivacy.MicActiveSince.HasValue && !_isMicMuted)
            ? FormatTime(DateTime.UtcNow - _currentPrivacy.MicActiveSince.Value) : "";
    }

    private void UpdateActiveTimers()
    {
        bool runEq = _currentMedia?.IsPlaying == true && !_focus.IsRunning && !IsOpen && !IsSlim && !ShowGameStrip && !_toastActive;
        if (runEq && !_eqTimer.IsEnabled) _eqTimer.Start();
        else if (!runEq && _eqTimer.IsEnabled) _eqTimer.Stop();

        bool runTimeline = IsOpen && _activeTab == ActiveTab.Media && _currentMedia?.IsPlaying == true;
        if (runTimeline && !_timelineTimer.IsEnabled) _timelineTimer.Start();
        else if (!runTimeline && _timelineTimer.IsEnabled) _timelineTimer.Stop();

        bool needPerf = ShowGameStrip || (IsOpen && _activeTab == ActiveTab.Widgets) || (!IsOpen && !IsSlim && _settings.RestingFace == RestingFace.SystemStats);
        if (needPerf && !_perfTimer.IsEnabled) { UpdatePerfDisplay(); _perfTimer.Start(); }
        else if (!needPerf && _perfTimer.IsEnabled) _perfTimer.Stop();

        bool needCall = _settings.ShowPrivacyDots && _currentPrivacy.MicInUse && !_isMicMuted;
        if (needCall && !_callTimer.IsEnabled) { UpdateCallTimerText(); _callTimer.Start(); }
        else if (!needCall && _callTimer.IsEnabled) _callTimer.Stop();
    }

    private void UpdatePerfDisplay()
    {
        var s = _perf.Sample();
        GameStripCpuLbl.Text = $"CPU {s.CpuPercent}%"; GameStripRamLbl.Text = $"RAM {s.RamPercent}%";
        CpuRingLbl.Text = $"{s.CpuPercent}%"; CpuRingArc.Data = BuildRingArc(s.CpuPercent);
        RamRingLbl.Text = $"{s.RamPercent}%"; RamRingArc.Data = BuildRingArc(s.RamPercent);
        SparkCpuVal.Text = $"{s.CpuPercent}%";
        SparkRamVal.Text = $"{s.RamPercent}%";
        UpdateSparkline(CpuSparkPath, CpuSparkDot, CpuSparkCanvas, s.CpuHistory);
        UpdateSparkline(RamSparkPath, RamSparkDot, RamSparkCanvas, s.RamHistory);
        NetDownLbl.Text = $"↓ {SystemPerfService.FormatSpeed(s.NetDownBytesPerSec)}";
        NetUpLbl.Text = $"↑ {SystemPerfService.FormatSpeed(s.NetUpBytesPerSec)}";
        if (_settings.RestingFace == RestingFace.SystemStats) CollapsedRightDetail.Text = $"CPU {s.CpuPercent}% · {s.RamPercent}%";
    }

    private static void UpdateSparkline(Path path, Ellipse dot, Canvas canvas, IReadOnlyList<int> history)
    {
        double w = canvas.ActualWidth > 20 ? canvas.ActualWidth : 134.0;
        double h = canvas.ActualHeight > 6 ? canvas.ActualHeight : 16.0;
        if (history.Count == 0) { path.Data = null; dot.Visibility = Visibility.Collapsed; return; }

        int count = history.Count;
        double step = count > 1 ? (w - 4) / (count - 1) : 0;
        double lastX = 2, lastY = h - 2;

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? w - 3 : 2 + i * step;
                double y = (h - 2) - (Math.Clamp(history[i], 0, 100) / 100.0) * (h - 4);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, true);
                lastX = x; lastY = y;
            }
            if (count == 1)
            {
                ctx.BeginFigure(new Point(2, lastY), false, false);
                ctx.LineTo(new Point(w - 3, lastY), true, true);
            }
        }
        g.Freeze();
        path.Data = g;
        Canvas.SetLeft(dot, lastX - 2.25);
        Canvas.SetTop(dot, lastY - 2.25);
        dot.Visibility = Visibility.Visible;
    }

    private static Geometry BuildRingArc(double percent, double cx = 22.0, double cy = 22.0, double r = 20.0)
    {
        double clamped = Math.Clamp(percent, 0, 100);
        if (clamped <= 0.5) return Geometry.Empty;
        if (clamped >= 99.5)
        {
            var full = new EllipseGeometry(new Point(cx, cy), r, r);
            full.Freeze();
            return full;
        }

        double angleRad = (clamped / 100.0) * Math.PI * 2.0;
        double startRad = -Math.PI / 2.0;
        double endRad = startRad + angleRad;
        Point start = new(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        Point end = new(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, clamped > 50.0, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private void OnClipboardItemCopied(ClipboardItem item)
    {
        if (!_settings.ShowClipboardHud || IsOpen) return;
        if (item.Kind == ClipKind.Image)
        {
            ShowRichToast(Icons.Image, "Copied · Image", item.Preview, "IMG", Color.FromRgb(0x64, 0xD2, 0xFF), 1700);
        }
        else if (item.Kind == ClipKind.Link)
        {
            ShowRichToast(Icons.Link, "Copied · Link", item.Preview, "URL", Color.FromRgb(0x0A, 0x84, 0xFF), 1550);
        }
        else
        {
            ShowRichToast(Icons.Clipboard, "Copied · Text", item.Preview, "", Color.FromRgb(0xD8, 0xDC, 0xE6), 1450);
        }
    }

    public void TriggerTestNotification()
    {
        _isHovered = false;
        _isPinned = false;
        UpdatePinUi();
        ShowRichToast(Icons.Headphones, "AirPods Pro", "Connected", "92%", Color.FromRgb(0x30, 0xD1, 0x58), 2600);
    }

    private void EqTimer_Tick(object? sender, EventArgs e)
    {
        _eqPhase += 0.38;
        // Symmetrical Dynamic Island 5-bar spectrum (center bars taller, outer bars subtler).
        EqScale1.ScaleY = 0.22 + 0.46 * Math.Abs(Math.Sin(_eqPhase * 0.92 + 0.2));
        EqScale2.ScaleY = 0.30 + 0.62 * Math.Abs(Math.Sin(_eqPhase * 1.28 + 1.1));
        EqScale3.ScaleY = 0.36 + 0.64 * Math.Abs(Math.Sin(_eqPhase * 1.10 + 2.2));
        EqScale4.ScaleY = 0.28 + 0.60 * Math.Abs(Math.Sin(_eqPhase * 1.35 + 3.0));
        EqScale5.ScaleY = 0.20 + 0.44 * Math.Abs(Math.Sin(_eqPhase * 0.96 + 4.1));

        var snap = _currentMedia;
        if (snap != null && snap.Duration.TotalSeconds > 0.5)
        {
            TimeSpan pos = snap.Position + (DateTimeOffset.UtcNow - snap.LastUpdated);
            double ratio = Math.Clamp(pos.TotalSeconds / snap.Duration.TotalSeconds, 0, 1);
            double trackW = CollapsedProgressTrack.ActualWidth > 20 ? CollapsedProgressTrack.ActualWidth : 248;
            CollapsedProgressFill.Width = trackW * ratio;
        }
    }

    private static string FormatTime(TimeSpan ts)
    {
        int s = (int)Math.Max(0, ts.TotalSeconds);
        int h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
        return h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m}:{sec:D2}";
    }

    // ── Clipboard History UI ────────────────────────────────────────────────

    private void RebuildClipboardList()
    {
        ClipListStack.Children.Clear();
        var items = _clipboard.Items.Where(i => _clipFilter == "All" || i.Kind.ToString() == _clipFilter).ToList();
        ClipEmptyLbl.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var primary = GetPrimaryBrush(); var secondary = GetSecondaryBrush();

        foreach (var item in items.Take(20))
        {
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (item.Kind == ClipKind.Image && item.Thumbnail != null)
            {
                var thumb = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = item.Thumbnail, Stretch = Stretch.UniformToFill } };
                rowGrid.Children.Add(thumb);
            }
            else
            {
                rowGrid.Children.Add(new Viewbox { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = new Path { Data = item.Kind == ClipKind.Link ? Icons.Link : Icons.FileText, Stroke = secondary, StrokeThickness = 1.9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round } });
            }

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            textStack.Children.Add(new TextBlock { Text = item.Preview, FontSize = 11.5, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis });
            textStack.Children.Add(new TextBlock { Text = item.IsPinned ? $"Pinned · {item.CopiedAt:h:mm tt}" : item.CopiedAt.ToString("h:mm tt"), FontSize = 9.5, Foreground = item.IsPinned ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : secondary });
            Grid.SetColumn(textStack, 1); rowGrid.Children.Add(textStack);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pinBtn = new Button { Style = (Style)FindResource("IconBtn"), Width = 22, Height = 22, ToolTip = item.IsPinned ? "Unpin" : "Pin", Content = new Viewbox { Width = 10.5, Height = 10.5, Child = new Path { Data = Icons.Pin, Stroke = item.IsPinned ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : secondary, StrokeThickness = 2 } } };
            pinBtn.Click += (_, e) => { e.Handled = true; _clipboard.TogglePin(item.Id); };
            var delBtn = new Button { Style = (Style)FindResource("IconBtn"), Width = 22, Height = 22, ToolTip = "Remove", Content = new Viewbox { Width = 10.5, Height = 10.5, Child = new Path { Data = Icons.Trash, Stroke = secondary, StrokeThickness = 2 } } };
            delBtn.Click += (_, e) => { e.Handled = true; _clipboard.Remove(item.Id); };
            actions.Children.Add(pinBtn); actions.Children.Add(delBtn);
            Grid.SetColumn(actions, 2); rowGrid.Children.Add(actions);

            var card = new Button { Style = (Style)FindResource("CardBtn"), Padding = new Thickness(9, 6, 6, 6), Margin = new Thickness(0, 0, 0, 5), Content = rowGrid, ToolTip = "Click to copy" };
            card.Click += (_, _) => { _clipboard.CopyToClipboard(item); card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)); };
            ClipListStack.Children.Add(card);
        }
        StyleClipFilters();
    }

    private void StyleClipFilters()
    {
        StyleSegment(ClipFilterAll, ClipFilterAllLbl, _clipFilter == "All");
        StyleSegment(ClipFilterText, ClipFilterTextLbl, _clipFilter == "Text");
        StyleSegment(ClipFilterLinks, ClipFilterLinksLbl, _clipFilter == "Link");
        StyleSegment(ClipFilterImages, ClipFilterImagesLbl, _clipFilter == "Image");
    }

    private void ClipFilter_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string tag) { _clipFilter = tag; RebuildClipboardList(); } }
    private void ClipClear_Click(object sender, RoutedEventArgs e) => _clipboard.ClearUnpinned();

    // ── Focus Timer & To-Do Checklist (Screenshot 4!) ───────────────────────

    private void UpdateFocusUi()
    {
        string clock = FormatTime(_focus.Remaining);
        MiniFocusTimeLbl.Text = clock;
        CollapsedFocusLbl.Text = clock;
        MiniFocusBtnLbl.Text = _focus.IsRunning ? "Focusing…" : $"{_focus.PresetMinutes} + 5 min";
        MiniFocusPlayIcon.Data = _focus.IsRunning ? Icons.Pause : Icons.Play;
        MiniFocusBtn.Background = _focus.IsRunning ? new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2)) : (IsLight ? new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)) : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)));
        MiniFocusPlayIcon.Fill = _focus.IsRunning ? Brushes.White : (IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12)));
        double remPct = Math.Clamp((1.0 - _focus.Progress) * 100.0, 0, 100);
        FocusRingArc.Data = BuildRingArc(_focus.IsRunning || _focus.Progress > 0.005 ? remPct : 0, 29.0, 29.0, 26.8);

        // Update purple Focus tile in Control Center (Screenshot 2!)
        CtrlFocusState.Text = _focus.IsRunning ? $"On · {clock}" : "Off";
        if (_focus.IsRunning)
        {
            CtrlFocusBtn.Background = new SolidColorBrush(Color.FromRgb(0x36, 0x20, 0x45));
            CtrlFocusBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xBF, 0x5A, 0xF2));
            FocusBadge.Background = new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2));
            CtrlFocusIcon.Stroke = Brushes.White;
        }
        else
        {
            CtrlFocusBtn.Background = GetCardSurfaceBrush();
            CtrlFocusBtn.BorderBrush = GetCardBorderBrush();
            FocusBadge.Background = GetSubtleSurfaceBrush();
            CtrlFocusIcon.Stroke = GetPrimaryBrush();
        }

        UpdateCollapsedSlots();
        if (!IsOpen) UpdateTargets();
    }

    private void OnFocusCompleted() =>
        ShowRichToast(Icons.Check, "Focus Complete", "Session finished · Great job!", "Done", Color.FromRgb(0xBF, 0x5A, 0xF2), 3400);
    private void FocusStartPause_Click(object sender, RoutedEventArgs e) => _focus.ToggleStartPause();
    private void FocusReset_Click(object sender, RoutedEventArgs e) => _focus.Reset();
    private void FocusMinus_Click(object sender, RoutedEventArgs e) => _focus.AdjustMinutes(-5);
    private void FocusPlus_Click(object sender, RoutedEventArgs e) => _focus.AdjustMinutes(5);

    private void TodoAddToggle_Click(object sender, RoutedEventArgs e)
    {
        bool showInput = TodoInputWrap.Visibility != Visibility.Visible;
        TodoInputWrap.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
        TodoListScroll.Visibility = showInput ? Visibility.Collapsed : Visibility.Visible;
        if (showInput)
        {
            _isTypingNote = true;
            SetNoActivate(false);
            NativeMethods.SetForegroundWindow(_hwnd);
            Activate();
            NoteInput.Focus();
        }
        else
        {
            NoteInput_LostFocus(sender, e);
        }
    }

    private void NoteInput_PreviewMouseDown(object sender, MouseButtonEventArgs e) { _isTypingNote = true; SetNoActivate(false); NativeMethods.SetForegroundWindow(_hwnd); Activate(); NoteInput.Focus(); }
    private void NoteInput_LostFocus(object sender, RoutedEventArgs e)
    {
        _isTypingNote = false;
        SetNoActivate(true);
        TodoInputWrap.Visibility = Visibility.Collapsed;
        TodoListScroll.Visibility = Visibility.Visible;
        if (_isHovered && !_isPinned) _hoverWatchTimer.Start();
    }

    private void NoteInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (!string.IsNullOrWhiteSpace(NoteInput.Text)) { _focus.AddNote(NoteInput.Text); NoteInput.Clear(); }
            NoteInput_LostFocus(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { Keyboard.ClearFocus(); NoteInput_LostFocus(sender, e); }
    }

    private void RebuildNotesList()
    {
        NotesListStack.Children.Clear();
        var openCount = _focus.Notes.Count(n => !n.Done);
        TodoCountLbl.Text = openCount.ToString();
        var primary = GetPrimaryBrush(); var secondary = GetSecondaryBrush();

        if (_focus.Notes.Count == 0)
        {
            NotesListStack.Children.Add(new TextBlock { Text = "Click + to add a task", FontSize = 11, Foreground = secondary, Margin = new Thickness(0, 8, 0, 0) });
            return;
        }

        foreach (var note in _focus.Notes.Take(12))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new Button
            {
                Style = (Style)FindResource("IconBtn"), Width = 15, Height = 15,
                Background = note.Done ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : Brushes.Transparent,
                BorderBrush = note.Done ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : secondary,
                BorderThickness = new Thickness(1.4),
                Content = note.Done ? new Viewbox { Width = 8.5, Height = 8.5, Child = new Path { Data = Icons.Check, Stroke = Brushes.Black, StrokeThickness = 3 } } : null
            };
            check.Click += (_, _) => _focus.ToggleNote(note.Id);

            var lbl = new TextBlock { Text = note.Text, FontSize = 11.5, Foreground = note.Done ? secondary : primary, TextDecorations = note.Done ? TextDecorations.Strikethrough : null, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 4, 0) };
            Grid.SetColumn(lbl, 1);

            var del = new Button { Style = (Style)FindResource("IconBtn"), Width = 17, Height = 17, Content = new Viewbox { Width = 9, Height = 9, Child = new Path { Data = Icons.Trash, Stroke = secondary, StrokeThickness = 2 } } };
            del.Click += (_, _) => _focus.DeleteNote(note.Id);
            Grid.SetColumn(del, 2);

            row.Children.Add(check); row.Children.Add(lbl); row.Children.Add(del);
            NotesListStack.Children.Add(row);
        }
    }

    // ── Quick Sites (NotchPixel Globe Tab!) ─────────────────────────────────

    private void RebuildSitesGrid()
    {
        SitesGrid.Children.Clear();
        var primary = GetPrimaryBrush();
        var secondary = GetSecondaryBrush();

        for (int i = 0; i < _sites.Sites.Count && i < 6; i++)
        {
            int idx = i;
            var site = _sites.Sites[i];
            Color accent = SitesService.ParseAccent(site.AccentHex);

            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.9) };
            if (IsLight)
            {
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, accent.R, accent.G, accent.B), 0.0));
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x12, accent.R, accent.G, accent.B), 1.0));
            }
            else
            {
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x52, (byte)(accent.R * 0.55), (byte)(accent.G * 0.55), (byte)(accent.B * 0.55)), 0.0));
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, (byte)(accent.R * 0.25), (byte)(accent.G * 0.25), (byte)(accent.B * 0.25)), 1.0));
            }
            bg.Freeze();

            var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string initial = string.IsNullOrWhiteSpace(site.Name) ? "W" : site.Name[..1].ToUpperInvariant();
            var iconBadge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(accent),
                    Child = new TextBlock { Text = initial, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            row.Children.Add(iconBadge);

            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            txt.Children.Add(new TextBlock { Text = site.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = SitesService.ShortDomain(site.Url), FontSize = 10, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);

            var card = new Button
            {
                Style = (Style)FindResource("CardBtn"),
                Margin = new Thickness(4),
                Padding = new Thickness(10, 8, 10, 8),
                Background = bg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)),
                Content = row,
                ToolTip = $"{site.Url} · Right-click to remove"
            };
            card.Click += (_, _) => { SitesService.Open(site); CollapseFromHover(); };
            card.MouseRightButtonUp += (_, _) => _sites.RemoveAt(idx);
            SitesGrid.Children.Add(card);
        }

        if (_sites.Sites.Count < 6)
        {
            var dashGrid = new Grid { Margin = new Thickness(4) };
            dashGrid.Children.Add(new Rectangle
            {
                RadiusX = 14, RadiusY = 14,
                Stroke = new SolidColorBrush(IsLight ? Color.FromArgb(0x38, 0, 0, 0) : Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            });
            var inner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(new Viewbox { Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), Child = new Path { Data = Icons.Plus, Stroke = secondary, StrokeThickness = 2 } });
            inner.Children.Add(new TextBlock { Text = "Add a site", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = secondary });
            var addBtn = new Button { Style = (Style)FindResource("CardBtn"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = inner, ToolTip = "Pin any website" };
            addBtn.Click += (_, _) => ShowSiteAddBar(true);
            dashGrid.Children.Add(addBtn);
            SitesGrid.Children.Add(dashGrid);
        }
    }

    private void ShowSiteAddBar(bool show)
    {
        SiteAddWrap.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SitesHintLbl.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            _isTypingSite = true;
            SetNoActivate(false);
            NativeMethods.SetForegroundWindow(_hwnd);
            Activate();
            SiteUrlInput.Focus();
        }
        else
        {
            _isTypingSite = false;
            SetNoActivate(true);
            SiteNameInput.Clear(); SiteUrlInput.Clear();
            if (_isHovered && !_isPinned) _hoverWatchTimer.Start();
        }
    }

    private void SiteInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isTypingSite = true; SetNoActivate(false); NativeMethods.SetForegroundWindow(_hwnd); Activate();
        if (sender is TextBox tb) tb.Focus();
    }

    private void SiteInput_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!SiteNameInput.IsKeyboardFocused && !SiteUrlInput.IsKeyboardFocused)
            {
                _isTypingSite = false;
                SetNoActivate(true);
                if (_isHovered && !_isPinned) _hoverWatchTimer.Start();
            }
        });
    }

    private void SiteInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SiteSave_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { ShowSiteAddBar(false); e.Handled = true; }
    }

    private void SiteSave_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SiteUrlInput.Text))
            _sites.AddSite(SiteNameInput.Text, SiteUrlInput.Text);
        ShowSiteAddBar(false);
    }

    private void SiteCancel_Click(object sender, RoutedEventArgs e) => ShowSiteAddBar(false);

    // ── Quick Capture (Screenshot / Snip / Record) & 6-Slot App Launcher ────

    private async void CaptureScreenshot_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false; UpdatePinUi();
        CollapseFromHover();
        await Task.Delay(220);
        string? fileName = ScreenCaptureService.CapturePrimaryScreenToFileAndClipboard();
        if (!string.IsNullOrEmpty(fileName))
            ShowRichToast(Icons.Camera, "Screenshot saved", fileName, "PNG", Color.FromRgb(0x30, 0xD1, 0x58), 2200);
    }

    private async void CaptureSnip_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false; UpdatePinUi();
        CollapseFromHover();
        await Task.Delay(160);
        ScreenCaptureService.LaunchSnip();
    }

    private async void CaptureRecord_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false; UpdatePinUi();
        CollapseFromHover();
        await Task.Delay(160);
        ScreenCaptureService.LaunchScreenRecord();
    }

    private void RebuildAppsGrid()
    {
        AppsGrid.Children.Clear();
        var primary = GetPrimaryBrush(); var secondary = GetSecondaryBrush();

        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            if (i < _launcher.Apps.Count)
            {
                var app = _launcher.Apps[i];
                var tileGrid = new Grid();
                tileGrid.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = GetSubtleSurfaceBrush(), Padding = new Thickness(4.5, 1, 4.5, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = $"{i + 1}", FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = secondary } });

                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) };
                if (app.Icon != null) stack.Children.Add(new Image { Source = app.Icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 0, 5) });
                stack.Children.Add(new TextBlock { Text = app.Name, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = primary, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                tileGrid.Children.Add(stack);

                var btn = new Button { Style = (Style)FindResource("CardBtn"), Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(5, 5, 5, 6), Content = tileGrid, ToolTip = $"{app.Name} · Ctrl+Alt+{i + 1} · Right-click to remove" };
                btn.Click += (_, _) => LauncherService.Launch(app);
                btn.MouseRightButtonUp += (_, _) => _launcher.RemoveAt(idx);
                AppsGrid.Children.Add(btn);
            }
            else
            {
                // NotchPixel signature dashed rounded-14 "+ Add" slot!
                var slotRoot = new StackPanel { Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                var box = new Grid { Height = 64 };
                box.Children.Add(new Rectangle
                {
                    RadiusX = 14, RadiusY = 14,
                    Stroke = new SolidColorBrush(IsLight ? Color.FromArgb(0x36, 0, 0, 0) : Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1.2,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                });
                var emptyBtn = new Button
                {
                    Style = (Style)FindResource("CardBtn"),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Content = new Viewbox { Width = 14, Height = 14, Child = new Path { Data = Icons.Plus, Stroke = secondary, StrokeThickness = 1.9 } },
                    ToolTip = "Pin an app (.exe)"
                };
                emptyBtn.Click += AddApp_Click;
                box.Children.Add(emptyBtn);
                slotRoot.Children.Add(box);
                slotRoot.Children.Add(new TextBlock { Text = "Add", FontSize = 10.5, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
                AppsGrid.Children.Add(slotRoot);
            }
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Pin an application to Pill", Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true) _launcher.AddApp(dlg.FileName);
    }

    // ── Control Center: Radios, Power Plan, Vertical Capsule Sliders, Mic ───

    private async Task RefreshRadiosAsync() { _radioStatus = await _toggles.GetStatusAsync(); await Dispatcher.InvokeAsync(UpdateRadioUi); }

    private void UpdateRadioUi()
    {
        StyleColorTile(WifiToggleBtn, WifiBadge, WifiIcon, WifiStateLbl, _radioStatus.WifiOn, _radioStatus.WifiAvailable ? (_radioStatus.WifiOn ? "On" : "Off") : "N/A", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x16, 0x30, 0x4A));
        StyleColorTile(BtToggleBtn, BtBadge, BtIcon, BtStateLbl, _radioStatus.BluetoothOn, _radioStatus.BluetoothAvailable ? (_radioStatus.BluetoothOn ? "On" : "Off") : "N/A", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x16, 0x30, 0x4A));
    }

    private void StyleColorTile(Button btn, Border badge, Path icon, TextBlock stateLbl, bool active, string stateText, Color badgeAccent, Color tileTint)
    {
        stateLbl.Text = stateText;
        if (active)
        {
            btn.Background = IsLight ? new SolidColorBrush(Color.FromArgb(0x22, badgeAccent.R, badgeAccent.G, badgeAccent.B)) : new SolidColorBrush(tileTint);
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, badgeAccent.R, badgeAccent.G, badgeAccent.B));
            badge.Background = new SolidColorBrush(badgeAccent);
            icon.Stroke = Brushes.White;
        }
        else
        {
            btn.Background = GetCardSurfaceBrush();
            btn.BorderBrush = GetCardBorderBrush();
            badge.Background = GetSubtleSurfaceBrush();
            icon.Stroke = GetPrimaryBrush();
        }
    }

    private async void WifiToggle_Click(object sender, RoutedEventArgs e) { await _toggles.ToggleRadioAsync(RadioKind.WiFi); await RefreshRadiosAsync(); }
    private async void BtToggle_Click(object sender, RoutedEventArgs e) { await _toggles.ToggleRadioAsync(RadioKind.Bluetooth); await RefreshRadiosAsync(); }

    private void PowerPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && Enum.TryParse<PowerPlanMode>(s, out var mode))
        {
            PowerPlanService.SetActive(mode);
            _powerPlan = PowerPlanService.GetActive();
            UpdatePowerPlanUi();
        }
    }

    private void UpdatePowerPlanUi()
    {
        StylePowerPill(PwrBatteryBtn, PwrBatteryLbl, _powerPlan == PowerPlanMode.Battery);
        StylePowerPill(PwrBalancedBtn, PwrBalancedLbl, _powerPlan == PowerPlanMode.Balanced);
        StylePowerPill(PwrPerfBtn, PwrPerfLbl, _powerPlan == PowerPlanMode.Performance);
    }

    private void StylePowerPill(Button btn, TextBlock lbl, bool active)
    {
        if (active)
        {
            btn.Background = IsLight ? new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)) : new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF2));
            lbl.Foreground = IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12));
        }
        else
        {
            btn.Background = Brushes.Transparent;
            lbl.Foreground = GetSecondaryBrush();
        }
    }

    private void ToggleGlobalMicMute(bool showToast)
    {
        _isMicMuted = _audio.ToggleMicMute();
        UpdateMicUi(); UpdateCollapsedSlots();
        if (showToast && !IsOpen)
            ShowRichToast(_isMicMuted ? Icons.MicOff : Icons.Mic,
                _isMicMuted ? "Microphone Muted" : "Microphone Live",
                "Global input", _isMicMuted ? "OFF" : "ON",
                _isMicMuted ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x30, 0xD1, 0x58), 1800);
    }

    private void SyncAudioUi() { _isMicMuted = _audio.GetMicMuted(); _isVolMuted = _audio.GetMasterMute(); UpdateVolumeVisuals(_isVolMuted ? 0f : _audio.GetMasterVolume()); UpdateMicUi(); }

    private void UpdateMicUi()
    {
        CollapsedMicMutedIcon.Visibility = _isMicMuted ? Visibility.Visible : Visibility.Collapsed;
        QuickMicIcon.Data = _isMicMuted ? Icons.MicOff : Icons.Mic;
        CtrlMicIcon.Data = _isMicMuted ? Icons.MicOff : Icons.Mic;

        if (_isMicMuted)
        {
            var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x52));
            QuickMicBtn.Background = new SolidColorBrush(Color.FromArgb(0x34, 0xFF, 0x45, 0x3A));
            QuickMicIcon.Stroke = red; QuickMicLbl.Foreground = red; QuickMicLbl.Text = "Muted";
            StyleColorTile(CtrlMicBtn, MicBadge, CtrlMicIcon, CtrlMicLbl, true, "Muted", Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0x42, 0x1C, 0x1E));
        }
        else
        {
            var fg = GetPrimaryBrush();
            QuickMicBtn.Background = GetSubtleSurfaceBrush(); QuickMicIcon.Stroke = fg; QuickMicLbl.Foreground = fg; QuickMicLbl.Text = _currentPrivacy.MicInUse ? "Live" : "Mic";
            if (_currentPrivacy.MicInUse)
                StyleColorTile(CtrlMicBtn, MicBadge, CtrlMicIcon, CtrlMicLbl, true, _currentPrivacy.ActiveMicApp ?? "In use", Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x3F, 0x2B, 0x12));
            else
                StyleColorTile(CtrlMicBtn, MicBadge, CtrlMicIcon, CtrlMicLbl, false, "Live", Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x22, 0x22, 0x28));
        }
    }

    private void UpdateVolumeVisuals(float scalar)
    {
        float clamped = Math.Clamp(scalar, 0f, 1f);
        int pct = (int)Math.Round(clamped * 100);

        // 1. Tall Vertical Volume Capsule (Screenshot 2!)
        double trackH = VolSliderTrack.ActualHeight > 20 ? VolSliderTrack.ActualHeight - 2 : 156;
        VolSliderFill.Height = Math.Max(0, trackH * clamped);
        VolPercentLbl.Text = _isVolMuted ? "0%" : $"{pct}%";
        VolPercentLbl.Foreground = clamped > 0.78 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : GetPrimaryBrush();
        VolSliderIcon.Data = _isVolMuted || pct == 0 ? Icons.VolumeMuted : Icons.Volume;
        VolSliderIcon.Stroke = clamped > 0.20 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : GetPrimaryBrush();

        // 2. Inline Mini Volume Bar in Media View (Screenshot 3!)
        MiniVolFill.Width = Math.Max(0, 112.0 * clamped);
        QuickVolIcon.Data = _isVolMuted || pct == 0 ? Icons.VolumeMuted : Icons.Volume;

        // 3. Mute Tile in Control Center
        CtrlMuteIcon.Data = _isVolMuted ? Icons.VolumeMuted : Icons.Volume;
        StyleColorTile(CtrlMuteBtn, MuteBadge, CtrlMuteIcon, CtrlMuteState, _isVolMuted, _isVolMuted ? "Muted" : "Sound on", Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0x42, 0x1C, 0x1E));
    }

    private void SyncBrightnessUi()
    {
        if (_brightness.TryGetBrightness(out int pct)) { BrightSliderTrack.Opacity = 1.0; UpdateBrightnessVisual(pct / 100.0); }
        else
        {
            BrightSliderTrack.Opacity = 0.45; BrightSliderFill.Height = 0; BrightPercentLbl.Text = "Ext";
            BrightPercentLbl.Foreground = GetPrimaryBrush(); BrightSliderIcon.Stroke = GetPrimaryBrush();
        }
    }

    private void UpdateBrightnessVisual(double ratio)
    {
        double clamped = Math.Clamp(ratio, 0, 1);
        double trackH = BrightSliderTrack.ActualHeight > 20 ? BrightSliderTrack.ActualHeight - 2 : 156;
        BrightSliderFill.Height = Math.Max(0, trackH * clamped);
        BrightPercentLbl.Text = $"{(int)Math.Round(clamped * 100)}%";
        BrightPercentLbl.Foreground = clamped > 0.78 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : GetPrimaryBrush();
        BrightSliderIcon.Stroke = clamped > 0.20 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : GetPrimaryBrush();
    }

    // ── Themes & Visual Styling ─────────────────────────────────────────────

    private bool IsLight => _settings.Theme == NotchTheme.Frost;
    private SolidColorBrush GetPrimaryBrush() => new(IsLight ? Color.FromRgb(0x11, 0x12, 0x16) : Color.FromArgb(0xF6, 0xFF, 0xFF, 0xFF));
    private SolidColorBrush GetSecondaryBrush() => new(IsLight ? Color.FromArgb(0x96, 0x11, 0x12, 0x16) : Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
    private SolidColorBrush GetSubtleSurfaceBrush() => new(IsLight ? Color.FromArgb(0x14, 0x00, 0x00, 0x00) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private Brush GetCardSurfaceBrush() => (Brush)FindResource(IsLight ? "FrostFaceLight" : "FrostFaceDark");
    private Brush GetCardBorderBrush() => (Brush)FindResource(IsLight ? "FrostRimLight" : "FrostRimDark");

    private void ApplyTheme()
    {
        LinearGradientBrush sheen = new(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90);
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 0.35));
        sheen.Freeze();
        ShellSheen.Fill = sheen;

        switch (_settings.Theme)
        {
            case NotchTheme.AlbumSync:
                ApplyAlbumSyncShell();
                break;
            case NotchTheme.LiquidGlass:
                var lg = new LinearGradientBrush(Color.FromArgb(0xBA, 0x1B, 0x1F, 0x2B), Color.FromArgb(0xD2, 0x0B, 0x0C, 0x12), 90); lg.Freeze();
                var lgRim = new LinearGradientBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 90); lgRim.Freeze();
                ShellFill.Fill = lg; ShellBorder.Stroke = lgRim; ShellSheen.Opacity = 0.90;
                break;
            case NotchTheme.Graphite:
                var graph = new LinearGradientBrush(Color.FromRgb(0x21, 0x22, 0x2A), Color.FromRgb(0x14, 0x15, 0x1B), 90); graph.Freeze();
                ShellFill.Fill = graph; ShellBorder.Stroke = new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)); ShellSheen.Opacity = 0.5;
                break;
            case NotchTheme.Midnight:
                var mid = new LinearGradientBrush(Color.FromArgb(0xEC, 0x0F, 0x19, 0x2F), Color.FromArgb(0xF4, 0x08, 0x0D, 0x1A), 90); mid.Freeze();
                ShellFill.Fill = mid; ShellBorder.Stroke = new SolidColorBrush(Color.FromArgb(0x3A, 0x7A, 0xB4, 0xFF)); ShellSheen.Opacity = 0.6;
                break;
            case NotchTheme.CandyRose:
                var rose = new LinearGradientBrush(Color.FromArgb(0xEC, 0x28, 0x16, 0x28), Color.FromArgb(0xF4, 0x14, 0x0C, 0x16), 90); rose.Freeze();
                ShellFill.Fill = rose; ShellBorder.Stroke = new SolidColorBrush(Color.FromArgb(0x3E, 0xFF, 0x9E, 0xCF)); ShellSheen.Opacity = 0.6;
                break;
            case NotchTheme.Frost:
                var frost = new LinearGradientBrush(Color.FromArgb(0xF0, 0xF7, 0xF8, 0xFC), Color.FromArgb(0xF6, 0xEA, 0xED, 0xF4), 90); frost.Freeze();
                ShellFill.Fill = frost; ShellBorder.Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00)); ShellSheen.Opacity = 0.22;
                break;
            default: // Obsidian
                var obs = new LinearGradientBrush(Color.FromRgb(0x0B, 0x0B, 0x0F), Color.FromRgb(0x05, 0x05, 0x07), 90); obs.Freeze();
                ShellFill.Fill = obs; ShellBorder.Stroke = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)); ShellSheen.Opacity = 0.35;
                break;
        }

        ShellBorder.Visibility = _settings.ShowNotchOutline ? Visibility.Visible : Visibility.Collapsed;

        var primary = GetPrimaryBrush(); var secondary = GetSecondaryBrush();
        var cardBg = GetCardSurfaceBrush(); var cardBd = GetCardBorderBrush();

        foreach (var card in new[] { LyricsDrawer, WidgetCalendarCard, WidgetFocusRingCard, WidgetWeatherCard, WidgetSparkCard, WidgetClockCard, WidgetTodoCard, WidgetSystemCard, CtrlBatCard })
        { card.Background = cardBg; card.BorderBrush = cardBd; }

        PowerBarWrap.Background = cardBg; PowerBarWrap.BorderBrush = cardBd;
        TopDivider.Background = GetSubtleSurfaceBrush();
        SettingsGearIco.Stroke = secondary;
        ThemePaletteIco.Stroke = secondary;
        ThemeCycleBtn.Background = cardBg; ThemeCycleBtn.BorderBrush = cardBd;

        CollapsedClockLeft.Foreground = primary; CollapsedClockMeridiem.Foreground = secondary; CollapsedClockRight.Foreground = primary;
        CollapsedTrackLbl.Foreground = primary;
        CollapsedRightDetail.Foreground = primary; CollapsedRightDetailWrap.Background = GetSubtleSurfaceBrush();
        CollapsedWeather.Background = GetSubtleSurfaceBrush(); CollapsedWeatherLbl.Foreground = primary; CollapsedWeatherIcon.Stroke = primary;
        MediaTitleLbl.Foreground = primary;
        if (_currentMedia == null || IsLight) MediaArtistLbl.Foreground = secondary;
        TimeElapsedLbl.Foreground = secondary; TimeDurationLbl.Foreground = secondary;
        PrevIcon.Fill = secondary; NextIcon.Fill = secondary; PlayPauseIcon.Fill = primary;
        WaveThumb.Background = primary; MiniVolFill.Background = primary; QuickVolIcon.Stroke = secondary;
        WaveRemLine.Stroke = new SolidColorBrush(IsLight ? Color.FromArgb(0x34, 0x00, 0x00, 0x00) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

        WidgetTimeNum.Foreground = primary; WidgetTimeMeridiem.Foreground = secondary; WidgetDayLbl.Foreground = secondary;
        WorldClockGlobe.Stroke = secondary; WorldClockCity.Foreground = secondary; WorldClockTime.Foreground = secondary;
        TodoTitleLbl.Foreground = primary; TodoCountLbl.Foreground = secondary; TodoPlusIcon.Stroke = primary;
        CalBigDayLbl.Foreground = primary; CalDowLbl.Foreground = primary; CalMonthLbl.Foreground = secondary; CalWeekLbl.Foreground = secondary;
        WeatherTempLbl.Foreground = primary; WeatherCondLbl.Foreground = secondary; WeatherHiLoLbl.Foreground = secondary; WeatherCardIcon.Stroke = primary;
        WeatherDivider.Background = GetSubtleSurfaceBrush();
        SparkCpuLbl.Foreground = secondary; SparkRamLbl.Foreground = secondary; SparkBatLbl.Foreground = secondary;
        SparkCpuVal.Foreground = primary; SparkRamVal.Foreground = primary; CpuSparkPath.Stroke = primary; CpuSparkDot.Fill = primary;
        NetDownLbl.Foreground = secondary; NetUpLbl.Foreground = secondary;
        SystemHeaderLbl.Foreground = secondary; CpuRingLbl.Foreground = primary; RamRingLbl.Foreground = primary; CpuRingArc.Stroke = primary; RamRingArc.Stroke = primary;
        CpuRingCap.Foreground = secondary; RamRingCap.Foreground = secondary; BatRingCap.Foreground = secondary;
        MiniFocusLabel.Foreground = primary; MiniFocusTimeLbl.Foreground = primary; MiniFocusBtnLbl.Foreground = secondary; FocusResetIcon.Stroke = secondary;
        FocusMinusIcon.Stroke = primary; FocusPlusIcon.Stroke = primary; FocusMinusBtn.Background = GetSubtleSurfaceBrush(); FocusPlusBtn.Background = GetSubtleSurfaceBrush();
        WidgetFlipBtn.Background = IsLight ? new SolidColorBrush(Color.FromArgb(0xEC, 0xEE, 0xF0, 0xF6)) : new SolidColorBrush(Color.FromArgb(0xEB, 0x1B, 0x1C, 0x22));
        WidgetFlipBtn.BorderBrush = cardBd; WidgetFlipIcon.Stroke = primary;
        CaptureShotIcon.Stroke = primary; CaptureShotLbl.Foreground = primary;
        CaptureSnipIcon.Stroke = primary; CaptureSnipLbl.Foreground = primary;
        CaptureRecLbl.Foreground = primary;
        SitesHintLbl.Foreground = secondary; SiteNameInput.Foreground = primary; SiteUrlInput.Foreground = primary;
        NoteInput.Foreground = primary; NoteInput.CaretBrush = primary; NotePlaceholder.Foreground = secondary;
        LyricCurrLbl.Foreground = primary; LyricPrevLbl.Foreground = secondary; LyricNextLbl.Foreground = secondary;

        foreach (var t in new[] { WifiTitleLbl, BtTitleLbl, CtrlMicTitle, CtrlFocusTitle, CtrlMuteTitle, CtrlBatPctLbl }) t.Foreground = primary;
        foreach (var s in new[] { WifiStateLbl, BtStateLbl, CtrlMicLbl, CtrlFocusState, CtrlMuteState, CtrlStatusLbl }) s.Foreground = secondary;

        ClipClearLbl.Foreground = secondary; ClipEmptyLbl.Foreground = secondary;
        SlimPill.Background = new SolidColorBrush(IsLight ? Color.FromArgb(0x58, 0x11, 0x12, 0x16) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

        UpdateTabUi(animate: false);
        UpdateLyricsBtnStyle();
        UpdatePinUi();
        UpdateFocusUi();
        UpdateMicUi();
        UpdateRadioUi();
        UpdatePowerPlanUi();
        SyncAudioUi();
        SyncBrightnessUi();
        RebuildCalendarStrip(DateTime.Now);
        RebuildClipboardList();
        RebuildNotesList();
        RebuildAppsGrid();
        RebuildSitesGrid();
    }

    private void ApplyAlbumSyncShell()
    {
        if (_currentMedia == null)
        {
            var fallback = new LinearGradientBrush(Color.FromArgb(0xCC, 0x1A, 0x1E, 0x2A), Color.FromArgb(0xE2, 0x0B, 0x0C, 0x12), 90);
            fallback.Freeze();
            var fbRim = new LinearGradientBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 90);
            fbRim.Freeze();
            ShellFill.Fill = fallback;
            ShellBorder.Stroke = fbRim;
            ShellSheen.Opacity = 0.85;
            return;
        }

        Color p = _currentMedia.AccentColor;
        Color s = _currentMedia.SecondaryColor;

        // Stronger rich artwork immersion on the closed pill and Media tab; slightly deeper velvet on utility tabs.
        bool fullImmersion = !IsOpen || _activeTab == ActiveTab.Media;
        double k1 = fullImmersion ? 0.48 : 0.28;
        double k2 = fullImmersion ? 0.38 : 0.22;

        Color cTopLeft = Color.FromArgb(0xEB,
            (byte)Math.Clamp(p.R * k1 + 14, 14, 150),
            (byte)Math.Clamp(p.G * k1 + 14, 14, 150),
            (byte)Math.Clamp(p.B * k1 + 18, 16, 155));
        Color cMid = Color.FromArgb(0xF0,
            (byte)Math.Clamp((p.R * 0.5 + s.R * 0.5) * (k1 * 0.78) + 10, 10, 120),
            (byte)Math.Clamp((p.G * 0.5 + s.G * 0.5) * (k1 * 0.78) + 10, 10, 120),
            (byte)Math.Clamp((p.B * 0.5 + s.B * 0.5) * (k1 * 0.78) + 14, 12, 125));
        Color cBotRight = Color.FromArgb(0xF5,
            (byte)Math.Clamp(s.R * k2 + 10, 10, 118),
            (byte)Math.Clamp(s.G * k2 + 10, 10, 118),
            (byte)Math.Clamp(s.B * k2 + 14, 12, 126));

        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        bg.GradientStops.Add(new GradientStop(cTopLeft, 0.0));
        bg.GradientStops.Add(new GradientStop(cMid, 0.52));
        bg.GradientStops.Add(new GradientStop(cBotRight, 1.0));
        bg.Freeze();

        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x8A, (byte)Math.Min(255, p.R * 0.65 + 95), (byte)Math.Min(255, p.G * 0.65 + 95), (byte)Math.Min(255, p.B * 0.65 + 95)), 0.0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x3A, (byte)Math.Min(255, s.R * 0.60 + 80), (byte)Math.Min(255, s.G * 0.60 + 80), (byte)Math.Min(255, s.B * 0.60 + 80)), 1.0));
        rim.Freeze();

        ShellFill.Fill = bg;
        ShellBorder.Stroke = rim;
        ShellSheen.Opacity = fullImmersion ? 0.92 : 0.65;
    }

    // ── Top Icon Strip Active State ─────────────────────────────────────────

    private void UpdateTabUi(bool animate = true)
    {
        MediaPanel.Visibility = _activeTab == ActiveTab.Media ? Visibility.Visible : Visibility.Collapsed;
        SitesPanel.Visibility = _activeTab == ActiveTab.Sites ? Visibility.Visible : Visibility.Collapsed;
        WidgetsPanel.Visibility = _activeTab == ActiveTab.Widgets ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = _activeTab == ActiveTab.Controls ? Visibility.Visible : Visibility.Collapsed;
        ClipPanel.Visibility = _activeTab == ActiveTab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = _activeTab == ActiveTab.Apps ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = GetPrimaryBrush();
        var dimBrush = GetSecondaryBrush();

        NavMediaIco.Stroke = _activeTab == ActiveTab.Media ? activeBrush : dimBrush;
        NavMediaDot.Visibility = _activeTab == ActiveTab.Media ? Visibility.Visible : Visibility.Hidden;
        NavMediaDot.Background = activeBrush;

        NavSitesIco.Stroke = _activeTab == ActiveTab.Sites ? activeBrush : dimBrush;
        NavSitesDot.Visibility = _activeTab == ActiveTab.Sites ? Visibility.Visible : Visibility.Hidden;
        NavSitesDot.Background = activeBrush;

        NavWidgetsIco.Stroke = _activeTab == ActiveTab.Widgets ? activeBrush : dimBrush;
        NavWidgetsDot.Visibility = _activeTab == ActiveTab.Widgets ? Visibility.Visible : Visibility.Hidden;
        NavWidgetsDot.Background = activeBrush;

        NavControlsIco.Stroke = _activeTab == ActiveTab.Controls ? activeBrush : dimBrush;
        NavControlsDot.Visibility = _activeTab == ActiveTab.Controls ? Visibility.Visible : Visibility.Hidden;
        NavControlsDot.Background = activeBrush;

        NavClipIco.Stroke = _activeTab == ActiveTab.Clipboard ? activeBrush : dimBrush;
        NavClipDot.Visibility = _activeTab == ActiveTab.Clipboard ? Visibility.Visible : Visibility.Hidden;
        NavClipDot.Background = activeBrush;

        NavAppsIco.Fill = _activeTab == ActiveTab.Apps ? activeBrush : dimBrush;
        NavAppsDot.Visibility = _activeTab == ActiveTab.Apps ? Visibility.Visible : Visibility.Hidden;
        NavAppsDot.Background = activeBrush;

        if (_activeTab == ActiveTab.Widgets) UpdatePerfDisplay();
        if (_activeTab == ActiveTab.Controls) { SyncAudioUi(); SyncBrightnessUi(); _powerPlan = PowerPlanService.GetActive(); UpdatePowerPlanUi(); _ = RefreshRadiosAsync(); }
        if (_activeTab == ActiveTab.Clipboard) RebuildClipboardList();
        if (_settings.Theme == NotchTheme.AlbumSync) ApplyAlbumSyncShell();

        if (animate)
        {
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ViewHost.BeginAnimation(OpacityProperty, fade);
        }

        UpdateTargets();
    }

    private void UpdateLyricsBtnStyle()
    {
        if (_lyricsOpen)
        {
            LyricsToggleBtn.Background = IsLight ? new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x16)) : Brushes.White;
            var fg = IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
            LyricsBtnIcon.Stroke = fg; LyricsBtnLbl.Foreground = fg;
        }
        else
        {
            LyricsToggleBtn.Background = GetSubtleSurfaceBrush();
            LyricsBtnIcon.Stroke = GetSecondaryBrush(); LyricsBtnLbl.Foreground = GetSecondaryBrush();
        }
    }

    private void UpdatePinUi()
    {
        PinBtn.Background = _isPinned ? (IsLight ? new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x16)) : Brushes.White) : Brushes.Transparent;
        PinIcon.Stroke = _isPinned ? (IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C))) : GetSecondaryBrush();
    }

    private void StyleSegment(Button btn, TextBlock lbl, bool active)
    {
        btn.Background = active ? (IsLight ? new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x16)) : Brushes.White) : Brushes.Transparent;
        lbl.Foreground = active ? (IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0E))) : GetSecondaryBrush();
    }

    // ── Click & Drag Handlers ───────────────────────────────────────────────

    private void TabMedia_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Media; UpdateTabUi(); }
    private void TabSites_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Sites; UpdateTabUi(); }
    private void TabWidgets_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Widgets; UpdateTabUi(); }
    private void TabControls_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Controls; UpdateTabUi(); }
    private void TabClip_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Clipboard; UpdateTabUi(); }
    private void TabApps_Click(object sender, RoutedEventArgs e) { _activeTab = ActiveTab.Apps; UpdateTabUi(); }
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

    private void ThemeCycle_Click(object sender, RoutedEventArgs e)
    {
        var values = Enum.GetValues<NotchTheme>();
        int next = (Array.IndexOf(values, _settings.Theme) + 1) % values.Length;
        _settings.Theme = values[next];
        _settings.Save();
        ApplyTheme();
    }

    private void LyricsToggle_Click(object sender, RoutedEventArgs e)
    {
        _lyricsOpen = !_lyricsOpen;
        LyricsDrawer.Visibility = _lyricsOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateLyricsBtnStyle();
        if (_lyricsOpen && _currentLyrics == null) _ = LoadLyricsForCurrentTrackAsync();
        else if (_lyricsOpen) UpdateTimelineAndLyricsUi();
        UpdateTargets();
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned; UpdatePinUi();
        if (_isPinned) { BringToTopmostNoActivate(); _edgeProbeTimer.Stop(); }
        else if (_isHovered) _hoverWatchTimer.Start();
        else _edgeProbeTimer.Start();
        UpdateTargets();
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e) => _ = _media.PreviousAsync();
    private void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => _ = _media.TogglePlayPauseAsync();
    private void NextBtn_Click(object sender, RoutedEventArgs e) => _ = _media.NextAsync();

    private void ScrubTrack_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var snap = _currentMedia;
        if (snap == null || snap.Duration.TotalSeconds <= 0.5) return;
        double ratio = Math.Clamp(e.GetPosition(ScrubTrack).X / Math.Max(1, ScrubTrack.ActualWidth), 0, 1);
        var target = TimeSpan.FromSeconds(snap.Duration.TotalSeconds * ratio);
        UpdateWavyScrubBar(ratio);
        TimeElapsedLbl.Text = FormatTime(target);
        TimeDurationLbl.Text = "-" + FormatTime(snap.Duration - target);
        _ = _media.SeekAsync(target);
    }

    private void MicMute_Click(object sender, RoutedEventArgs e) => ToggleGlobalMicMute(false);
    private void VolMute_Click(object sender, RoutedEventArgs e) { _isVolMuted = _audio.ToggleMasterMute(); UpdateVolumeVisuals(_isVolMuted ? 0f : _audio.GetMasterVolume()); }

    // Vertical Volume Capsule Drag (bottom = 0%, top = 100%)
    private void VolSlider_MouseDown(object sender, MouseButtonEventArgs e) { _draggingVol = true; VolSliderTrack.CaptureMouse(); ApplyVerticalVol(e.GetPosition(VolSliderTrack).Y); }
    private void VolSlider_MouseMove(object sender, MouseEventArgs e) { if (_draggingVol) ApplyVerticalVol(e.GetPosition(VolSliderTrack).Y); }
    private void VolSlider_MouseUp(object sender, MouseButtonEventArgs e) { if (!_draggingVol) return; _draggingVol = false; VolSliderTrack.ReleaseMouseCapture(); }
    private void ApplyVerticalVol(double y)
    {
        float ratio = (float)Math.Clamp(1.0 - (y / Math.Max(1, VolSliderTrack.ActualHeight)), 0, 1);
        _isVolMuted = false; _audio.SetMasterVolume(ratio); UpdateVolumeVisuals(ratio);
    }

    // Inline Mini Horizontal Volume Drag (Media View)
    private void MiniVol_MouseDown(object sender, MouseButtonEventArgs e) { _draggingMiniVol = true; MiniVolTrack.CaptureMouse(); ApplyMiniVol(e.GetPosition(MiniVolTrack).X); }
    private void MiniVol_MouseMove(object sender, MouseEventArgs e) { if (_draggingMiniVol) ApplyMiniVol(e.GetPosition(MiniVolTrack).X); }
    private void MiniVol_MouseUp(object sender, MouseButtonEventArgs e) { if (!_draggingMiniVol) return; _draggingMiniVol = false; MiniVolTrack.ReleaseMouseCapture(); }
    private void ApplyMiniVol(double x)
    {
        float ratio = (float)Math.Clamp(x / Math.Max(1, MiniVolTrack.ActualWidth), 0, 1);
        _isVolMuted = false; _audio.SetMasterVolume(ratio); UpdateVolumeVisuals(ratio);
    }

    // Vertical Brightness Capsule Drag (bottom = 0%, top = 100%)
    private void BrightSlider_MouseDown(object sender, MouseButtonEventArgs e) { if (!_brightness.IsSupported) return; _draggingBright = true; BrightSliderTrack.CaptureMouse(); ApplyVerticalBright(e.GetPosition(BrightSliderTrack).Y); }
    private void BrightSlider_MouseMove(object sender, MouseEventArgs e) { if (_draggingBright) ApplyVerticalBright(e.GetPosition(BrightSliderTrack).Y); }
    private void BrightSlider_MouseUp(object sender, MouseButtonEventArgs e) { if (!_draggingBright) return; _draggingBright = false; BrightSliderTrack.ReleaseMouseCapture(); }
    private void ApplyVerticalBright(double y)
    {
        double ratio = Math.Clamp(1.0 - (y / Math.Max(1, BrightSliderTrack.ActualHeight)), 0, 1);
        int pct = (int)Math.Round(ratio * 100);
        _lastKnownBrightness = pct;
        _brightness.SetBrightness(pct); UpdateBrightnessVisual(ratio);
    }
}
