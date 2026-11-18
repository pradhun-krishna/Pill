using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using NotchWin.Core;

namespace NotchWin.Services;

/// <summary>
/// 100% event-driven Win32 window state monitor using SetWinEventHook.
/// Zero polling loops, ~0.00% idle CPU. Ignores Alt+Tab / Start / Action Center
/// overlays so the notch never flickers or disappears during app switching.
/// </summary>
public sealed class WindowMonitorService : IDisposable
{
    private readonly IntPtr _selfHwnd;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _locDebounce;
    private NativeMethods.WinEventDelegate? _fgDelegate;
    private NativeMethods.WinEventDelegate? _locDelegate;
    private IntPtr _hookFg;
    private IntPtr _hookLoc;
    private bool _shouldSlim;
    private bool _isTrueFullscreen;

    public event Action<bool, bool>? SlimStateChanged;

    private static readonly HashSet<string> OverlayClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "TaskSwitcherWnd",
        "ForegroundStaging",
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TaskListThumbnailWnd",
        "Windows.UI.Input.InputSite.WindowClass",
    };

    private static readonly HashSet<string> OverlayExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "LockApp",
    };

    private static readonly HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
    };

    public WindowMonitorService(IntPtr selfHwnd, Dispatcher dispatcher)
    {
        _selfHwnd = selfHwnd;
        _dispatcher = dispatcher;
        _locDebounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _locDebounce.Tick += (_, _) =>
        {
            _locDebounce.Stop();
            Evaluate();
        };
    }

    public void Start()
    {
        if (_hookFg != IntPtr.Zero) return;

        _fgDelegate = OnForegroundChanged;
        _locDelegate = OnLocationChanged;

        _hookFg = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _fgDelegate, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _hookLoc = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locDelegate, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        Evaluate();
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Evaluate();
    }

    private void OnLocationChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        if (hwnd != NativeMethods.GetForegroundWindow()) return;
        _locDebounce.Stop();
        _locDebounce.Start();
    }

    public void Evaluate()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _selfHwnd) return;

        var clsBuf = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, clsBuf, 256);
        string cls = clsBuf.ToString();

        // Alt+Tab, Start Menu, Taskbar, Action Center -> preserve current state!
        if (OverlayClasses.Contains(cls)) return;

        // Desktop -> show full collapsed notch bar.
        if (DesktopClasses.Contains(cls))
        {
            SetSlim(false, false);
            return;
        }

        // Check system overlay exes (Start, Search, Lock screen).
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                if (OverlayExes.Contains(proc.ProcessName)) return;
            }
        }
        catch { }

        if (NativeMethods.IsIconic(hwnd))
        {
            SetSlim(false, false);
            return;
        }

        // Only react if the foreground window is on the same monitor as our notch.
        IntPtr selfMon = NativeMethods.MonitorFromWindow(_selfHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        IntPtr fgMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (selfMon != fgMon) return;

        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(fgMon, ref mi)) return;

        bool zoomed = NativeMethods.IsZoomed(hwnd);
        bool fullscreen = false;
        bool touchesTop = false;

        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            if (!zoomed)
            {
                fullscreen = rect.Left <= mi.rcMonitor.Left + 2
                          && rect.Top <= mi.rcMonitor.Top + 2
                          && rect.Right >= mi.rcMonitor.Right - 2
                          && rect.Bottom >= mi.rcMonitor.Bottom - 2;
            }
            touchesTop = zoomed || fullscreen || (rect.Top <= mi.rcWork.Top + 12 && (rect.Right - rect.Left) > 320);
        }

        SetSlim(fullscreen || zoomed || touchesTop, fullscreen);
    }

    private void SetSlim(bool slim, bool trueFullscreen)
    {
        if (slim == _shouldSlim && trueFullscreen == _isTrueFullscreen) return;
        _shouldSlim = slim;
        _isTrueFullscreen = trueFullscreen;
        _dispatcher.BeginInvoke(() => SlimStateChanged?.Invoke(slim, trueFullscreen));
    }

    public void Dispose()
    {
        _locDebounce.Stop();
        if (_hookFg != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookFg);
        if (_hookLoc != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookLoc);
        _hookFg = IntPtr.Zero;
        _hookLoc = IntPtr.Zero;
    }
}
