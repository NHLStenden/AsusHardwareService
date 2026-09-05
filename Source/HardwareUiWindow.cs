using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AsusHardwareService;

/// <summary>
/// Hosts the lightweight Win32 hardware status overlay for an interactive user session.
/// </summary>
internal static class HardwareUiWindow
{
    private const string WindowClassName = "AsusHardwareService.HardwareUiWindow";
    private const string InstanceMutexName = @"Local\AsusHardwareService.HardwareUi";
    private const string ServiceName = "ASUS Hardware Service";
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeRegistryValue = "SystemUsesLightTheme";
    private const string IndicatorPositionRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\ConfirmatorPosition";
    private const string IndicatorPositionRegistryValue = "PositionIndex";
    private const string AccentRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string AccentPaletteRegistryValue = "AccentPalette";
    private const uint ErrorAlreadyExists = 183;
    private const int ErrorSuccess = 0;

    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmSettingChange = 0x001A;
    private const uint WmSysColorChange = 0x0015;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmPaint = 0x000F;
    private const uint WmTimer = 0x0113;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmPrintClient = 0x0318;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const uint WmApp = 0x8000;
    private const uint WmMicStatusChanged = WmApp + 0x31;
    private const uint WmKeyboardBacklightChanged = WmApp + 0x32;
    private const uint WmDisplayBrightnessChanged = WmApp + 0x33;
    private const uint WmPerformanceGpuChanged = WmApp + 0x34;
    private const uint WmAnimationFrame = WmApp + 0x35;

    private const int MaNoActivate = 3;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;

    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private const int DwmTransitionOwnedWindowReposition = 0;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtNone = 1;
    private const int DwmsbtTransientWindow = 3;

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;
    // Accent-policy colours are AABBGGRR. This keeps the Shell's #2C2C2C dark Acrylic tint
    // while retaining enough backdrop contribution to avoid the old opaque/fallback look.
    private const uint DarkAcrylicGradientColor = 0xCC2C2C2C;

    private const uint SpiGetHighContrast = 0x0042;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint HcfHighContrastOn = 0x00000001;

    private const int ColorWindow = 5;
    private const int ColorWindowText = 8;
    private const int ColorHighlight = 13;

    private const uint RrfRtRegBinary = 0x00000008;
    private const uint RrfRtRegDword = 0x00000010;
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStopPending = 0x00000003;
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly UIntPtr HkeyCurrentUser = new(0x80000001u);

    private const int Transparent = 1;
    private const uint DtLeft = 0x00000000;
    private const uint DtCenter = 0x00000001;
    private const uint DtVCenter = 0x00000004;
    private const uint DtSingleLine = 0x00000020;
    private const int PsSolid = 0;
    private const int NullBrush = 5;
    private const int NullPen = 8;

    // Use the current Windows motion fast-duration token for both the direct entrance and exit.
    // 83 ms is an opacity-oriented token; using it for the full Y translation would be too abrupt.
    private const uint ShowAnimationMilliseconds = 167;
    private const uint HideAnimationMilliseconds = 167;
    private const int FlyoutAnimationDistanceDip = 20;
    // A compositor can follow the Fluent curves at fractional coordinates; an HWND cannot.
    // Limit the instantaneous HWND velocity to 1.5 times the average animation velocity so the
    // endpoint-heavy Fluent curves cannot collapse into a large whole-pixel jump.
    private const double MaxAnimationVelocityMultiplier = 1.5;
    private const uint HideDelayMilliseconds = 1500;
    // Graceful service/session changes send WM_CLOSE immediately. This low-frequency Win32
    // watchdog is only a fallback for abrupt service termination or a missed session transition.
    private const uint ServiceWatchIntervalMilliseconds = 5000;

    // Windows 11 ships these glyphs in Segoe Fluent Icons. Keep them at an optical 16-DIP size.
    private const string BrightnessGlyph = "\uE706";
    private const string KeyboardGlyph = "\uE765";
    private const string MicrophoneOffGlyph = "\uEC54";
    private const string MicrophoneOnGlyph = "\uE720";
    private const string SpeedMediumGlyph = "\uEC49";
    private const string SpeedHighGlyph = "\uEC4A";

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private static readonly UIntPtr HideTimerId = (UIntPtr)1u;
    private static readonly UIntPtr DwmAnimationTimerId = (UIntPtr)2u;
    private static readonly UIntPtr ServiceWatchTimerId = (UIntPtr)3u;
    private static readonly WindowProcedureDelegate WindowProcedureCallback = WindowProcedure;

    private static IntPtr _windowHandle;
    private static HardwareUiNotification _notification = new(HardwareUiNotificationKind.Microphone, 0);
    private static bool _isDarkTheme = true;
    private static bool _highContrast;
    private static bool _animationsEnabled = true;
    private static IndicatorPosition _indicatorPosition = IndicatorPosition.BottomCenter;
    private static int _finalX;
    private static int _finalY;
    private static int _windowWidth;
    private static int _windowHeight;
    private static uint _windowDpi = 96;
    private static bool _showAnimationActive;
    private static bool _hideAnimationActive;
    private static bool _dwmTransitionActive;
    private static bool _systemBackdropEnabled;
    private static long _animationStartTimestamp;
    private static long _animationLastFrameTimestamp;
    private static int _animationStartY;
    private static int _animationEndY;
    private static double _animationCurrentY;
    private static int _animationLastPresentedY;
    private static uint _animationDurationMilliseconds;
    private static bool _animationIncoming;
    private static uint _animationGeneration;

