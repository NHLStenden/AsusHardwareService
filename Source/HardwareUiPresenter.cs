using System.Diagnostics;
using System.Runtime.InteropServices;
using static AsusHardwareService.HardwareUiNative;

namespace AsusHardwareService;

/// <summary>
/// Computes DPI-aware layout and owns show/hide positioning and motion.
/// </summary>
internal static class HardwareUiPresenter
{
    private static int _finalX;
    private static int _finalY;
    private static int _hideOffScreenY;
    private static bool _showAnimationActive;
    private static bool _hideAnimationActive;
    private static long _animationStartTimestamp;
    private static int _animationStartY;
    private static int _animationEndY;
    private static int _animationLastPresentedY;
    private static uint _animationDurationMilliseconds;
    private static bool _animationIncoming;
    private static uint _animationGeneration;

    internal static void ShowStatusWindow(IntPtr window)
    {
        // Re-check the shell for every notification as well. This covers the narrow logoff/Explorer
        // shutdown race where the resident UI can still exist briefly after the shell has gone away.
        if (GetShellWindow() == IntPtr.Zero)
        {
            return;
        }

        var foregroundWindow = GetForegroundWindow();
        var monitor = MonitorFromWindow(
            foregroundWindow != IntPtr.Zero ? foregroundWindow : window,
            foregroundWindow != IntPtr.Zero ? MonitorDefaultToNearest : MonitorDefaultToPrimary);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            cbSize = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = GetWindowDpiForMonitor(window, monitor, ref monitorInfo);

        HardwareUiHost.WindowDpi = dpi;
        HardwareUiHost.WindowWidth = Scale(GetLogicalWindowWidth(HardwareUiHost.Notification.Kind), dpi);
        HardwareUiHost.WindowHeight = Scale(HardwareIndicatorHeightDip, dpi);
        var edgeMargin = Scale(HardwareIndicatorEdgeMarginDip, dpi);
        var workAreaWidth = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        _finalX = HardwareUiTheme.IndicatorPosition == IndicatorPosition.TopLeft
            ? monitorInfo.rcWork.Left + edgeMargin
            : monitorInfo.rcWork.Left + ((workAreaWidth - HardwareUiHost.WindowWidth) / 2);
        _finalY = HardwareUiTheme.IndicatorPosition == IndicatorPosition.BottomCenter
            ? monitorInfo.rcWork.Bottom - HardwareUiHost.WindowHeight - edgeMargin
            : monitorInfo.rcWork.Top + edgeMargin;

        // Dismiss through the physical monitor edge, not merely a fixed translation from the
        // resting position. Keep half a flyout of extra clearance so the DWM shadow/backdrop is
        // also outside the visible monitor before SW_HIDE. This clearance is an implementation
        // detail, not a WinUI design token. rcMonitor is intentional: the taskbar is part of the
        // bottom flyout's exit path.
        var offScreenVisualClearance = Math.Max(1, HardwareUiHost.WindowHeight / 2);
        _hideOffScreenY = HardwareUiTheme.IndicatorPosition == IndicatorPosition.BottomCenter
            ? monitorInfo.rcMonitor.Bottom + offScreenVisualClearance
            : monitorInfo.rcMonitor.Top - HardwareUiHost.WindowHeight - offScreenVisualClearance;

        InvalidateRect(window, IntPtr.Zero, false);
        KillTimer(window, HideTimerId);
        CancelShowAnimation(window);
        CancelHideAnimation(window);

        var alreadyVisible = IsWindowVisible(window);
        SetStatusWindowPosition(window, _finalY, show: alreadyVisible || !HardwareUiTheme.AnimationsEnabled);
        UpdateWindow(window);

        if (!alreadyVisible)
        {
            if (HardwareUiTheme.AnimationsEnabled)
            {
                StartShowAnimation(window);
            }
            else
            {
                ShowWindow(window, SwShowNoActivate);
            }
        }

        ArmHideTimer(window);
    }

    private static void StartShowAnimation(IntPtr window)
    {
        var translation = Scale(EntranceTranslationDip, HardwareUiHost.WindowDpi);
        var direction = HardwareUiTheme.IndicatorPosition == IndicatorPosition.BottomCenter ? 1 : -1;
        BeginWindowAnimation(
            window,
            _finalY + (direction * translation),
            _finalY,
            ControlFastAnimationDurationMilliseconds,
            incoming: true);
    }

    internal static void CancelShowAnimation(IntPtr window)
    {
        if (!_showAnimationActive)
        {
            return;
        }

        _showAnimationActive = false;
        SetStatusWindowPosition(window, _finalY, show: true);
    }

    internal static void HideStatusWindow(IntPtr window)
    {
        if (!IsWindowVisible(window))
        {
            return;
        }

        CancelShowAnimation(window);
        if (!HardwareUiTheme.AnimationsEnabled)
        {
            ShowWindow(window, SwHide);
            return;
        }

        BeginWindowAnimation(
            window,
            _finalY,
            _hideOffScreenY,
            ControlFastAnimationDurationMilliseconds,
            incoming: false);
    }

    internal static void CancelHideAnimation(IntPtr window)
    {
        if (!_hideAnimationActive)
        {
            return;
        }

        _hideAnimationActive = false;
        SetStatusWindowPosition(window, _finalY, show: true);
    }

