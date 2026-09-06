using System.Runtime.InteropServices;
using static AsusHardwareService.HardwareUiNative;

namespace AsusHardwareService;

/// <summary>
/// Owns the resident HWND, message pump, local notification IPC, and process/session liveness.
/// </summary>
internal static class HardwareUiHost
{
    internal static HardwareUiNotification Notification { get; set; } =
        new(HardwareUiNotificationKind.Microphone, 0);

    internal static int WindowWidth { get; set; }
    internal static int WindowHeight { get; set; }
    internal static uint WindowDpi { get; set; } = 96;
    private static readonly WindowProcedureDelegate WindowProcedureCallback = WindowProcedure;
    private static IntPtr _windowHandle;

    internal static int RunMessageLoop(HardwareUiNotification? initialNotification)
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

        HardwareUiTheme.RefreshSystemPreferences();
        HardwareUiTheme.ConfigureWindows11Appearance(_windowHandle);
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
                Notification = new HardwareUiNotification(
                    HardwareUiNotificationKind.Microphone,
                    wParam == UIntPtr.Zero ? 0 : 1);
                HardwareUiPresenter.ShowStatusWindow(window);
                return IntPtr.Zero;

            case WmKeyboardBacklightChanged:
                var keyboardLevel = (int)wParam.ToUInt64();
                if (keyboardLevel is >= 0 and <= 3)
                {
                    Notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.KeyboardBacklight,
                        keyboardLevel);
                    HardwareUiPresenter.ShowStatusWindow(window);
                }
                return IntPtr.Zero;

            case WmDisplayBrightnessChanged:
                var brightness = (int)wParam.ToUInt64();
                if (brightness is >= 0 and <= 100)
                {
                    Notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.DisplayBrightness,
                        brightness);
                    HardwareUiPresenter.ShowStatusWindow(window);
                }
                return IntPtr.Zero;

            case WmPerformanceGpuChanged:
                var performanceGpuMode = (int)wParam.ToUInt64();
                if (performanceGpuMode is >= 0 and <= 3)
                {
                    Notification = new HardwareUiNotification(
                        HardwareUiNotificationKind.PerformanceGpuMode,
                        performanceGpuMode);
                    HardwareUiPresenter.ShowStatusWindow(window);
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

                if (wParam == HideTimerId)
                {
                    KillTimer(window, HideTimerId);
                    HardwareUiPresenter.HideStatusWindow(window);
                    return IntPtr.Zero;
                }

                break;

            case WmAnimationFrame:
                HardwareUiPresenter.AdvanceWindowAnimation(window, unchecked((uint)wParam.ToUInt64()));
                return IntPtr.Zero;

            case WmSettingChange:
            case WmSysColorChange:
                HardwareUiTheme.RefreshSystemPreferences();
                HardwareUiTheme.ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmDwmCompositionChanged:
                HardwareUiTheme.ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmEraseBackground:
                // WM_PAINT redraws the whole compact surface, avoiding an extra erase/flicker pass.
                return new IntPtr(1);

            case WmPaint:
                HardwareUiRenderer.PaintStatus(window);
                return IntPtr.Zero;

            case WmPrintClient:
                HardwareUiRenderer.DrawStatus(window, new IntPtr(unchecked((long)wParam.ToUInt64())));
                return IntPtr.Zero;

            case WmMouseActivate:
                return new IntPtr(MaNoActivate);

            case WmClose:
                KillTimer(window, HideTimerId);
                HardwareUiPresenter.CancelShowAnimation(window);
                HardwareUiPresenter.CancelHideAnimation(window);
                KillTimer(window, ServiceWatchTimerId);
                DestroyWindow(window);
                return IntPtr.Zero;

            case WmDestroy:
                HardwareUiRenderer.DestroyBackBuffer();
                HardwareUiRenderer.ShutdownGdiPlus();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

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

    internal static IntPtr WaitForExistingWindow()
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

    internal static bool SendNotification(IntPtr window, HardwareUiNotification notification)
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
}