    /// <summary>
    /// Runs UI mode, forwarding to an existing instance or becoming the resident UI instance.
    /// </summary>
    /// <param name="initialNotification">An optional hardware status to show immediately.</param>
    /// <returns>A process exit code.</returns>
    public static int Run(HardwareUiNotification? initialNotification)
    {
        SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);

        var mutexHandle = CreateMutex(IntPtr.Zero, false, InstanceMutexName);
        var mutexLastError = Marshal.GetLastWin32Error();
        if (mutexHandle == IntPtr.Zero)
        {
            return mutexLastError;
        }

        try
        {
            if (mutexLastError == ErrorAlreadyExists)
            {
                var existingWindow = WaitForExistingWindow();
                if (existingWindow == IntPtr.Zero)
                {
                    return 3;
                }

                if (initialNotification.HasValue)
                {
                    return SendNotification(existingWindow, initialNotification.Value) ? 0 : 4;
                }

                return 0;
            }

            // The command is already running inside the interactive user session. A logged-on user
            // can exist briefly before Explorer has created the shell window; in that case do not
            // leave a resident UI process behind.
            if (GetShellWindow() == IntPtr.Zero)
            {
                return 0;
            }

            return RunMessageLoop(initialNotification);
        }
        finally
        {
            CloseHandle(mutexHandle);
        }
    }

    /// <summary>
    /// Requests that the resident UI instance in the current user session exits.
    /// </summary>
    /// <returns>A process exit code.</returns>
    public static int Shutdown()
    {
        var existingWindow = FindWindow(WindowClassName, null);
        if (existingWindow == IntPtr.Zero)
        {
            return 0;
        }

        return SendMessageTimeout(
            existingWindow,
            WmClose,
            UIntPtr.Zero,
            IntPtr.Zero,
            0x0002,
            500,
            out _) != IntPtr.Zero ? 0 : 4;
    }

    private static int RunMessageLoop(HardwareUiNotification? initialNotification)
    {
        var moduleHandle = GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = WindowProcedureCallback,
            hInstance = moduleHandle,
            lpszClassName = WindowClassName,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            return Marshal.GetLastWin32Error();
        }

        _windowHandle = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExNoActivate,
            WindowClassName,
            string.Empty,
            WsPopup,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error();
        }

        RefreshSystemPreferences();
        ConfigureWindows11Appearance(_windowHandle);
        SetTimer(_windowHandle, ServiceWatchTimerId, ServiceWatchIntervalMilliseconds, IntPtr.Zero);

        if (initialNotification.HasValue)
        {
            PostNotification(_windowHandle, initialNotification.Value);
        }

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        return 0;
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmMicStatusChanged:
                _notification = new HardwareUiNotification(
                    HardwareUiNotificationKind.Microphone,
                    wParam == UIntPtr.Zero ? 0 : 1);
                ShowStatusWindow(window);
                return IntPtr.Zero;

            case WmKeyboardBacklightChanged:
                var keyboardLevel = (int)wParam.ToUInt64();
                if (keyboardLevel is >= 0 and <= 3)
                {
                    _notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.KeyboardBacklight,
                        keyboardLevel);
                    ShowStatusWindow(window);
                }
                return IntPtr.Zero;

            case WmDisplayBrightnessChanged:
                var brightness = (int)wParam.ToUInt64();
                if (brightness is >= 0 and <= 100)
                {
                    _notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.DisplayBrightness,
                        brightness);
                    ShowStatusWindow(window);
                }
                return IntPtr.Zero;

            case WmPerformanceGpuChanged:
                var performanceGpuMode = (int)wParam.ToUInt64();
                if (performanceGpuMode is >= 0 and <= 3)
                {
                    _notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.PerformanceGpuMode,
                        performanceGpuMode);
                    ShowStatusWindow(window);
                }
                return IntPtr.Zero;

            case WmTimer:
                if (wParam == ServiceWatchTimerId)
                {
                    if (ShouldResidentUiExit())
                    {
                        PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
                    }
                    return IntPtr.Zero;
                }

                if (wParam == DwmAnimationTimerId)
                {
                    KillTimer(window, DwmAnimationTimerId);
                    _dwmTransitionActive = false;
                    FinishWindowAnimation(window);
                    return IntPtr.Zero;
                }

                if (wParam == HideTimerId)
                {
                    KillTimer(window, HideTimerId);
                    HideStatusWindow(window);
                    return IntPtr.Zero;
                }

                break;

            case WmAnimationFrame:
                AdvanceWindowAnimation(window, unchecked((uint)wParam.ToUInt64()));
                return IntPtr.Zero;

            case WmSettingChange:
            case WmSysColorChange:
                RefreshSystemPreferences();
                ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmDwmCompositionChanged:
                ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmEraseBackground:
                // WM_PAINT redraws the whole compact surface, avoiding an extra erase/flicker pass.
                return new IntPtr(1);

            case WmPaint:
                PaintStatus(window);
                return IntPtr.Zero;

            case WmPrintClient:
                DrawStatus(window, new IntPtr(unchecked((long)wParam.ToUInt64())));
                return IntPtr.Zero;

            case WmMouseActivate:
                return new IntPtr(MaNoActivate);

            case WmClose:
                KillTimer(window, HideTimerId);
                KillTimer(window, DwmAnimationTimerId);
                CancelShowAnimation(window);
                CancelHideAnimation(window);
                KillTimer(window, ServiceWatchTimerId);
                DestroyWindow(window);
                return IntPtr.Zero;

            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private static void ShowStatusWindow(IntPtr window)
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

        _windowDpi = dpi;
        _windowWidth = Scale(GetLogicalWindowWidth(_notification.Kind), dpi);
        _windowHeight = Scale(48, dpi);
        var edgeMargin = Scale(16, dpi);
        var workAreaWidth = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        _finalX = _indicatorPosition == IndicatorPosition.TopLeft
            ? monitorInfo.rcWork.Left + edgeMargin
            : monitorInfo.rcWork.Left + ((workAreaWidth - _windowWidth) / 2);
        _finalY = _indicatorPosition == IndicatorPosition.BottomCenter
            ? monitorInfo.rcWork.Bottom - _windowHeight - edgeMargin
            : monitorInfo.rcWork.Top + edgeMargin;

        InvalidateRect(window, IntPtr.Zero, false);
        KillTimer(window, HideTimerId);
        CancelShowAnimation(window);
        CancelHideAnimation(window);

        var alreadyVisible = IsWindowVisible(window);
        SetStatusWindowPosition(window, _finalY, show: alreadyVisible || !_animationsEnabled);
        UpdateWindow(window);

        if (!alreadyVisible)
        {
            if (_animationsEnabled)
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
        var distance = Scale(FlyoutAnimationDistanceDip, _windowDpi);
        var direction = _indicatorPosition == IndicatorPosition.BottomCenter ? 1 : -1;
        BeginWindowAnimation(
            window,
            _finalY + (direction * distance),
            _finalY,
            ShowAnimationMilliseconds,
            incoming: true);
    }

    private static void CancelShowAnimation(IntPtr window)
    {
        if (!_showAnimationActive)
        {
            return;
        }

        CancelDwmTransition(window);
        _showAnimationActive = false;
        SetStatusWindowPosition(window, _finalY, show: true);
    }

    private static void HideStatusWindow(IntPtr window)
    {
        if (!IsWindowVisible(window))
        {
            return;
        }

        CancelShowAnimation(window);
        if (!_animationsEnabled)
        {
            ShowWindow(window, SwHide);
            return;
        }

        BeginWindowAnimation(
            window,
            _finalY,
            GetOffScreenHideY(window),
            HideAnimationMilliseconds,
            incoming: false);
    }


    private static int GetOffScreenHideY(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            cbSize = (uint)Marshal.SizeOf<MonitorInfo>(),
        };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            // Windows motion uses the fast 167 ms exit token for transient UI. The DWM
            // reposition transition does not expose a duration, so aim its destination a little
            // beyond the physical monitor edge. This makes the flyout fully clear the screen
            // during the visible part of the transition instead of appearing to linger at the edge.
            // Use rcMonitor rather than rcWork so the taskbar remains part of the exit path.
            var overshoot = Math.Max(1, _windowHeight / 2);
            return _indicatorPosition == IndicatorPosition.BottomCenter
                ? monitorInfo.rcMonitor.Bottom + overshoot
                : monitorInfo.rcMonitor.Top - _windowHeight - overshoot;
        }

        // Keep the previous 20-DIP endpoint as a defensive fallback if monitor information
        // cannot be obtained.
        var distance = Scale(FlyoutAnimationDistanceDip, _windowDpi);
        var direction = _indicatorPosition == IndicatorPosition.BottomCenter ? 1 : -1;
        return _finalY + (direction * distance);
    }

    private static void CancelHideAnimation(IntPtr window)
    {
        if (!_hideAnimationActive)
        {
            return;
        }

        CancelDwmTransition(window);
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
        _animationCurrentY = startY;
        _animationLastPresentedY = startY;
        _animationDurationMilliseconds = durationMilliseconds;
        _animationIncoming = incoming;
        _showAnimationActive = incoming;
        _hideAnimationActive = !incoming;

        // DwmTransitionOwnedWindow is the smallest native path that asks DWM itself to coordinate
        // a tool-window reposition. Keep the existing sampled HWND animation as a fallback for
        // systems where the transition API is unavailable or rejects this window.
        if (TryBeginDwmTransitionAnimation(window, startY, endY, durationMilliseconds))
        {
            return;
        }

        // Fallback: keep the normal DWM-backed HWND intact. Present the exact first position before
        // the high-resolution clock starts so the first animation sample is not skipped.
        SetStatusWindowPosition(window, startY, show: true);
        UpdateWindow(window);
        if (DwmFlush() < 0)
        {
            FinishWindowAnimation(window);
            return;
        }

        _animationStartTimestamp = Stopwatch.GetTimestamp();
        _animationLastFrameTimestamp = _animationStartTimestamp;
        PostAnimationFrame(window, _animationGeneration);
    }

    private static bool TryBeginDwmTransitionAnimation(
        IntPtr window,
        int startY,
        int endY,
        uint durationMilliseconds)
    {
        // Commit the source rect first. DWM can then transition the tool window from that
        // presented rect to the single reposition below instead of us moving it every frame.
        SetStatusWindowPosition(window, startY, show: true);
        UpdateWindow(window);
        if (DwmFlush() < 0)
        {
            return false;
        }

        MoveStatusWindow(window, endY);
        if (DwmTransitionOwnedWindow(window, DwmTransitionOwnedWindowReposition) < 0)
        {
            MoveStatusWindow(window, startY);
            return false;
        }

        _dwmTransitionActive = true;
        if (SetTimer(window, DwmAnimationTimerId, durationMilliseconds, IntPtr.Zero) == UIntPtr.Zero)
        {
            _dwmTransitionActive = false;
            FinishWindowAnimation(window);
        }

        return true;
    }

    private static void CancelDwmTransition(IntPtr window)
    {
        if (!_dwmTransitionActive)
        {
            return;
        }

        KillTimer(window, DwmAnimationTimerId);
        _dwmTransitionActive = false;
    }

    private static void AdvanceWindowAnimation(IntPtr window, uint generation)
    {
        if (generation != _animationGeneration ||
            (!_showAnimationActive && !_hideAnimationActive))
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = now - _animationStartTimestamp;
        var frameTicks = Math.Max(0L, now - _animationLastFrameTimestamp);
        _animationLastFrameTimestamp = now;

        var durationSeconds = _animationDurationMilliseconds / 1000.0;
        var durationTicks = Stopwatch.Frequency * durationSeconds;
        var progress = durationTicks <= 0.0
            ? 1.0
            : Math.Clamp(elapsedTicks / durationTicks, 0.0, 1.0);
        var easedProgress = _animationIncoming
            ? EaseIncoming(progress)
            : EaseOutgoing(progress);
        var desiredY = _animationStartY +
            ((_animationEndY - _animationStartY) * easedProgress);

        // The Fluent easing curves are designed for compositor transforms, where fractional
        // positions are cheap. SetWindowPos accepts whole pixels only, so sampling the curves
        // directly can turn their endpoint velocity into a visible multi-pixel jump, especially
        // at 60 Hz and higher DPI. Keep a fractional position and velocity-limit only the HWND
        // adaptation; the requested curve remains the source of the desired position.
        var frameSeconds = frameTicks / (double)Stopwatch.Frequency;
        var animationDistance = Math.Abs(_animationEndY - _animationStartY);
        var averageVelocity = durationSeconds <= 0.0
            ? double.PositiveInfinity
            : animationDistance / durationSeconds;
        var maxFrameTravel = averageVelocity *
            MaxAnimationVelocityMultiplier * frameSeconds;

        if (durationSeconds <= 0.0)
        {
            _animationCurrentY = desiredY;
        }
        else if (maxFrameTravel > 0.0)
        {
            _animationCurrentY = MoveToward(
                _animationCurrentY,
                desiredY,
                maxFrameTravel);
        }

        var y = (int)Math.Round(_animationCurrentY);
        if (y != _animationLastPresentedY)
        {
            MoveStatusWindow(window, y);
            _animationLastPresentedY = y;
        }

        if (progress >= 1.0 && Math.Abs(_animationCurrentY - _animationEndY) < 0.5)
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

    private static double MoveToward(double current, double target, double maxDistance)
    {
        var remaining = target - current;
        if (Math.Abs(remaining) <= maxDistance)
        {
            return target;
        }

        return current + (Math.Sign(remaining) * maxDistance);
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
            _windowWidth,
            _windowHeight,
            SwpNoActivate | (show ? SwpShowWindow : 0u));
    }

    private static void PaintStatus(IntPtr window)
    {
        var deviceContext = BeginPaint(window, out var paintStruct);
        if (deviceContext == IntPtr.Zero)
        {
            return;
        }

        try
        {
            DrawStatus(window, deviceContext);
        }
        finally
        {
            EndPaint(window, ref paintStruct);
        }
    }

    private static void DrawStatus(IntPtr window, IntPtr deviceContext)
    {
        if (deviceContext == IntPtr.Zero || !GetClientRect(window, out var clientRect))
        {
            return;
        }

        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = _windowDpi == 0 ? 96u : _windowDpi;
        }

        FillStatusBackground(deviceContext, ref clientRect);
        SetBkMode(deviceContext, Transparent);

        switch (_notification.Kind)
        {
            case HardwareUiNotificationKind.KeyboardBacklight:
                DrawKeyboardBacklightStatus(deviceContext, dpi, _notification.Value);
                break;

            case HardwareUiNotificationKind.DisplayBrightness:
                DrawDisplayBrightnessStatus(deviceContext, dpi, _notification.Value);
                break;

            case HardwareUiNotificationKind.PerformanceGpuMode:
                DrawPerformanceGpuStatus(deviceContext, dpi, _notification.Value);
                break;

            case HardwareUiNotificationKind.Microphone:
            default:
                DrawMicrophoneStatus(deviceContext, dpi, _notification.Value != 0);
                break;
        }
    }

    private static void FillStatusBackground(IntPtr deviceContext, ref Rect clientRect)
    {
        // A black GDI fill has zeroed pixel data on an extended DWM frame, exposing the Desktop
        // Acrylic backdrop instead of covering it with the opaque fallback colour.
        if (_systemBackdropEnabled)
        {
            var transparentBrush = CreateSolidBrush(Rgb(0, 0, 0));
            if (transparentBrush != IntPtr.Zero)
            {
                FillRect(deviceContext, ref clientRect, transparentBrush);
                DeleteObject(transparentBrush);
            }
            return;
        }

        // WinUI flyouts use these colours as the solid fallback when Acrylic cannot be shown.
        var backgroundColor = _highContrast
            ? GetSysColor(ColorWindow)
            : _isDarkTheme
                ? Rgb(44, 44, 44)
                : Rgb(249, 249, 249);
        var backgroundBrush = CreateSolidBrush(backgroundColor);
        if (backgroundBrush == IntPtr.Zero)
        {
            return;
        }

        try
        {
            FillRect(deviceContext, ref clientRect, backgroundBrush);
            if (!_highContrast)
            {
                // SurfaceStrokeColorFlyout is translucent in WinUI. Pre-blend it against the
                // fallback fill because classic GDI pens do not carry an alpha channel.
                var contourColor = _isDarkTheme ? Rgb(35, 35, 35) : Rgb(234, 234, 234);
                DrawRoundRectOutline(
                    deviceContext,
                    0,
                    0,
                    clientRect.Right - 1,
                    clientRect.Bottom - 1,
                    Scale(16, _windowDpi), // RoundRect expects the corner ellipse diameter.
                    contourColor);
            }
        }
        finally
        {
            DeleteObject(backgroundBrush);
        }
    }

    private static void DrawMicrophoneStatus(IntPtr deviceContext, uint dpi, bool muted)
    {
        var foreground = GetPrimaryTextColor();
        DrawMicrophoneIcon(deviceContext, dpi, foreground, muted);
        DrawPrimaryText(
            deviceContext,
            dpi,
            muted ? "Microphone muted" : "Microphone unmuted",
            Scale(42, dpi),
            0,
            _windowWidth - Scale(12, dpi),
            _windowHeight);
    }

    private static void DrawMicrophoneIcon(
        IntPtr deviceContext,
        uint dpi,
        uint color,
        bool muted)
    {
        DrawFluentIcon(
            deviceContext,
            dpi,
            muted ? MicrophoneOffGlyph : MicrophoneOnGlyph,
            color);
    }

    private static void DrawKeyboardBacklightStatus(IntPtr deviceContext, uint dpi, int level)
    {
        var foreground = GetPrimaryTextColor();
        DrawKeyboardIcon(deviceContext, dpi, foreground);
        DrawLevelTrack(deviceContext, dpi, Math.Clamp(level, 0, 3) / 3.0);
        DrawLevelValue(deviceContext, dpi, Math.Clamp(level, 0, 3).ToString());
    }

    private static void DrawKeyboardIcon(IntPtr deviceContext, uint dpi, uint color)
    {
        DrawFluentIcon(deviceContext, dpi, KeyboardGlyph, color);
    }

    private static void DrawDisplayBrightnessStatus(IntPtr deviceContext, uint dpi, int brightness)
    {
        var foreground = GetPrimaryTextColor();
        DrawSunIcon(deviceContext, dpi, foreground);
        DrawLevelTrack(deviceContext, dpi, Math.Clamp(brightness, 0, 100) / 100.0);
        DrawLevelValue(deviceContext, dpi, Math.Clamp(brightness, 0, 100).ToString());
    }

    private static void DrawSunIcon(IntPtr deviceContext, uint dpi, uint color)
    {
        DrawFluentIcon(deviceContext, dpi, BrightnessGlyph, color);
    }

    private static void DrawPerformanceGpuStatus(IntPtr deviceContext, uint dpi, int modeValue)
    {
        var foreground = GetPrimaryTextColor();
        var silent = (modeValue & 1) != 0;
        DrawPerformanceIcon(deviceContext, dpi, foreground, silent);

        var performanceMode = silent ? "Silent" : "Performance";
        var gpuMode = (modeValue & 2) != 0 ? "Eco" : "Standard";
        DrawPrimaryText(
            deviceContext,
            dpi,
            $"{performanceMode} · {gpuMode}",
            Scale(42, dpi),
            0,
            _windowWidth - Scale(16, dpi),
            _windowHeight);
    }

    private static void DrawPerformanceIcon(
        IntPtr deviceContext,
        uint dpi,
        uint color,
        bool silent)
    {
        DrawFluentIcon(
            deviceContext,
            dpi,
            silent ? SpeedMediumGlyph : SpeedHighGlyph,
            color);
    }

    private static void DrawLevelTrack(IntPtr deviceContext, uint dpi, double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var trackColor = _highContrast
            ? GetSysColor(ColorWindowText)
            : _isDarkTheme
                ? Rgb(96, 96, 96)
                : Rgb(190, 190, 190);
        var accentColor = GetAccentColor();

        // Native HWConfirmatorUI uses a 48-DIP plate, a 32-DIP icon button with 5-DIP trailing margin, and a 40-DIP value slot.
        var trackLeft = Scale(42, dpi);
        var trackTop = Scale(22, dpi);
        var trackRight = _windowWidth - Scale(40, dpi);
        var trackBottom = Scale(26, dpi);
        DrawFilledRoundRect(
            deviceContext,
            trackLeft,
            trackTop,
            trackRight,
            trackBottom,
            Scale(4, dpi),
            trackColor);

        var trackWidth = trackRight - trackLeft;
        var progressRight = trackLeft + (int)Math.Round(trackWidth * progress);
        if (progress > 0.0)
        {
            DrawFilledRoundRect(
                deviceContext,
                trackLeft,
                trackTop,
                progressRight,
                trackBottom,
                Scale(4, dpi),
                accentColor);
        }
    }

    private static void DrawLevelValue(IntPtr deviceContext, uint dpi, string value)
    {
        DrawTextCore(
            deviceContext,
            dpi,
            value,
            _windowWidth - Scale(40, dpi),
            0,
            _windowWidth,
            _windowHeight - Scale(2, dpi),
            400,
            14,
            DtCenter);
    }

    private static int GetLogicalWindowWidth(HardwareUiNotificationKind kind)
    {
        return kind switch
        {
            HardwareUiNotificationKind.KeyboardBacklight => 200,
            HardwareUiNotificationKind.DisplayBrightness => 200,
            HardwareUiNotificationKind.Microphone => 224,
            HardwareUiNotificationKind.PerformanceGpuMode => 236,
            _ => 200,
        };
    }

    private static void DrawPrimaryText(
        IntPtr deviceContext,
        uint dpi,
        string text,
        int left,
        int top,
        int right,
        int bottom)
    {
        DrawTextCore(deviceContext, dpi, text, left, top, right, bottom, 400, 14);
    }

    private static void DrawFluentIcon(
        IntPtr deviceContext,
        uint dpi,
        string glyph,
        uint color)
    {
        var font = CreateFont(
            -Scale(16, dpi),
            0,
            0,
            0,
            400,
            false,
            false,
            false,
            1,
            0,
            0,
            5,
            0,
            "Segoe Fluent Icons");
        if (font == IntPtr.Zero)
        {
            return;
        }

        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, color);
            var iconRect = new Rect
            {
                Left = Scale(5, dpi),
                Top = 0,
                Right = Scale(37, dpi),
                Bottom = _windowHeight - Scale(1, dpi),
            };
            DrawText(
                deviceContext,
                glyph,
                -1,
                ref iconRect,
                DtCenter | DtVCenter | DtSingleLine);
        }
        finally
        {
            SelectObject(deviceContext, oldFont);
            DeleteObject(font);
        }
    }

    private static void DrawTextCore(
        IntPtr deviceContext,
        uint dpi,
        string text,
        int left,
        int top,
        int right,
        int bottom,
        int weight,
        int fontSizeDip,
        uint horizontalAlignment = DtLeft)
    {
        var font = CreateFont(
            -Scale(fontSizeDip, dpi),
            0,
            0,
            0,
            weight,
            false,
            false,
            false,
            1,
            0,
            0,
            5,
            0,
            "Segoe UI Variable Text");
        if (font == IntPtr.Zero)
        {
            return;
        }

        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, GetPrimaryTextColor());
            var textRect = new Rect
            {
                Left = left,
                Top = top,
                Right = right,
                Bottom = bottom,
            };
            DrawText(deviceContext, text, -1, ref textRect, horizontalAlignment | DtVCenter | DtSingleLine);
        }
        finally
        {
            SelectObject(deviceContext, oldFont);
            DeleteObject(font);
        }
    }

    private static void DrawRoundRectOutline(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint color)
    {
        var pen = CreatePen(PsSolid, Math.Max(1, Scale(1, _windowDpi)), color);
        if (pen == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, pen);
        var oldBrush = SelectObject(deviceContext, GetStockObject(NullBrush));
        try
        {
            RoundRect(deviceContext, left, top, right, bottom, radius, radius);
        }
        finally
        {
            SelectObject(deviceContext, oldBrush);
            SelectObject(deviceContext, oldPen);
            DeleteObject(pen);
        }
    }

    private static void DrawFilledRoundRect(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint color)
    {
        if (right <= left || bottom <= top)
        {
            return;
        }

        var brush = CreateSolidBrush(color);
        if (brush == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, GetStockObject(NullPen));
        var oldBrush = SelectObject(deviceContext, brush);
        try
        {
            RoundRect(deviceContext, left, top, right, bottom, radius, radius);
        }
        finally
        {
            SelectObject(deviceContext, oldBrush);
            SelectObject(deviceContext, oldPen);
            DeleteObject(brush);
        }
    }

    private static uint GetPrimaryTextColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorWindowText);
        }

        return _isDarkTheme ? Rgb(255, 255, 255) : Rgb(30, 30, 30);
    }

    private static uint GetAccentColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorHighlight);
        }

        // WinUI uses Light2 for normal accent fills in dark theme and Dark1 in light theme.
        // Prefer the shell-generated palette so custom Windows accent colours keep the same contrast.
        // The Explorer palette is best-effort; DWM/system highlight remains the supported fallback.
        var accentShadeIndex = _isDarkTheme ? 1 : 4;
        if (TryReadAccentPaletteColor(accentShadeIndex, out var themedAccent))
        {
            return themedAccent;
        }

        if (DwmGetColorizationColor(out var argbColor, out _) != 0)
        {
            return GetSysColor(ColorHighlight);
        }

        var red = (byte)((argbColor >> 16) & 0xff);
        var green = (byte)((argbColor >> 8) & 0xff);
        var blue = (byte)(argbColor & 0xff);
        return Rgb(red, green, blue);
    }

    private static bool TryReadAccentPaletteColor(int shadeIndex, out uint color)
    {
        color = 0;
        var palette = new byte[32];
        uint dataSize = (uint)palette.Length;
        if (RegGetValueBytes(
                HkeyCurrentUser,
                AccentRegistryPath,
                AccentPaletteRegistryValue,
                RrfRtRegBinary,
                IntPtr.Zero,
                palette,
                ref dataSize) != ErrorSuccess ||
            shadeIndex < 0 ||
            shadeIndex >= (int)(dataSize / 4))
        {
            return false;
        }

        var offset = shadeIndex * 4;
        // AccentPalette entries expose the RGB components in byte order for this palette.
        color = Rgb(palette[offset], palette[offset + 1], palette[offset + 2]);
        return true;
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

    private static void RefreshSystemPreferences()
    {
        _highContrast = IsHighContrastEnabled();
        _isDarkTheme = !_highContrast && IsDarkSystemThemeEnabled();
        _animationsEnabled = AreClientAreaAnimationsEnabled();
        _indicatorPosition = ReadIndicatorPosition();
    }

    private static bool IsDarkSystemThemeEnabled()
    {
        return TryReadRegistryDword(
                   PersonalizeRegistryPath,
                   SystemUsesLightThemeRegistryValue,
                   out var lightTheme) &&
               lightTheme == 0;
    }

    private static IndicatorPosition ReadIndicatorPosition()
    {
        // This is a shell preference rather than an app contract. Read it best-effort and keep
        // bottom-centre as the stable fallback if a Windows build does not expose the value.
        if (!TryReadRegistryDword(
                IndicatorPositionRegistryPath,
                IndicatorPositionRegistryValue,
                out var position))
        {
            return IndicatorPosition.BottomCenter;
        }

        return position switch
        {
            2 => IndicatorPosition.TopLeft,
            3 => IndicatorPosition.TopCenter,
            _ => IndicatorPosition.BottomCenter,
        };
    }

    private static bool TryReadRegistryDword(string subKey, string valueName, out uint value)
    {
        value = 0;
        uint dataSize = sizeof(uint);
        return RegGetValue(
                   HkeyCurrentUser,
                   subKey,
                   valueName,
                   RrfRtRegDword,
                   IntPtr.Zero,
                   ref value,
                   ref dataSize) == ErrorSuccess;
    }

    private static bool IsHighContrastEnabled()
    {
        var highContrast = new HighContrast
        {
            cbSize = (uint)Marshal.SizeOf<HighContrast>(),
        };
        return SystemParametersInfoHighContrast(
                   SpiGetHighContrast,
                   highContrast.cbSize,
                   ref highContrast,
                   0) &&
               (highContrast.dwFlags & HcfHighContrastOn) != 0;
    }

    private static bool AreClientAreaAnimationsEnabled()
    {
        var enabled = 1;
        if (!SystemParametersInfoInt(SpiGetClientAreaAnimation, 0, ref enabled, 0))
        {
            return true;
        }

        return enabled != 0;
    }

    private static void ConfigureWindows11Appearance(IntPtr window)
    {
        var darkMode = _isDarkTheme ? 1 : 0;
        DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        var cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        // Clear the legacy accent policy first so theme changes cannot leave a stale dark tint.
        SetAcrylicAccentPolicy(window, enabled: false);

        var margins = _highContrast
            ? new Margins()
            : new Margins
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1,
            };
        var frameResult = DwmExtendFrameIntoClientArea(window, ref margins);

        if (_highContrast)
        {
            var noBackdrop = DwmsbtNone;
            DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));
            _systemBackdropEnabled = false;
            return;
        }

        if (_isDarkTheme)
        {
            // DWMSBT_TRANSIENTWINDOW has no tint parameter and on current Windows 11 builds can
            // expose the bright Desktop Acrylic underpaint even with immersive dark mode enabled.
            // The Shell's dark flyouts add a #2C2C2C Acrylic tint, so use the accent-policy path
            // only for dark mode to reproduce that layer while keeping the same DWM blur.
            var noBackdrop = DwmsbtNone;
            DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));
            if (frameResult >= 0 && SetAcrylicAccentPolicy(window, enabled: true))
            {
                _systemBackdropEnabled = true;
                return;
            }

            // If the tint-capable path is ever unavailable, prefer WinUI's #2C2C2C fallback over
            // the visibly washed-out untinted transient backdrop.
            _systemBackdropEnabled = false;
            return;
        }

        // Light mode intentionally keeps Windows' bright transient Acrylic.
        var backdropType = DwmsbtTransientWindow;
        var backdropResult = DwmSetWindowAttribute(
            window,
            DwmwaSystemBackdropType,
            ref backdropType,
            sizeof(int));
        _systemBackdropEnabled = backdropResult >= 0 && frameResult >= 0;
    }

    private static bool SetAcrylicAccentPolicy(IntPtr window, bool enabled)
    {
        var policy = new AccentPolicy
        {
            AccentState = enabled ? AccentEnableAcrylicBlurBehind : AccentDisabled,
            AccentFlags = 0,
            GradientColor = enabled ? DarkAcrylicGradientColor : 0,
            AnimationId = 0,
        };

        var policyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttribData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = (nuint)Marshal.SizeOf<AccentPolicy>(),
            };
            return SetWindowCompositionAttribute(window, ref data);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    /// <summary>
    /// Determines whether the resident UI should leave the user session.
    /// </summary>
    /// <remarks>
    /// The service normally closes the UI explicitly. This check covers hard service termination and
    /// session transitions where the short-lived shutdown invocation cannot be created.
    /// </remarks>
    private static bool ShouldResidentUiExit()
    {
        if (!IsCurrentProcessInActiveConsoleSession())
        {
            return true;
        }

        // Keep the resident process aligned with the original UI contract as well: if Explorer/the
        // interactive Windows shell has gone away, there is no surface on which this OSD belongs.
        if (GetShellWindow() == IntPtr.Zero)
        {
            return true;
        }

        return IsHardwareServiceStopped();
    }

    private static bool IsCurrentProcessInActiveConsoleSession()
    {
        var activeSessionId = WTSGetActiveConsoleSessionId();
        if (activeSessionId == uint.MaxValue)
        {
            return false;
        }

        if (!ProcessIdToSessionId(GetCurrentProcessId(), out var currentSessionId))
        {
            // Do not tear down the UI merely because a defensive status query failed.
            return true;
        }

        return currentSessionId == activeSessionId;
    }

    private static bool IsHardwareServiceStopped()
    {
        var serviceManager = OpenSCManager(null, null, ScManagerConnect);
        if (serviceManager == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var service = OpenService(serviceManager, ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                // If the service was removed altogether there is no owner left for this resident UI.
                return Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist;
            }

            try
            {
                var status = new ServiceStatusProcess();
                if (!QueryServiceStatusEx(
                        service,
                        ScStatusProcessInfo,
                        ref status,
                        (uint)Marshal.SizeOf<ServiceStatusProcess>(),
                        out _))
                {
                    return false;
                }

                return status.dwCurrentState is ServiceStopped or ServiceStopPending;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(serviceManager);
        }
    }

    private static IntPtr WaitForExistingWindow()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var window = FindWindow(WindowClassName, null);
            if (window != IntPtr.Zero)
            {
                return window;
            }

            Sleep(25);
        }

        return IntPtr.Zero;
    }

    private static void PostNotification(IntPtr window, HardwareUiNotification notification)
    {
        var message = GetNotificationMessage(notification.Kind);
        if (message == 0)
        {
            return;
        }

        PostMessage(window, message, (UIntPtr)(uint)notification.Value, IntPtr.Zero);
    }

    private static bool SendNotification(IntPtr window, HardwareUiNotification notification)
    {
        var message = GetNotificationMessage(notification.Kind);
        if (message == 0)
        {
            return false;
        }

        return SendMessageTimeout(
            window,
            message,
            (UIntPtr)(uint)notification.Value,
            IntPtr.Zero,
            0x0002,
            500,
            out _) != IntPtr.Zero;
    }

    private static uint GetNotificationMessage(HardwareUiNotificationKind kind)
    {
        return kind switch
        {
            HardwareUiNotificationKind.Microphone => WmMicStatusChanged,
            HardwareUiNotificationKind.KeyboardBacklight => WmKeyboardBacklightChanged,
            HardwareUiNotificationKind.DisplayBrightness => WmDisplayBrightnessChanged,
            HardwareUiNotificationKind.PerformanceGpuMode => WmPerformanceGpuChanged,
            _ => 0,
        };
    }

    private static int Scale(int value, uint dpi)
    {
        return (int)((value * dpi + 48) / 96);
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(IntPtr mutexAttributes, bool initialOwner, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern void Sleep(uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        ref ServiceStatusProcess status,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValue(
        UIntPtr hKey,
        string? subKey,
        string? value,
        uint flags,
        IntPtr valueType,
        ref uint data,
        ref uint dataSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW", ExactSpelling = true)]
    private static extern int RegGetValueBytes(
        UIntPtr hKey,
        string? subKey,
        string? value,
        uint flags,
        IntPtr valueType,
        [Out] byte[] data,
        ref uint dataSize);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr window, UIntPtr timerId, uint milliseconds, IntPtr timerProcedure);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr window, UIntPtr timerId);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paintStruct);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paintStruct);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref Rect rect, IntPtr brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr deviceContext, string text, int textLength, ref Rect rect, uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoInt(
        uint action,
        uint parameter,
        ref int value,
        uint updateFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoHighContrast(
        uint action,
        uint parameter,
        ref HighContrast highContrast,
        uint updateFlags);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int penStyle, int width, uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int objectType);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll")]
    private static extern bool RoundRect(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int width,
        int height);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        bool italic,
        bool underline,
        bool strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(
        IntPtr window,
        ref WindowCompositionAttribData data);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmTransitionOwnedWindow(IntPtr window, int target);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool opaqueBlend);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedureDelegate(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    private enum IndicatorPosition
    {
        BottomCenter = 1,
        TopLeft = 2,
        TopCenter = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public WindowProcedureDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point point;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrast
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fErase;
        public Rect rcPaint;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIncUpdate;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
        public int reserved8;
    }
}