    private static void BeginWindowAnimation(
        IntPtr window,
        int startY,
        int endY,
        uint durationMilliseconds,
        bool incoming)
    {
        _animationGeneration = unchecked(_animationGeneration + 1u);
        if (_animationGeneration == 0u)
        {
            _animationGeneration = 1u;
        }

        _animationStartY = startY;
        _animationEndY = endY;
        _animationLastPresentedY = startY;
        _animationDurationMilliseconds = durationMilliseconds;
        _animationIncoming = incoming;
        _showAnimationActive = incoming;
        _hideAnimationActive = !incoming;

        // Present the exact source position first, then use DwmFlush only as the display-paced
        // frame clock. DwmTransitionOwnedWindow intentionally isn't used here: that API accepts
        // neither a duration nor an easing curve, so an application timer cannot safely decide
        // when its compositor-owned transition has visually finished.
        SetStatusWindowPosition(window, startY, show: true);
        UpdateWindow(window);
        if (DwmFlush() < 0)
        {
            FinishWindowAnimation(window);
            return;
        }

        _animationStartTimestamp = Stopwatch.GetTimestamp();
        PostAnimationFrame(window, _animationGeneration);
    }

    internal static void AdvanceWindowAnimation(IntPtr window, uint generation)
    {
        if (generation != _animationGeneration ||
            (!_showAnimationActive && !_hideAnimationActive))
        {
            return;
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - _animationStartTimestamp;
        var durationTicks = Stopwatch.Frequency * (_animationDurationMilliseconds / 1000.0);
        var progress = durationTicks <= 0.0
            ? 1.0
            : Math.Clamp(elapsedTicks / durationTicks, 0.0, 1.0);
        var easedProgress = _animationIncoming
            ? EaseIncoming(progress)
            : EaseOutgoing(progress);
        var y = (int)Math.Round(
            _animationStartY + ((_animationEndY - _animationStartY) * easedProgress));
        if (y != _animationLastPresentedY)
        {
            MoveStatusWindow(window, y);
            _animationLastPresentedY = y;
        }

        if (progress >= 1.0)
        {
            FinishWindowAnimation(window);
            return;
        }

        // DwmFlush is the frame clock here: it waits for the next DWM present instead of asking a
        // low-priority WM_TIMER to approximate the display cadence. If DWM cannot pace the window,
        // finish without animation rather than spinning the UI thread or reintroducing timer jitter.
        if (DwmFlush() < 0)
        {
            FinishWindowAnimation(window);
            return;
        }

        PostAnimationFrame(window, generation);
    }

    private static void PostAnimationFrame(IntPtr window, uint generation)
    {
        PostMessage(window, WmAnimationFrame, new UIntPtr(generation), IntPtr.Zero);
    }

    private static void FinishWindowAnimation(IntPtr window)
    {
        MoveStatusWindow(window, _animationEndY);

        if (_showAnimationActive)
        {
            _showAnimationActive = false;
            SetStatusWindowPosition(window, _finalY, show: true);
            return;
        }

        if (_hideAnimationActive)
        {
            // Make the last translated position a real presented frame before hiding the HWND.
            // Otherwise the final step can be coalesced with SW_HIDE and the exit looks truncated.
            DwmFlush();
            _hideAnimationActive = false;
            ShowWindow(window, SwHide);
            SetStatusWindowPosition(window, _finalY, show: false);
        }
    }

    private static void MoveStatusWindow(IntPtr window, int y)
    {
        SetWindowPos(
            window,
            IntPtr.Zero,
            _finalX,
            y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static double EaseIncoming(double progress)
    {
        if (progress <= 0.0)
        {
            return 0.0;
        }

        if (progress >= 1.0)
        {
            return 1.0;
        }

        // Exact y(t) for cubic-bezier(0,0,0,1), after solving x=t^3 for the Bezier parameter.
        var parameter = Math.Cbrt(progress);
        return (3.0 * parameter * parameter) - (2.0 * progress);
    }

    private static double EaseOutgoing(double progress)
    {
        if (progress <= 0.0)
        {
            return 0.0;
        }

        if (progress >= 1.0)
        {
            return 1.0;
        }

        // Exact y(t) for cubic-bezier(1,0,1,1), rather than the incorrect t^3 approximation.
        var parameter = 1.0 - Math.Cbrt(1.0 - progress);
        return (3.0 * parameter * parameter) -
            (2.0 * parameter * parameter * parameter);
    }

    private static void ArmHideTimer(IntPtr window)
    {
        KillTimer(window, HideTimerId);
        SetTimer(window, HideTimerId, HideDelayMilliseconds, IntPtr.Zero);
    }

    private static void SetStatusWindowPosition(IntPtr window, int y, bool show)
    {
        SetWindowPos(
            window,
            HwndTopmost,
            _finalX,
            y,
            HardwareUiHost.WindowWidth,
            HardwareUiHost.WindowHeight,
            SwpNoActivate | (show ? SwpShowWindow : 0u));
    }

    private static uint GetWindowDpiForMonitor(IntPtr window, IntPtr monitor, ref MonitorInfo monitorInfo)
    {
        // This process is Per-Monitor V2 aware, so GetDpiForWindow returns the DPI of the monitor
        // that hosts this HWND, including the user's display scale. If the target monitor changed,
        // move the still non-activating window there first; this also avoids relying on the legacy
        // GetDpiForMonitor API from a per-monitor-aware thread.
        var currentMonitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (currentMonitor != monitor)
        {
            if (IsWindowVisible(window))
            {
                ShowWindow(window, SwHide);
            }

            SetWindowPos(
                window,
                HwndTopmost,
                monitorInfo.rcWork.Left,
                monitorInfo.rcWork.Top,
                1,
                1,
                SwpNoActivate);
        }

        var dpi = GetDpiForWindow(window);
        return dpi == 0 ? 96u : dpi;
    }

}
