using System.Diagnostics;
using System.Runtime.InteropServices;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;
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
    private static MiniLedMode _miniLedMode = MiniLedMode.MultiZone;
    private static MiniLedMode _committedMiniLedMode = MiniLedMode.MultiZone;
    private static MiniLedMode? _hoveredMiniLedMode;
    private static MiniLedMode? _pressedMiniLedMode;
    private static SplendidVisualMode _splendidVisualMode = SplendidVisualMode.Default;
    private static SplendidVisualMode _committedSplendidVisualMode = SplendidVisualMode.Default;
    private static SplendidGamutMode _splendidGamutMode = SplendidGamutMode.Native;
    private static SplendidGamutMode _committedSplendidGamutMode = SplendidGamutMode.Native;
    private static SplendidColorTemperature _splendidColorTemperature = SplendidColorTemperature.Neutral;
    private static SplendidColorTemperature _committedSplendidColorTemperature = SplendidColorTemperature.Neutral;
    private static SplendidProfileSelector? _hoveredSplendidSelector;
    private static SplendidProfileSelector? _pressedSplendidSelector;
    private static SplendidProfileSelector? _openSplendidPopup;
    private static int? _hoveredSplendidPopupIndex;
    private static int? _pressedSplendidPopupIndex;
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

    /// <summary>Creates the fly-out when necessary and activates it in the UI process.</summary>
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

        // Disable the default DWM transition; this window provides its own animation.
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

                // Do not route pointer input through an open popup.
                if (_openSplendidPopup is { } openSplendidSelector)
                {
                    if (CanEditSplendidSelector(openSplendidSelector) && TryGetSplendidPopupIndexAtPoint(
                        window,
                        openSplendidSelector,
                        GetMouseX(lParam),
                        GetMouseY(lParam),
                        out var popupIndex))
                    {
                        KillTimer(window, ApplyDebounceTimerId);
                        _focusedControl = GetFocusedControl(openSplendidSelector);
                        _hoveredSplendidPopupIndex = popupIndex;
                        _pressedSplendidPopupIndex = popupIndex;
                        EnsureMouseLeaveTracking(window);
                        SetCapture(window);
                        InvalidateDipRect(
                            window,
                            SettingsFlyoutLayout.GetSplendidPopupItemRect(openSplendidSelector, popupIndex));
                        return IntPtr.Zero;
                    }

                    if (!IsSplendidSelectorAtPoint(
                        window, openSplendidSelector, GetMouseX(lParam), GetMouseY(lParam)))
                    {
                        CloseSplendidPopup(window);
                        return IntPtr.Zero;
                    }
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

                if (CanEdit() &&
                    TryGetMiniLedModeAtPoint(
                        window,
                        GetMouseX(lParam),
                        GetMouseY(lParam),
                        out var miniLedMode))
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    _focusedControl = SettingsFlyoutFocusedControl.MiniLedMode;
                    var hoveredMiniLedModeBeforePress = _hoveredMiniLedMode;
                    _hoveredMiniLedMode = miniLedMode;
                    _pressedMiniLedMode = miniLedMode;
                    EnsureMouseLeaveTracking(window);
                    SetCapture(window);
                    InvalidateMiniLedModeTile(window, hoveredMiniLedModeBeforePress);
                    InvalidateMiniLedModeTile(window, miniLedMode);
                    return IntPtr.Zero;
                }

                if (TryGetSplendidSelectorAtPoint(
                    window, GetMouseX(lParam), GetMouseY(lParam), out var splendidSelector) &&
                    CanEditSplendidSelector(splendidSelector))
                {
                    KillTimer(window, ApplyDebounceTimerId);
                    _focusedControl = GetFocusedControl(splendidSelector);
                    _hoveredSplendidSelector = splendidSelector;
                    _pressedSplendidSelector = splendidSelector;
                    EnsureMouseLeaveTracking(window);
                    SetCapture(window);
                    InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(splendidSelector));
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
                if (_hoveredMiniLedMode is { } previouslyHoveredMiniLedMode)
                {
                    _hoveredMiniLedMode = null;
                    InvalidateMiniLedModeTile(window, previouslyHoveredMiniLedMode);
                }
                if (_hoveredSplendidSelector is { } hoveredSplendidSelector)
                {
                    _hoveredSplendidSelector = null;
                    InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(hoveredSplendidSelector));
                }
                if (_hoveredSplendidPopupIndex is { } hoveredSplendidIndex &&
                    _openSplendidPopup is { } hoveredPopupSelector)
                {
                    _hoveredSplendidPopupIndex = null;
                    InvalidateDipRect(
                        window,
                        SettingsFlyoutLayout.GetSplendidPopupItemRect(hoveredPopupSelector, hoveredSplendidIndex));
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

                if (_pressedMiniLedMode is { } pressedMiniLedMode)
                {
                    var shouldCommit = CanEdit() &&
                        TryGetMiniLedModeAtPoint(
                            window,
                            GetMouseX(lParam),
                            GetMouseY(lParam),
                            out var releasedMiniLedMode) &&
                        releasedMiniLedMode == pressedMiniLedMode;
                    _pressedMiniLedMode = null;
                    ReleaseCapture();
                    UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                    InvalidateMiniLedModeTile(window, pressedMiniLedMode);
                    if (shouldCommit)
                    {
                        SetMiniLedMode(window, pressedMiniLedMode);
                        BeginApply(window);
                    }
                }

                if (_pressedSplendidPopupIndex is { } pressedSplendidIndex &&
                    _openSplendidPopup is { } pressedPopupSelector)
                {
                    var shouldCommit = CanEditSplendidSelector(pressedPopupSelector) &&
                        TryGetSplendidPopupIndexAtPoint(
                            window,
                            pressedPopupSelector,
                            GetMouseX(lParam),
                            GetMouseY(lParam),
                            out var releasedSplendidIndex) &&
                        releasedSplendidIndex == pressedSplendidIndex;
                    _pressedSplendidPopupIndex = null;
                    ReleaseCapture();
                    if (shouldCommit)
                    {
                        ApplySplendidPopupSelection(window, pressedPopupSelector, pressedSplendidIndex);
                        CloseSplendidPopup(window);
                        BeginApply(window);
                    }
                    else
                    {
                        UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                        InvalidateDipRect(
                            window,
                            SettingsFlyoutLayout.GetSplendidPopupItemRect(
                                pressedPopupSelector, pressedSplendidIndex));
                    }
                }

                if (_pressedSplendidSelector is { } pressedSplendidSelector)
                {
                    var shouldToggle = CanEditSplendidSelector(pressedSplendidSelector) &&
                        IsSplendidSelectorAtPoint(
                            window, pressedSplendidSelector, GetMouseX(lParam), GetMouseY(lParam));
                    _pressedSplendidSelector = null;
                    ReleaseCapture();
                    UpdateTileHover(window, GetMouseX(lParam), GetMouseY(lParam));
                    InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(pressedSplendidSelector));
                    if (shouldToggle)
                    {
                        SetSplendidPopupOpen(
                            window,
                            _openSplendidPopup == pressedSplendidSelector ? null : pressedSplendidSelector);
                    }
                }
                return IntPtr.Zero;

            case WmCaptureChanged:
                if (_isDragging ||
                    _pressedOperatingMode is not null ||
                    _pressedLaptopDisplayMode is not null ||
                    _pressedMiniLedMode is not null ||
                    _pressedSplendidSelector is not null ||
                    _pressedSplendidPopupIndex is not null)
                {
                    var wasDragging = _isDragging;
                    var previouslyPressedMode = _pressedOperatingMode;
                    var previouslyPressedDisplayMode = _pressedLaptopDisplayMode;
                    var previouslyPressedMiniLedMode = _pressedMiniLedMode;
                    var previouslyPressedSplendidSelector = _pressedSplendidSelector;
                    var capturedSplendidPopupIndex = _pressedSplendidPopupIndex;
                    var capturedPopupSelector = _openSplendidPopup;
                    _isDragging = false;
                    _pressedOperatingMode = null;
                    _pressedLaptopDisplayMode = null;
                    _pressedMiniLedMode = null;
                    _pressedSplendidSelector = null;
                    _pressedSplendidPopupIndex = null;
                    if (wasDragging)
                    {
                        InvalidateDipRect(window, SettingsFlyoutLayout.SliderVisualRect);
                    }
                    InvalidateOperatingModeTile(window, previouslyPressedMode);
                    InvalidateLaptopDisplayModeTile(window, previouslyPressedDisplayMode);
                    InvalidateMiniLedModeTile(window, previouslyPressedMiniLedMode);
                    if (previouslyPressedSplendidSelector is { } capturedSplendidSelector)
                    {
                        InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(capturedSplendidSelector));
                    }
                    if (capturedSplendidPopupIndex is { } splendidIndex && capturedPopupSelector is { } popupSelector)
                    {
                        InvalidateDipRect(
                            window,
                            SettingsFlyoutLayout.GetSplendidPopupItemRect(popupSelector, splendidIndex));
                    }
                }
                return IntPtr.Zero;

            case WmKeyDown:
                var key = (int)wParam.ToUInt64();
                if (key == VkEscape)
                {
                    if (_openSplendidPopup is not null)
                    {
                        CloseSplendidPopup(window);
                    }
                    else
                    {
                        RequestClose(window, commitPendingChange: false);
                    }
                    return IntPtr.Zero;
                }

                if (key == VkTab)
                {
                    if (_openSplendidPopup is not null)
                    {
                        CloseSplendidPopup(window);
                    }

                    var previouslyFocusedControl = _focusedControl;
                    var focusWasVisible = _showFocusVisual;
                    _focusedControl = GetNextFocusedControl(_focusedControl);
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
                        SettingsFlyoutFocusedControl.MiniLedMode => HandleMiniLedModeKeyAdjustment(window, key),
                        SettingsFlyoutFocusedControl.SplendidVisualMode =>
                            HandleSplendidSelectorKeyAdjustment(window, SplendidProfileSelector.VisualMode, key),
                        SettingsFlyoutFocusedControl.SplendidGamutMode =>
                            HandleSplendidSelectorKeyAdjustment(window, SplendidProfileSelector.GamutMode, key),
                        SettingsFlyoutFocusedControl.SplendidColorTemperature =>
                            HandleSplendidSelectorKeyAdjustment(window, SplendidProfileSelector.ColorTemperature, key),
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
        var miniLedMode = _miniLedMode != _committedMiniLedMode
            ? _miniLedMode
            : (MiniLedMode?)null;
        var splendidVisualMode = _splendidVisualMode != _committedSplendidVisualMode
            ? _splendidVisualMode
            : (SplendidVisualMode?)null;
        var splendidGamutMode = _splendidGamutMode != _committedSplendidGamutMode
            ? _splendidGamutMode
            : (SplendidGamutMode?)null;
        var splendidColorTemperature = _splendidColorTemperature != _committedSplendidColorTemperature
            ? _splendidColorTemperature
            : (SplendidColorTemperature?)null;
        var patch = new HardwareSettingsPatch(
            chargeLimit,
            operatingMode,
            laptopDisplayMode,
            miniLedMode,
            splendidVisualMode,
            splendidGamutMode,
            splendidColorTemperature);
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
            _miniLedMode = _committedMiniLedMode;
            _splendidVisualMode = _committedSplendidVisualMode;
            _splendidGamutMode = _committedSplendidGamutMode;
            _splendidColorTemperature = _committedSplendidColorTemperature;
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
        _committedMiniLedMode = snapshot.MiniLedMode;
        _miniLedMode = _committedMiniLedMode;
        _committedSplendidVisualMode = snapshot.SplendidVisualMode;
        _splendidVisualMode = _committedSplendidVisualMode;
        _committedSplendidGamutMode = snapshot.SplendidGamutMode;
        _splendidGamutMode = _committedSplendidGamutMode;
        _committedSplendidColorTemperature = snapshot.SplendidColorTemperature;
        _splendidColorTemperature = _committedSplendidColorTemperature;
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

    private static bool HandleMiniLedModeKeyAdjustment(IntPtr window, int key)
    {
        MiniLedMode next;
        switch (key)
        {
            case VkLeft:
            case VkDown:
                next = _miniLedMode switch
                {
                    MiniLedMode.MultiZoneStrong => MiniLedMode.MultiZone,
                    MiniLedMode.MultiZone => MiniLedMode.OneZone,
                    _ => MiniLedMode.OneZone,
                };
                break;
            case VkRight:
            case VkUp:
                next = _miniLedMode switch
                {
                    MiniLedMode.OneZone => MiniLedMode.MultiZone,
                    MiniLedMode.MultiZone => MiniLedMode.MultiZoneStrong,
                    _ => MiniLedMode.MultiZoneStrong,
                };
                break;
            case VkHome:
                next = MiniLedMode.OneZone;
                break;
            case VkEnd:
                next = MiniLedMode.MultiZoneStrong;
                break;
            default:
                return false;
        }

        SetMiniLedMode(window, next);
        KillTimer(window, ApplyDebounceTimerId);
        SetTimer(window, ApplyDebounceTimerId, ApplyDebounceMilliseconds, IntPtr.Zero);
        return true;
    }

    private static bool HandleSplendidSelectorKeyAdjustment(
        IntPtr window,
        SplendidProfileSelector selector,
        int key)
    {
        if (!CanEditSplendidSelector(selector))
        {
            return false;
        }

        if (_openSplendidPopup != selector)
        {
            if (key is VkUp or VkDown or 0x0D or 0x20) // Up, Down, Enter, Space
            {
                SetSplendidPopupOpen(window, selector);
                return true;
            }

            return false;
        }

        var currentIndex = _hoveredSplendidPopupIndex ?? GetSplendidSelectionIndex(selector);
        var lastIndex = SettingsFlyoutLayout.GetSplendidPopupItemCount(selector) - 1;
        switch (key)
        {
            case VkUp:
                currentIndex = Math.Max(0, currentIndex - 1);
                break;
            case VkDown:
                currentIndex = Math.Min(lastIndex, currentIndex + 1);
                break;
            case VkHome:
                currentIndex = 0;
                break;
            case VkEnd:
                currentIndex = lastIndex;
                break;
            case 0x0D: // VK_RETURN
            case 0x20: // VK_SPACE
                ApplySplendidPopupSelection(window, selector, currentIndex);
                CloseSplendidPopup(window);
                BeginApply(window);
                return true;
            default:
                return false;
        }

        var previousIndex = _hoveredSplendidPopupIndex;
        _hoveredSplendidPopupIndex = currentIndex;
        if (previousIndex is { } previous)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidPopupItemRect(selector, previous));
        }

        InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidPopupItemRect(selector, currentIndex));
        return true;
    }

    private static int GetSplendidSelectionIndex(SplendidProfileSelector selector) => selector switch
    {
        SplendidProfileSelector.VisualMode => GetSplendidVisualModeIndex(_splendidVisualMode),
        SplendidProfileSelector.GamutMode => GetSplendidGamutModeIndex(_splendidGamutMode),
        SplendidProfileSelector.ColorTemperature => GetSplendidColorTemperatureIndex(_splendidColorTemperature),
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
    };

    private static int GetSplendidVisualModeIndex(SplendidVisualMode visualMode) => visualMode switch
    {
        SplendidVisualMode.Default => 0,
        SplendidVisualMode.Vivid => 1,
        SplendidVisualMode.Racing => 2,
        SplendidVisualMode.Scenery => 3,
        SplendidVisualMode.Rts => 4,
        SplendidVisualMode.Fps => 5,
        SplendidVisualMode.Cinema => 6,
        SplendidVisualMode.EyeCare => 7,
        SplendidVisualMode.EReading => 8,
        _ => 9,
    };

    private static int GetSplendidGamutModeIndex(SplendidGamutMode gamutMode) => gamutMode switch
    {
        SplendidGamutMode.Native => 0,
        SplendidGamutMode.SRgb => 1,
        SplendidGamutMode.DciP3 => 2,
        SplendidGamutMode.DisplayP3 => 3,
        _ => 0,
    };

    private static int GetSplendidColorTemperatureIndex(SplendidColorTemperature colorTemperature) =>
        colorTemperature switch
        {
            SplendidColorTemperature.Warmest => 0,
            SplendidColorTemperature.Warmer => 1,
            SplendidColorTemperature.Warm => 2,
            SplendidColorTemperature.Neutral => 3,
            SplendidColorTemperature.Cold => 4,
            SplendidColorTemperature.Colder => 5,
            SplendidColorTemperature.Coldest => 6,
            _ => 3,
        };

    private static SplendidVisualMode GetSplendidVisualModeAtIndex(int index) => index switch
    {
        0 => SplendidVisualMode.Default,
        1 => SplendidVisualMode.Vivid,
        2 => SplendidVisualMode.Racing,
        3 => SplendidVisualMode.Scenery,
        4 => SplendidVisualMode.Rts,
        5 => SplendidVisualMode.Fps,
        6 => SplendidVisualMode.Cinema,
        7 => SplendidVisualMode.EyeCare,
        8 => SplendidVisualMode.EReading,
        _ => SplendidVisualMode.Disabled,
    };

    private static SplendidGamutMode GetSplendidGamutModeAtIndex(int index) => index switch
    {
        0 => SplendidGamutMode.Native,
        1 => SplendidGamutMode.SRgb,
        2 => SplendidGamutMode.DciP3,
        _ => SplendidGamutMode.DisplayP3,
    };

    private static SplendidColorTemperature GetSplendidColorTemperatureAtIndex(int index) => index switch
    {
        0 => SplendidColorTemperature.Warmest,
        1 => SplendidColorTemperature.Warmer,
        2 => SplendidColorTemperature.Warm,
        3 => SplendidColorTemperature.Neutral,
        4 => SplendidColorTemperature.Cold,
        5 => SplendidColorTemperature.Colder,
        _ => SplendidColorTemperature.Coldest,
    };

    private static void ApplySplendidPopupSelection(
        IntPtr window,
        SplendidProfileSelector selector,
        int index)
    {
        switch (selector)
        {
            case SplendidProfileSelector.VisualMode:
                SetSplendidVisualMode(window, GetSplendidVisualModeAtIndex(index));
                break;
            case SplendidProfileSelector.GamutMode:
                SetSplendidGamutMode(window, GetSplendidGamutModeAtIndex(index));
                break;
            case SplendidProfileSelector.ColorTemperature:
                SetSplendidColorTemperature(window, GetSplendidColorTemperatureAtIndex(index));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector.");
        }
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

    private static void SetMiniLedMode(IntPtr window, MiniLedMode miniLedMode)
    {
        if (_miniLedMode == miniLedMode)
        {
            return;
        }

        var previousMiniLedMode = _miniLedMode;
        _miniLedMode = miniLedMode;
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        InvalidateMiniLedModeTile(window, previousMiniLedMode);
        InvalidateMiniLedModeTile(window, miniLedMode);
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
    }

    private static void SetSplendidVisualMode(IntPtr window, SplendidVisualMode visualMode)
    {
        if (_splendidVisualMode == visualMode)
        {
            return;
        }

        var disabledStateChanged =
            (_splendidVisualMode == SplendidVisualMode.Disabled) !=
            (visualMode == SplendidVisualMode.Disabled);
        _splendidVisualMode = visualMode;
        ClearStatus(window);
        InvalidateDipRect(window, SettingsFlyoutLayout.SplendidVisualSelectorRect);
        if (disabledStateChanged)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.SplendidGamutSelectorRect);
            InvalidateDipRect(window, SettingsFlyoutLayout.SplendidTemperatureSelectorRect);
        }
    }

    private static void SetSplendidGamutMode(IntPtr window, SplendidGamutMode gamutMode)
    {
        if (_splendidGamutMode == gamutMode)
        {
            return;
        }

        _splendidGamutMode = gamutMode;
        ClearStatus(window);
        InvalidateDipRect(window, SettingsFlyoutLayout.SplendidGamutSelectorRect);
    }

    private static void SetSplendidColorTemperature(
        IntPtr window,
        SplendidColorTemperature colorTemperature)
    {
        if (_splendidColorTemperature == colorTemperature)
        {
            return;
        }

        _splendidColorTemperature = colorTemperature;
        ClearStatus(window);
        InvalidateDipRect(window, SettingsFlyoutLayout.SplendidTemperatureSelectorRect);
    }

    private static void ClearStatus(IntPtr window)
    {
        var hadStatusText = !string.IsNullOrWhiteSpace(_statusText);
        _statusText = null;
        if (hadStatusText)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.StatusRect);
        }
    }

    private static bool CanEditSplendidSelector(SplendidProfileSelector selector) =>
        CanEdit() &&
        (selector == SplendidProfileSelector.VisualMode ||
         _splendidVisualMode != SplendidVisualMode.Disabled);

    private static SettingsFlyoutFocusedControl GetFocusedControl(SplendidProfileSelector selector) =>
        selector switch
        {
            SplendidProfileSelector.VisualMode => SettingsFlyoutFocusedControl.SplendidVisualMode,
            SplendidProfileSelector.GamutMode => SettingsFlyoutFocusedControl.SplendidGamutMode,
            SplendidProfileSelector.ColorTemperature => SettingsFlyoutFocusedControl.SplendidColorTemperature,
            _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
        };

    private static SettingsFlyoutFocusedControl GetNextFocusedControl(SettingsFlyoutFocusedControl current) =>
        current switch
        {
            SettingsFlyoutFocusedControl.BatteryChargeLimit => SettingsFlyoutFocusedControl.OperatingMode,
            SettingsFlyoutFocusedControl.OperatingMode => SettingsFlyoutFocusedControl.LaptopDisplayMode,
            SettingsFlyoutFocusedControl.LaptopDisplayMode => SettingsFlyoutFocusedControl.MiniLedMode,
            SettingsFlyoutFocusedControl.MiniLedMode => SettingsFlyoutFocusedControl.SplendidVisualMode,
            SettingsFlyoutFocusedControl.SplendidVisualMode
                when _splendidVisualMode != SplendidVisualMode.Disabled =>
                    SettingsFlyoutFocusedControl.SplendidGamutMode,
            SettingsFlyoutFocusedControl.SplendidGamutMode =>
                SettingsFlyoutFocusedControl.SplendidColorTemperature,
            _ => SettingsFlyoutFocusedControl.BatteryChargeLimit,
        };

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

    private static bool TryGetMiniLedModeAtPoint(IntPtr window, int x, int y, out MiniLedMode miniLedMode)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return SettingsFlyoutLayout.TryGetMiniLedModeAtPoint(dpi, x, y, out miniLedMode);
    }

    private static bool TryGetSplendidSelectorAtPoint(
        IntPtr window,
        int x,
        int y,
        out SplendidProfileSelector selector)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return SettingsFlyoutLayout.TryGetSplendidSelectorAtPoint(dpi, x, y, out selector);
    }

    private static bool IsSplendidSelectorAtPoint(
        IntPtr window,
        SplendidProfileSelector selector,
        int x,
        int y) =>
        TryGetSplendidSelectorAtPoint(window, x, y, out var hitSelector) &&
        hitSelector == selector;

    private static bool TryGetSplendidPopupIndexAtPoint(
        IntPtr window,
        SplendidProfileSelector selector,
        int x,
        int y,
        out int index)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return SettingsFlyoutLayout.TryGetSplendidPopupIndexAtPoint(dpi, selector, x, y, out index);
    }

    private static void SetSplendidPopupOpen(IntPtr window, SplendidProfileSelector? selector)
    {
        if (_openSplendidPopup == selector)
        {
            return;
        }

        var previousSelector = _openSplendidPopup;
        _openSplendidPopup = selector;
        _hoveredSplendidPopupIndex = selector is { } openSelector
            ? GetSplendidSelectionIndex(openSelector)
            : null;
        _pressedSplendidPopupIndex = null;

        if (previousSelector is { } previous)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(previous));
            InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidPopupRect(previous));
        }

        if (selector is { } current)
        {
            InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(current));
            InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidPopupRect(current));
        }
    }

    private static void CloseSplendidPopup(IntPtr window) =>
        SetSplendidPopupOpen(window, null);

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
        MiniLedMode? hoveredMiniLedMode = null;
        SplendidProfileSelector? hoveredSplendidSelector = null;
        int? hoveredSplendidPopupIndex = null;

        if (CanEdit())
        {
            if (_openSplendidPopup is { } openSelector)
            {
                if (TryGetSplendidPopupIndexAtPoint(window, openSelector, x, y, out var popupIndex))
                {
                    hoveredSplendidPopupIndex = popupIndex;
                }
                else if (IsSplendidSelectorAtPoint(window, openSelector, x, y))
                {
                    hoveredSplendidSelector = openSelector;
                }
            }
            else if (TryGetOperatingModeAtPoint(window, x, y, out var operatingMode))
            {
                hoveredOperatingMode = operatingMode;
            }
            else if (TryGetLaptopDisplayModeAtPoint(window, x, y, out var laptopDisplayMode))
            {
                hoveredLaptopDisplayMode = laptopDisplayMode;
            }
            else if (TryGetMiniLedModeAtPoint(window, x, y, out var miniLedMode))
            {
                hoveredMiniLedMode = miniLedMode;
            }
            else if (TryGetSplendidSelectorAtPoint(window, x, y, out var selector) &&
                     CanEditSplendidSelector(selector))
            {
                hoveredSplendidSelector = selector;
            }
        }

        if (_hoveredOperatingMode != hoveredOperatingMode)
        {
            var previous = _hoveredOperatingMode;
            _hoveredOperatingMode = hoveredOperatingMode;
            InvalidateOperatingModeTile(window, previous);
            InvalidateOperatingModeTile(window, hoveredOperatingMode);
        }

        if (_hoveredLaptopDisplayMode != hoveredLaptopDisplayMode)
        {
            var previous = _hoveredLaptopDisplayMode;
            _hoveredLaptopDisplayMode = hoveredLaptopDisplayMode;
            InvalidateLaptopDisplayModeTile(window, previous);
            InvalidateLaptopDisplayModeTile(window, hoveredLaptopDisplayMode);
        }

        if (_hoveredMiniLedMode != hoveredMiniLedMode)
        {
            var previous = _hoveredMiniLedMode;
            _hoveredMiniLedMode = hoveredMiniLedMode;
            InvalidateMiniLedModeTile(window, previous);
            InvalidateMiniLedModeTile(window, hoveredMiniLedMode);
        }

        if (_hoveredSplendidSelector != hoveredSplendidSelector)
        {
            var previous = _hoveredSplendidSelector;
            _hoveredSplendidSelector = hoveredSplendidSelector;
            if (previous is { } previousSelector)
            {
                InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(previousSelector));
            }

            if (hoveredSplendidSelector is { } currentSelector)
            {
                InvalidateDipRect(window, SettingsFlyoutLayout.GetSplendidSelectorRect(currentSelector));
            }
        }

        if (_hoveredSplendidPopupIndex != hoveredSplendidPopupIndex)
        {
            var previous = _hoveredSplendidPopupIndex;
            _hoveredSplendidPopupIndex = hoveredSplendidPopupIndex;
            if (_openSplendidPopup is { } popupSelector)
            {
                if (previous is { } previousIndex)
                {
                    InvalidateDipRect(
                        window,
                        SettingsFlyoutLayout.GetSplendidPopupItemRect(popupSelector, previousIndex));
                }

                if (hoveredSplendidPopupIndex is { } currentIndex)
                {
                    InvalidateDipRect(
                        window,
                        SettingsFlyoutLayout.GetSplendidPopupItemRect(popupSelector, currentIndex));
                }
            }
        }
    }

    /// <summary>Invalidates only the stateful surface of an operating-mode action tile.</summary>
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

    /// <summary>Invalidates only the stateful surface of a laptop-screen action tile.</summary>
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

    /// <summary>Invalidates only the stateful surface of a MiniLED action tile.</summary>
    private static void InvalidateMiniLedModeTile(IntPtr window, MiniLedMode? miniLedMode)
    {
        if (miniLedMode is not { } mode)
        {
            return;
        }

        var rect = SettingsFlyoutLayout.GetMiniLedModeRect(mode);
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
            case SettingsFlyoutFocusedControl.MiniLedMode:
                InvalidateMiniLedModeTile(window, _miniLedMode);
                break;
            case SettingsFlyoutFocusedControl.SplendidVisualMode:
                InvalidateDipRect(window, SettingsFlyoutLayout.SplendidVisualSelectorRect);
                break;
            case SettingsFlyoutFocusedControl.SplendidGamutMode:
                InvalidateDipRect(window, SettingsFlyoutLayout.SplendidGamutSelectorRect);
                break;
            case SettingsFlyoutFocusedControl.SplendidColorTemperature:
                InvalidateDipRect(window, SettingsFlyoutLayout.SplendidTemperatureSelectorRect);
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
            _miniLedMode,
            _hoveredMiniLedMode,
            _pressedMiniLedMode,
            _splendidVisualMode,
            _splendidGamutMode,
            _splendidColorTemperature,
            _hoveredSplendidSelector,
            _pressedSplendidSelector,
            _openSplendidPopup,
            _hoveredSplendidPopupIndex,
            _pressedSplendidPopupIndex,
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

        if (_isDragging ||
            _pressedOperatingMode is not null ||
            _pressedLaptopDisplayMode is not null ||
            _pressedMiniLedMode is not null ||
            _pressedSplendidSelector is not null ||
            _pressedSplendidPopupIndex is not null)
        {
            _isDragging = false;
            _pressedOperatingMode = null;
            _pressedLaptopDisplayMode = null;
            _pressedMiniLedMode = null;
            _pressedSplendidSelector = null;
            _pressedSplendidPopupIndex = null;
            ReleaseCapture();
        }

        // Clear hover state when reusing the window.
        _hoveredOperatingMode = null;
        _hoveredLaptopDisplayMode = null;
        _hoveredMiniLedMode = null;
        _hoveredSplendidSelector = null;
        _openSplendidPopup = null;
        _hoveredSplendidPopupIndex = null;
        _trackingMouseLeave = false;

        if (commitPendingChange &&
            CanEdit() &&
            (_value != _committedValue ||
             _operatingMode != _committedOperatingMode ||
             _laptopDisplayMode != _committedLaptopDisplayMode ||
             _miniLedMode != _committedMiniLedMode ||
             _splendidVisualMode != _committedSplendidVisualMode ||
             _splendidGamutMode != _committedSplendidGamutMode ||
             _splendidColorTemperature != _committedSplendidColorTemperature))
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
            _miniLedMode = _committedMiniLedMode;
            _splendidVisualMode = _committedSplendidVisualMode;
            _splendidGamutMode = _committedSplendidGamutMode;
            _splendidColorTemperature = _committedSplendidColorTemperature;
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
            // Present the final animation frame before destroying the window.
            DwmFlush();
            PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
            return;
        }

        if (incoming)
        {
            SetFlyoutPosition(window, _finalY, show: true);
            return;
        }

        // Keep the window alive until any pending settings operation completes.
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

        // For cubic-bezier(0, 0, 0, 1), x = t^3.
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

        // Use the motion-only exit curve to preserve the backdrop.
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

        _finalX = monitorInfo.rcWork.Right - width - margin;
        _finalY = monitorInfo.rcWork.Bottom - height - margin;

        // Match OsdPresenter's taskbar-directed exit.
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
        _miniLedMode = MiniLedMode.MultiZone;
        _committedMiniLedMode = MiniLedMode.MultiZone;
        _hoveredMiniLedMode = null;
        _pressedMiniLedMode = null;
        _splendidVisualMode = SplendidVisualMode.Default;
        _committedSplendidVisualMode = SplendidVisualMode.Default;
        _splendidGamutMode = SplendidGamutMode.Native;
        _committedSplendidGamutMode = SplendidGamutMode.Native;
        _splendidColorTemperature = SplendidColorTemperature.Neutral;
        _committedSplendidColorTemperature = SplendidColorTemperature.Neutral;
        _hoveredSplendidSelector = null;
        _pressedSplendidSelector = null;
        _openSplendidPopup = null;
        _hoveredSplendidPopupIndex = null;
        _pressedSplendidPopupIndex = null;
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
