using System.Diagnostics;
using System.Runtime.InteropServices;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Presentation.Osd;
using AsusHardwareService.Settings;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>Owns the interactive hardware-settings fly-out window and input behavior.</summary>
internal static class SettingsFlyoutWindow
{
    private const string WindowClassName = "AsusHardwareService.SettingsFlyoutWindow";
    private const int ArrowCursorId = 32512;
    private const int DwmwaTransitionsForcedDisabled = 3;
    private const int VkTab = 0x09;
    private const uint WmReadCompleted = WmApp + 0x72;
    private const uint WmUpdateCompleted = WmApp + 0x73;
    private const uint WmFlyoutAnimationFrame = WmApp + 0x74;
    private const uint WmLightDismiss = WmApp + 0x75;
    private static readonly UIntPtr ApplyDebounceTimerId = (UIntPtr)11u;
    private const uint ApplyDebounceMilliseconds = 350;

    private static readonly WindowProcedureDelegate WindowProcedureCallback = WindowProcedure;
    private static readonly HardwareSettingsClient Client = new();
    private static readonly object CompletionLock = new();

    private static bool _classRegistered;
    private static IntPtr _windowHandle;
    private static HardwareSettingsResponse? _pendingResponse;
    private static int _value = 60;
    private static int _minimum = 60;
    private static int _maximum = 100;
    private static int _step = 5;
    private static int _committedValue = 60;
    private static OperatingModePreset? _operatingMode;
    private static OperatingModePreset? _committedOperatingMode;
    private static OperatingModePreset? _hoveredOperatingMode;
    private static OperatingModePreset? _pressedOperatingMode;
    private static LaptopDisplayMode _laptopDisplayMode = LaptopDisplayMode.Auto;
    private static LaptopDisplayMode _committedLaptopDisplayMode = LaptopDisplayMode.Auto;
    private static LaptopDisplayMode? _hoveredLaptopDisplayMode;
    private static LaptopDisplayMode? _pressedLaptopDisplayMode;
    private static SettingsFlyoutFocusedControl _focusedControl = SettingsFlyoutFocusedControl.BatteryChargeLimit;
    private static bool _isAvailable;
    private static bool _isLoading;
    private static bool _isApplying;
    private static bool _isDragging;
    private static bool _trackingMouseLeave;
    private static bool _showFocusVisual;
    private static bool _closeAfterOperation;
    private static bool _isDismissing;
    private static bool _animationActive;
    private static bool _animationIncoming;
    private static bool _destroyAfterAnimation;
    private static uint _animationGeneration;
    private static long _animationStartTimestamp;
    private static int _animationStartY;
    private static int _animationEndY;
    private static int _animationLastPresentedY;
    private static int _finalX;
    private static int _finalY;
    private static int _hideOffScreenY;
    private static string? _statusText;

    /// <summary>Creates the fly-out when necessary and activates it in the resident UI process.</summary>
    /// <returns><see langword="true"/> when the fly-out was shown.</returns>
    internal static bool Show()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            CancelFlyoutAnimation(_windowHandle);
            PositionFlyout(_windowHandle, preferForegroundMonitor: true);
            ShowAndActivate(_windowHandle);
            return true;
        }

        if (!EnsureWindowClass())
        {
            return false;
        }

        ResetState();
        var moduleHandle = GetModuleHandle(null);
        _windowHandle = CreateWindowEx(
            WsExTopmost | WsExToolWindow,
            WindowClassName,
            "ASUS hardware settings",
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
            return false;
        }

        OsdTheme.RefreshSystemPreferences();
        OsdTheme.ConfigureWindows11Appearance(_windowHandle);

        // A generic Win32 popup transition does not match the taskbar fly-outs. Disable DWM's
        // default window transition for this HWND and use the short Fluent translation below.
        var transitionsDisabled = 1;
        DwmSetWindowAttribute(
            _windowHandle,
            DwmwaTransitionsForcedDisabled,
            ref transitionsDisabled,
            sizeof(int));

        PositionFlyout(_windowHandle, preferForegroundMonitor: true);
        ShowAndActivate(_windowHandle);
        BeginRead(_windowHandle);
        return true;
    }

    private static bool EnsureWindowClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = WindowProcedureCallback,
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, new IntPtr(ArrowCursorId)),
            lpszClassName = WindowClassName,
        };

        _classRegistered = RegisterClassEx(ref windowClass) != 0;
        return _classRegistered;
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmReadCompleted:
                CompleteRead(window);
                return IntPtr.Zero;

            case WmUpdateCompleted:
                CompleteUpdate(window);
                return IntPtr.Zero;

            case WmFlyoutAnimationFrame:
                AdvanceFlyoutAnimation(window, (uint)wParam.ToUInt64());
                return IntPtr.Zero;

            case WmLightDismiss:
                if (IsWindowVisible(window))
                {
                    RequestClose(window, commitPendingChange: true);
                }
                return IntPtr.Zero;

            case WmLButtonDown:
                if (_showFocusVisual)
                {
                    var previouslyFocusedControl = _focusedControl;
                    _showFocusVisual = false;
                    InvalidateFocusVisual(window, previouslyFocusedControl);
                }

                if (CanEdit() && IsPointInSlider(window, GetMouseX(lParam), GetMouseY(lParam)))
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    _focusedControl = SettingsFlyoutFocusedControl.BatteryChargeLimit;
                    var hoveredModeBeforeSliderInteraction = _hoveredOperatingMode;
                    _hoveredOperatingMode = null;
                    _isDragging = true;
                    SetCapture(window);
                    UpdateValueFromMouse(window, GetMouseX(lParam));
                    InvalidateOperatingModeTile(window, hoveredModeBeforeSliderInteraction);
                    InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
                    return IntPtr.Zero;
                }

                if (CanEdit() &&
                    TryGetOperatingModeAtPoint(window, GetMouseX(lParam), GetMouseY(lParam), out var operatingMode))
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    _focusedControl = SettingsFlyoutFocusedControl.OperatingMode;
                    var hoveredModeBeforePress = _hoveredOperatingMode;
                    _hoveredOperatingMode = operatingMode;
                    _pressedOperatingMode = operatingMode;
                    EnsureMouseLeaveTracking(window);
                    SetCapture(window);
                    InvalidateOperatingModeTile(window, hoveredModeBeforePress);
                    InvalidateOperatingModeTile(window, operatingMode);
                    return IntPtr.Zero;
                }

                if (CanEdit() &&
                    TryGetLaptopDisplayModeAtPoint(
                        window,
                        GetMouseX(lParam),
                        GetMouseY(lParam),
                        out var laptopDisplayMode))
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    _focusedControl = SettingsFlyoutFocusedControl.LaptopDisplayMode;
                    var hoveredDisplayModeBeforePress = _hoveredLaptopDisplayMode;
                    _hoveredLaptopDisplayMode = laptopDisplayMode;
                    _pressedLaptopDisplayMode = laptopDisplayMode;
                    EnsureMouseLeaveTracking(window);
                    SetCapture(window);
                    InvalidateLaptopDisplayModeTile(window, hoveredDisplayModeBeforePress);
                    InvalidateLaptopDisplayModeTile(window, laptopDisplayMode);
                    return IntPtr.Zero;
                }
                return IntPtr.Zero;

            case WmMouseMove:
                EnsureMouseLeaveTracking(window);
                if (_isDragging && CanEdit())
                {
                    UpdateValueFromMouse(window, GetMouseX(lParam));
                    return IntPtr.Zero;
                }

                UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                return IntPtr.Zero;

            case WmMouseLeave:
                _trackingMouseLeave = false;
                if (_hoveredOperatingMode is { } previouslyHoveredMode)
                {
                    _hoveredOperatingMode = null;
                    InvalidateOperatingModeTile(window, previouslyHoveredMode);
                }
                if (_hoveredLaptopDisplayMode is { } previouslyHoveredDisplayMode)
                {
                    _hoveredLaptopDisplayMode = null;
                    InvalidateLaptopDisplayModeTile(window, previouslyHoveredDisplayMode);
                }
                return IntPtr.Zero;

            case WmLButtonUp:
                if (_isDragging)
                {
                    _isDragging = false;
                    ReleaseCapture();
                    if (CanEdit())
                    {
                        UpdateValueFromMouse(window, GetMouseX(lParam));
                        BeginApply(window);
                    }
                    InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
                    return IntPtr.Zero;
                }

                if (_pressedOperatingMode is { } pressedOperatingMode)
                {
                    var shouldCommit = CanEdit() &&
                        TryGetOperatingModeAtPoint(
                            window,
                            GetMouseX(lParam),
                            GetMouseY(lParam),
                            out var releasedOperatingMode) &&
                        releasedOperatingMode == pressedOperatingMode;
                    _pressedOperatingMode = null;
                    ReleaseCapture();
                    UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                    InvalidateOperatingModeTile(window, pressedOperatingMode);
                    if (shouldCommit)
                    {
                        SetOperatingMode(window, pressedOperatingMode);
                        BeginApply(window);
                    }
                }

                if (_pressedLaptopDisplayMode is { } pressedLaptopDisplayMode)
                {
                    var shouldCommit = CanEdit() &&
                        TryGetLaptopDisplayModeAtPoint(
                            window,
                            GetMouseX(lParam),
                            GetMouseY(lParam),
                            out var releasedLaptopDisplayMode) &&
                        releasedLaptopDisplayMode == pressedLaptopDisplayMode;
                    _pressedLaptopDisplayMode = null;
                    ReleaseCapture();
                    UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                    InvalidateLaptopDisplayModeTile(window, pressedLaptopDisplayMode);
                    if (shouldCommit)
                    {
                        SetLaptopDisplayMode(window, pressedLaptopDisplayMode);
                        BeginApply(window);
                    }
                }
                return IntPtr.Zero;

            case WmCaptureChanged:
                if (_isDragging || _pressedOperatingMode is not null || _pressedLaptopDisplayMode is not null)
                {
                    var wasDragging = _isDragging;
                    var previouslyPressedMode = _pressedOperatingMode;
                    var previouslyPressedDisplayMode = _pressedLaptopDisplayMode;
                    _isDragging = false;
                    _pressedOperatingMode = null;
                    _pressedLaptopDisplayMode = null;
                    if (wasDragging)
                    {
                        InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
                    }
                    InvalidateOperatingModeTile(window, previouslyPressedMode);
                    InvalidateLaptopDisplayModeTile(window, previouslyPressedDisplayMode);
                }
                return IntPtr.Zero;

            case WmKeyDown:
                var key = (int)wParam.ToUInt64();
                if (key == VkEscape)
                {
                    RequestClose(window, commitPendingChange: false);
                    return IntPtr.Zero;
                }

                if (key == VkTab)
                {
                    var previouslyFocusedControl = _focusedControl;
                    var focusWasVisible = _showFocusVisual;
                    _focusedControl = _focusedControl switch
                    {
                        SettingsFlyoutFocusedControl.BatteryChargeLimit => SettingsFlyoutFocusedControl.OperatingMode,
                        SettingsFlyoutFocusedControl.OperatingMode => SettingsFlyoutFocusedControl.LaptopDisplayMode,
                        _ => SettingsFlyoutFocusedControl.BatteryChargeLimit,
                    };
                    _showFocusVisual = true;
                    if (focusWasVisible)
                    {
                        InvalidateFocusVisual(window, previouslyFocusedControl);
                    }
                    InvalidateFocusVisual(window, _focusedControl);
                    return IntPtr.Zero;
                }

                if (CanEdit())
                {
                    var handled = _focusedControl switch
                    {
                        SettingsFlyoutFocusedControl.BatteryChargeLimit => HandleBatteryKeyAdjustment(window, key),
                        SettingsFlyoutFocusedControl.OperatingMode => HandleOperatingModeKeyAdjustment(window, key),
                        SettingsFlyoutFocusedControl.LaptopDisplayMode => HandleLaptopDisplayModeKeyAdjustment(window, key),
                        _ => false,
                    };
                    if (handled)
                    {
                        var focusWasVisible = _showFocusVisual;
                        _showFocusVisual = true;
                        if (!focusWasVisible)
                        {
                            InvalidateFocusVisual(window, _focusedControl);
                        }
                        return IntPtr.Zero;
                    }
                }
                break;

            case WmTimer:
                if (wParam == ApplyDebounceTimerId)
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    BeginApply(window);
                    return IntPtr.Zero;
                }
                break;

            case WmActivate:
                if ((ushort)(wParam.ToUInt64() & 0xffffu) == WaInactive && IsWindowVisible(window))
                {
                    RequestClose(window, commitPendingChange: true);
                }
                return IntPtr.Zero;

            case WmSettingChange:
            case WmSysColorChange:
                OsdTheme.RefreshSystemPreferences();
                OsdTheme.ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmDwmCompositionChanged:
                OsdTheme.ConfigureWindows11Appearance(window);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmDpiChanged:
                CancelFlyoutAnimation(window);
                PositionFlyout(window, preferForegroundMonitor: false);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmEraseBackground:
                return new IntPtr(1);

            case WmPaint:
                SettingsFlyoutRenderer.Paint(window, CreateViewModel());
                return IntPtr.Zero;

            case WmClose:
                KillTimer(window, ApplyDebounceTimerId);
                SettingsFlyoutLightDismiss.Stop();
                StopFlyoutAnimation();
                DestroyWindow(window);
                return IntPtr.Zero;

            case WmDestroy:
                SettingsFlyoutLightDismiss.Stop();
                SettingsFlyoutRenderer.DestroyBackBuffer();
                _windowHandle = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private static void BeginRead(IntPtr window)
    {
        _isLoading = true;
        _statusText = null;
        InvalidateRect(window, IntPtr.Zero, false);
        _ = ReadAsync(window);
    }

    private static async Task ReadAsync(IntPtr window)
    {
        HardwareSettingsResponse? response = null;
        try
        {
            response = await Client.ReadAsync().ConfigureAwait(false);
        }
        catch
        {
            // Completion is still posted so the UI can show the unavailable state.
        }

        StoreCompletion(response);
        PostMessage(window, WmReadCompleted, UIntPtr.Zero, IntPtr.Zero);
    }

    private static void CompleteRead(IntPtr window)
    {
        var response = TakeCompletion();
        _isLoading = false;
        if (response is not null && response.Success)
        {
            ApplySnapshot(response.Settings);
            _isAvailable = true;
            _statusText = null;
        }
        else
        {
            _isAvailable = false;
            _statusText = response?.Error ?? "Service unavailable.";
        }

        InvalidateRect(window, IntPtr.Zero, false);
        if (_closeAfterOperation)
        {
            PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void BeginApply(IntPtr window)
    {
        if (!CanEdit())
        {
            return;
        }

        var chargeLimit = _value != _committedValue ? _value : (int?)null;
        var operatingMode = _operatingMode != _committedOperatingMode ? _operatingMode : null;
        var laptopDisplayMode = _laptopDisplayMode != _committedLaptopDisplayMode
            ? _laptopDisplayMode
            : (LaptopDisplayMode?)null;
        var patch = new HardwareSettingsPatch(chargeLimit, operatingMode, laptopDisplayMode);
        if (patch.IsEmpty)
        {
            return;
        }

        KillTimer(window, ApplyDebounceTimerId);
        _isApplying = true;
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
        _ = ApplyAsync(window, patch);
    }

    private static async Task ApplyAsync(IntPtr window, HardwareSettingsPatch patch)
    {
        HardwareSettingsResponse? response = null;
        try
        {
            response = await Client.UpdateAsync(patch).ConfigureAwait(false);
        }
        catch
        {
            // Completion is still posted so the UI can show the unavailable state.
        }

        StoreCompletion(response);
        PostMessage(window, WmUpdateCompleted, UIntPtr.Zero, IntPtr.Zero);
    }

    private static void CompleteUpdate(IntPtr window)
    {
        var response = TakeCompletion();
        _isApplying = false;

        if (response is not null)
        {
            ApplySnapshot(response.Settings);
            _isAvailable = true;
            _statusText = response.Success
                ? null
                : response.Error ?? "The hardware setting could not be applied.";
        }
        else
        {
            _value = _committedValue;
            _laptopDisplayMode = _committedLaptopDisplayMode;
            _isAvailable = false;
            _statusText = "Service unavailable.";
        }

        InvalidateRect(window, IntPtr.Zero, false);
        if (_closeAfterOperation)
        {
            PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void StoreCompletion(HardwareSettingsResponse? response)
    {
        lock (CompletionLock)
        {
            _pendingResponse = response;
        }
    }

    private static HardwareSettingsResponse? TakeCompletion()
    {
        lock (CompletionLock)
        {
            var response = _pendingResponse;
            _pendingResponse = null;
            return response;
        }
    }

    private static void ApplySnapshot(HardwareSettingsSnapshot snapshot)
    {
        var setting = snapshot.BatteryChargeLimit;
        _minimum = setting.Minimum;
        _maximum = setting.Maximum;
        _step = Math.Max(1, setting.Step);
        _committedValue = NormalizeToStep(setting.Value);
        _value = _committedValue;
        _committedOperatingMode = snapshot.OperatingMode;
        _operatingMode = _committedOperatingMode;
        _committedLaptopDisplayMode = snapshot.LaptopDisplayMode;
        _laptopDisplayMode = _committedLaptopDisplayMode;
    }

    private static bool HandleBatteryKeyAdjustment(IntPtr window, int key)
    {
        int nextValue;
        switch (key)
        {
            case VkLeft:
            case VkDown:
                nextValue = _value - _step;
                break;
            case VkRight:
            case VkUp:
                nextValue = _value + _step;
                break;
            case VkHome:
                nextValue = _minimum;
                break;
            case VkEnd:
                nextValue = _maximum;
                break;
            default:
                return false;
        }

        SetValue(window, nextValue);
        KillTimer(window, ApplyDebounceTimerId);
        SetTimer(window, ApplyDebounceTimerId, ApplyDebounceMilliseconds, IntPtr.Zero);
        return true;
    }

    private static bool HandleOperatingModeKeyAdjustment(IntPtr window, int key)
    {
        var current = _operatingMode ?? OperatingModePreset.Normal;
        OperatingModePreset next;
        switch (key)
        {
            case VkLeft:
            case VkDown:
                next = current switch
                {
                    OperatingModePreset.Turbo => OperatingModePreset.Normal,
                    OperatingModePreset.Normal => OperatingModePreset.Eco,
                    _ => OperatingModePreset.Eco,
                };
                break;
            case VkRight:
            case VkUp:
                next = current switch
                {
                    OperatingModePreset.Eco => OperatingModePreset.Normal,
                    OperatingModePreset.Normal => OperatingModePreset.Turbo,
                    _ => OperatingModePreset.Turbo,
                };
                break;
            case VkHome:
                next = OperatingModePreset.Eco;
                break;
            case VkEnd:
                next = OperatingModePreset.Turbo;
                break;
            default:
                return false;
        }

        SetOperatingMode(window, next);
        KillTimer(window, ApplyDebounceTimerId);
        SetTimer(window, ApplyDebounceTimerId, ApplyDebounceMilliseconds, IntPtr.Zero);
        return true;
    }

    private static bool HandleLaptopDisplayModeKeyAdjustment(IntPtr window, int key)
    {
        LaptopDisplayMode next;
        switch (key)
        {
            case VkLeft:
            case VkDown:
                next = _laptopDisplayMode switch
                {
                    LaptopDisplayMode.Hz240Overdrive => LaptopDisplayMode.Hz60,
                    LaptopDisplayMode.Hz60 => LaptopDisplayMode.Auto,
                    _ => LaptopDisplayMode.Auto,
                };
                break;
            case VkRight:
            case VkUp:
                next = _laptopDisplayMode switch
                {
                    LaptopDisplayMode.Auto => LaptopDisplayMode.Hz60,
                    LaptopDisplayMode.Hz60 => LaptopDisplayMode.Hz240Overdrive,
                    _ => LaptopDisplayMode.Hz240Overdrive,
                };
                break;
            case VkHome:
                next = LaptopDisplayMode.Auto;
                break;
            case VkEnd:
                next = LaptopDisplayMode.Hz240Overdrive;
                break;
            default:
                return false;
        }

        SetLaptopDisplayMode(window, next);
        KillTimer(window, ApplyDebounceTimerId);
        SetTimer(window, ApplyDebounceTimerId, ApplyDebounceMilliseconds, IntPtr.Zero);
        return true;
    }

    private static void UpdateValueFromMouse(IntPtr window, int mouseX)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var track = OsdLayout.ToPixels(SettingsFlyoutLayout.TrackRect, dpi);
        var width = Math.Max(1, track.Right - track.Left);
        var progress = Math.Clamp((mouseX - track.Left) / (double)width, 0.0, 1.0);
        var rawValue = _minimum + ((_maximum - _minimum) * progress);
        var steppedValue = _minimum +
            ((int)Math.Round((rawValue - _minimum) / _step, MidpointRounding.AwayFromZero) * _step);
        SetValue(window, steppedValue);
    }

    private static void SetValue(IntPtr window, int value)
    {
        var normalized = NormalizeToStep(value);
        if (_value == normalized)
        {
            return;
        }

        _value = normalized;
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        InvalidateDipRect(window, SettingsFlyoutLayout.ValueRect);
        InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
    }

    private static void SetOperatingMode(IntPtr window, OperatingModePreset operatingMode)
    {
        if (_operatingMode == operatingMode)
        {
            return;
        }

        var previousOperatingMode = _operatingMode;
        _operatingMode = operatingMode;
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        InvalidateOperatingModeTile(window, previousOperatingMode);
        InvalidateOperatingModeTile(window, operatingMode);
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
    }

    private static void SetLaptopDisplayMode(IntPtr window, LaptopDisplayMode laptopDisplayMode)
    {
        if (_laptopDisplayMode == laptopDisplayMode)
        {
            return;
        }

        var previousLaptopDisplayMode = _laptopDisplayMode;
        _laptopDisplayMode = laptopDisplayMode;
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        InvalidateLaptopDisplayModeTile(window, previousLaptopDisplayMode);
        InvalidateLaptopDisplayModeTile(window, laptopDisplayMode);
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
    }

    private static int NormalizeToStep(int value)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        var stepIndex = (int)Math.Round(
            (clamped - _minimum) / (double)Math.Max(1, _step),
            MidpointRounding.AwayFromZero);
        return Math.Clamp(_minimum + (stepIndex * Math.Max(1, _step)), _minimum, _maximum);
    }

    private static bool IsPointInSlider(IntPtr window, int x, int y)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var hit = SettingsFlyoutLayout.GetSliderHitRect(dpi);
        return x >= hit.Left && x <= hit.Right && y >= hit.Top && y <= hit.Bottom;
    }

    private static bool TryGetOperatingModeAtPoint(
        IntPtr window,
        int x,
        int y,
        out OperatingModePreset operatingMode)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return SettingsFlyoutLayout.TryGetOperatingModeAtPoint(dpi, x, y, out operatingMode);
    }

    private static bool TryGetLaptopDisplayModeAtPoint(
        IntPtr window,
        int x,
        int y,
        out LaptopDisplayMode laptopDisplayMode)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return SettingsFlyoutLayout.TryGetLaptopDisplayModeAtPoint(dpi, x, y, out laptopDisplayMode);
    }

    private static void EnsureMouseLeaveTracking(IntPtr window)
    {
        if (_trackingMouseLeave)
        {
            return;
        }

        var tracking = new TrackMouseEventData
        {
            cbSize = (uint)Marshal.SizeOf<TrackMouseEventData>(),
            dwFlags = TmeLeave,
            hwndTrack = window,
        };
        _trackingMouseLeave = TrackMouseEvent(ref tracking);
    }

    private static void UpdateTileHover(IntPtr window, int x, int y)
    {
        OperatingModePreset? hoveredOperatingMode = null;
        LaptopDisplayMode? hoveredLaptopDisplayMode = null;
        if (CanEdit())
        {
            if (TryGetOperatingModeAtPoint(window, x, y, out var operatingMode))
            {
                hoveredOperatingMode = operatingMode;
            }
            else if (TryGetLaptopDisplayModeAtPoint(window, x, y, out var laptopDisplayMode))
            {
                hoveredLaptopDisplayMode = laptopDisplayMode;
            }
        }

        if (_hoveredOperatingMode != hoveredOperatingMode)
        {
            var previouslyHoveredOperatingMode = _hoveredOperatingMode;
            _hoveredOperatingMode = hoveredOperatingMode;
            InvalidateOperatingModeTile(window, previouslyHoveredOperatingMode);
            InvalidateOperatingModeTile(window, hoveredOperatingMode);
        }

        if (_hoveredLaptopDisplayMode != hoveredLaptopDisplayMode)
        {
            var previouslyHoveredLaptopDisplayMode = _hoveredLaptopDisplayMode;
            _hoveredLaptopDisplayMode = hoveredLaptopDisplayMode;
            InvalidateLaptopDisplayModeTile(window, previouslyHoveredLaptopDisplayMode);
            InvalidateLaptopDisplayModeTile(window, hoveredLaptopDisplayMode);
        }
    }

    /// <summary>Invalidates only the stateful surface of one operating-mode action tile.</summary>
    private static void InvalidateOperatingModeTile(IntPtr window, OperatingModePreset? operatingMode)
    {
        if (operatingMode is not { } mode)
        {
            return;
        }

        var rect = SettingsFlyoutLayout.GetOperatingModeRect(mode);
        InvalidateDipRect(
            window,
            new DipRect(rect.Left - 1.0, rect.Top - 1.0, rect.Right + 1.0, rect.Bottom + 1.0));
    }

    /// <summary>Invalidates only the stateful surface of one laptop-screen action tile.</summary>
    private static void InvalidateLaptopDisplayModeTile(IntPtr window, LaptopDisplayMode? laptopDisplayMode)
    {
        if (laptopDisplayMode is not { } mode)
        {
            return;
        }

        var rect = SettingsFlyoutLayout.GetLaptopDisplayModeRect(mode);
        InvalidateDipRect(
            window,
            new DipRect(rect.Left - 1.0, rect.Top - 1.0, rect.Right + 1.0, rect.Bottom + 1.0));
    }

    /// <summary>Invalidates the currently visible keyboard focus cue without repainting the fly-out.</summary>
    private static void InvalidateFocusVisual(IntPtr window, SettingsFlyoutFocusedControl control)
    {
        switch (control)
        {
            case SettingsFlyoutFocusedControl.BatteryChargeLimit:
                InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
                break;
            case SettingsFlyoutFocusedControl.OperatingMode:
                InvalidateOperatingModeTile(window, _operatingMode ?? OperatingModePreset.Normal);
                break;
            case SettingsFlyoutFocusedControl.LaptopDisplayMode:
                InvalidateLaptopDisplayModeTile(window, _laptopDisplayMode);
                break;
        }
    }

    /// <summary>Invalidates a DPI-independent client rectangle without erasing the Acrylic surface.</summary>
    private static void InvalidateDipRect(IntPtr window, DipRect dipRect)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var pixelRect = OsdLayout.ToPixels(dipRect, dpi);
        var nativeRect = new Rect
        {
            Left = pixelRect.Left,
            Top = pixelRect.Top,
            Right = pixelRect.Right,
            Bottom = pixelRect.Bottom,
        };
        InvalidateRectArea(window, ref nativeRect, false);
    }

    private static bool CanEdit() => _isAvailable && !_isLoading && !_isApplying;

    private static SettingsFlyoutViewModel CreateViewModel() =>
        new(
            _value,
            _minimum,
            _maximum,
            _operatingMode,
            _hoveredOperatingMode,
            _pressedOperatingMode,
            _laptopDisplayMode,
            _hoveredLaptopDisplayMode,
            _pressedLaptopDisplayMode,
            _isAvailable,
            _isApplying,
            _isDragging,
            _showFocusVisual,
            _focusedControl,
            _statusText);

    private static void RequestClose(IntPtr window, bool commitPendingChange)
    {
        if (_isDismissing)
        {
            return;
        }

        _isDismissing = true;
        SettingsFlyoutLightDismiss.Stop();

        if (_isDragging || _pressedOperatingMode is not null || _pressedLaptopDisplayMode is not null)
        {
            _isDragging = false;
            _pressedOperatingMode = null;
            _pressedLaptopDisplayMode = null;
            ReleaseCapture();
        }

        // The HWND is intentionally kept resident after a normal dismissal. Do not carry a stale
        // pointer-over state into the next keyboard-triggered presentation.
        _hoveredOperatingMode = null;
        _hoveredLaptopDisplayMode = null;
        _trackingMouseLeave = false;

        if (commitPendingChange &&
            CanEdit() &&
            (_value != _committedValue ||
             _operatingMode != _committedOperatingMode ||
             _laptopDisplayMode != _committedLaptopDisplayMode))
        {
            _closeAfterOperation = true;
            BeginApply(window);
            BeginDismissAnimation(window, destroyAfterAnimation: false);
            return;
        }

        if (!commitPendingChange && CanEdit())
        {
            KillTimer(window, ApplyDebounceTimerId);
            _value = _committedValue;
            _operatingMode = _committedOperatingMode;
            _laptopDisplayMode = _committedLaptopDisplayMode;
        }

        if (_isLoading || _isApplying)
        {
            _closeAfterOperation = true;
            BeginDismissAnimation(window, destroyAfterAnimation: false);
            return;
        }

        BeginDismissAnimation(window);
    }

    private static void ShowAndActivate(IntPtr window)
    {
        _closeAfterOperation = false;
        _isDismissing = false;
        CancelFlyoutAnimation(window);

        if (OsdTheme.AnimationsEnabled && !IsWindowVisible(window))
        {
            StartShowAnimation(window);
        }
        else
        {
            SetFlyoutPosition(window, _finalY, show: true);
            ShowWindow(window, SwShow);
        }

        SetForegroundWindow(window);
        SetFocus(window);
        SettingsFlyoutLightDismiss.Start(window, WmLightDismiss);
        InvalidateRect(window, IntPtr.Zero, false);
    }

    private static void StartShowAnimation(IntPtr window)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var translation = DipToPx(EntranceTranslationDip, dpi);
        BeginFlyoutAnimation(
            window,
            _finalY + translation,
            _finalY,
            incoming: true,
            destroyAfterAnimation: false);
    }

    private static void BeginDismissAnimation(IntPtr window, bool destroyAfterAnimation = true)
    {
        if (!IsWindowVisible(window))
        {
            if (destroyAfterAnimation)
            {
                PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
            }
            return;
        }

        if (!OsdTheme.AnimationsEnabled)
        {
            if (destroyAfterAnimation)
            {
                PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                ShowWindow(window, SwHide);
            }
            return;
        }

        BeginFlyoutAnimation(
            window,
            _finalY,
            _hideOffScreenY,
            incoming: false,
            destroyAfterAnimation);
    }

    private static void BeginFlyoutAnimation(
        IntPtr window,
        int startY,
        int endY,
        bool incoming,
        bool destroyAfterAnimation)
    {
        _animationGeneration = unchecked(_animationGeneration + 1u);
        if (_animationGeneration == 0u)
        {
            _animationGeneration = 1u;
        }

        _animationStartY = startY;
        _animationEndY = endY;
        _animationLastPresentedY = startY;
        _animationIncoming = incoming;
        _destroyAfterAnimation = destroyAfterAnimation;
        _animationActive = true;

        SetFlyoutPosition(window, startY, show: true);
        UpdateWindow(window);
        if (DwmFlush() < 0)
        {
            FinishFlyoutAnimation(window);
            return;
        }

        _animationStartTimestamp = Stopwatch.GetTimestamp();
        PostMessage(window, WmFlyoutAnimationFrame, new UIntPtr(_animationGeneration), IntPtr.Zero);
    }

    private static void AdvanceFlyoutAnimation(IntPtr window, uint generation)
    {
        if (!_animationActive || generation != _animationGeneration)
        {
            return;
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - _animationStartTimestamp;
        var durationTicks = Stopwatch.Frequency * (ControlFastAnimationDurationMilliseconds / 1000.0);
        var progress = durationTicks <= 0.0
            ? 1.0
            : Math.Clamp(elapsedTicks / durationTicks, 0.0, 1.0);
        var easedProgress = _animationIncoming
            ? EaseDirectEntrance(progress)
            : EaseGentleExit(progress);
        var y = (int)Math.Round(
            _animationStartY + ((_animationEndY - _animationStartY) * easedProgress));
        if (y != _animationLastPresentedY)
        {
            MoveFlyout(window, y);
            _animationLastPresentedY = y;
        }

        if (progress >= 1.0)
        {
            FinishFlyoutAnimation(window);
            return;
        }

        if (DwmFlush() < 0)
        {
            FinishFlyoutAnimation(window);
            return;
        }

        PostMessage(window, WmFlyoutAnimationFrame, new UIntPtr(generation), IntPtr.Zero);
    }

    private static void FinishFlyoutAnimation(IntPtr window)
    {
        if (!_animationActive)
        {
            return;
        }

        MoveFlyout(window, _animationEndY);
        var destroy = _destroyAfterAnimation;
        var incoming = _animationIncoming;
        StopFlyoutAnimation();

        if (destroy)
        {
            // Make the last translated position a real presented frame before destroying the HWND.
            // Otherwise DWM can coalesce the final move with destruction and truncate the exit.
            DwmFlush();
            PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
            return;
        }

        if (incoming)
        {
            SetFlyoutPosition(window, _finalY, show: true);
            return;
        }

        // A settings update/read may still be completing after light-dismiss. Finish the visual
        // dismissal now, but keep the HWND alive so its completion message cannot race a reused
        // window handle. CompleteRead/CompleteUpdate will close it when the operation finishes.
        DwmFlush();
        ShowWindow(window, SwHide);
        SetFlyoutPosition(window, _finalY, show: false);
    }

    private static void CancelFlyoutAnimation(IntPtr window)
    {
        if (!_animationActive)
        {
            return;
        }

        StopFlyoutAnimation();
        SetFlyoutPosition(window, _finalY, show: true);
    }

    private static void StopFlyoutAnimation()
    {
        _animationGeneration = unchecked(_animationGeneration + 1u);
        _animationActive = false;
        _animationIncoming = false;
        _destroyAfterAnimation = false;
    }

    private static double EaseDirectEntrance(double progress)
    {
        if (progress <= 0.0)
        {
            return 0.0;
        }

        if (progress >= 1.0)
        {
            return 1.0;
        }

        // Fluent direct-entrance/exit curve cubic-bezier(0, 0, 0, 1). With both x control
        // points at zero, x=t^3, so the parameter can be recovered with a cube root.
        var parameter = Math.Cbrt(progress);
        return (3.0 * parameter * parameter) - (2.0 * progress);
    }

    private static double EaseGentleExit(double progress)
    {
        if (progress <= 0.0)
        {
            return 0.0;
        }

        if (progress >= 1.0)
        {
            return 1.0;
        }

        // Fluent Gentle Exit uses cubic-bezier(1, 0, 1, 1). It is the motion-only exit
        // variant, avoiding a synthetic whole-window alpha fade that would break Acrylic.
        var parameter = 1.0 - Math.Cbrt(1.0 - progress);
        return (3.0 * parameter * parameter) -
            (2.0 * parameter * parameter * parameter);
    }

    private static void SetFlyoutPosition(IntPtr window, int y, bool show)
    {
        SetWindowPos(
            window,
            HwndTopmost,
            _finalX,
            y,
            0,
            0,
            SwpNoSize | SwpNoActivate | (show ? SwpShowWindow : 0u));
    }

    private static void MoveFlyout(IntPtr window, int y)
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

    private static void PositionFlyout(IntPtr window, bool preferForegroundMonitor)
    {
        var targetWindow = preferForegroundMonitor ? GetForegroundWindow() : window;
        if (targetWindow == IntPtr.Zero)
        {
            targetWindow = window;
        }

        var monitor = MonitorFromWindow(targetWindow, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            cbSize = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            monitor = MonitorFromWindow(window, MonitorDefaultToPrimary);
            monitorInfo.cbSize = (uint)Marshal.SizeOf<MonitorInfo>();
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }
        }

        if (!IsWindowVisible(window))
        {
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
        if (dpi == 0)
        {
            dpi = 96;
        }

        var width = DipToPx(SettingsFlyoutLayout.WidthDip, dpi);
        var height = DipToPx(SettingsFlyoutLayout.HeightDip, dpi);
        var margin = DipToPx(SettingsFlyoutLayout.EdgeMarginDip, dpi);

        // Unlike the configurable hardware OSD, this is taskbar-adjacent UI. Anchor it to the
        // same lower-right work-area edge as Windows Quick Settings instead of the OSD position.
        _finalX = monitorInfo.rcWork.Right - width - margin;
        _finalY = monitorInfo.rcWork.Bottom - height - margin;

        // Match OsdPresenter's taskbar-directed exit. Continue through the physical monitor edge
        // with enough extra clearance for the rounded shadow/backdrop before hiding the HWND.
        var offScreenVisualClearance = Math.Max(1, height / 2);
        _hideOffScreenY = monitorInfo.rcMonitor.Bottom + offScreenVisualClearance;

        SetWindowPos(window, HwndTopmost, _finalX, _finalY, width, height, SwpNoActivate);
    }

    private static void ResetState()
    {
        _pendingResponse = null;
        _value = 60;
        _minimum = 60;
        _maximum = 100;
        _step = 5;
        _committedValue = 60;
        _operatingMode = null;
        _committedOperatingMode = null;
        _hoveredOperatingMode = null;
        _pressedOperatingMode = null;
        _laptopDisplayMode = LaptopDisplayMode.Auto;
        _committedLaptopDisplayMode = LaptopDisplayMode.Auto;
        _hoveredLaptopDisplayMode = null;
        _pressedLaptopDisplayMode = null;
        _focusedControl = SettingsFlyoutFocusedControl.BatteryChargeLimit;
        _isAvailable = false;
        _isLoading = false;
        _isApplying = false;
        _isDragging = false;
        _trackingMouseLeave = false;
        _showFocusVisual = false;
        _closeAfterOperation = false;
        _isDismissing = false;
        _animationActive = false;
        _animationIncoming = false;
        _destroyAfterAnimation = false;
        _animationGeneration = unchecked(_animationGeneration + 1u);
        _animationStartTimestamp = 0;
        _animationStartY = 0;
        _animationEndY = 0;
        _animationLastPresentedY = 0;
        _finalX = 0;
        _finalY = 0;
        _hideOffScreenY = 0;
        _statusText = null;
    }

    private static int GetMouseX(IntPtr lParam) =>
        unchecked((short)(lParam.ToInt64() & 0xffff));

    private static int GetMouseY(IntPtr lParam) =>
        unchecked((short)((lParam.ToInt64() >> 16) & 0xffff));

    [DllImport("user32.dll", EntryPoint = "InvalidateRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRectArea(IntPtr window, ref Rect rect, bool erase);

}
