using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Presentation.Osd;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>Identifies the keyboard-focused control in the hardware-settings fly-out.</summary>
internal enum SettingsFlyoutFocusedControl
{
    /// <summary>The battery charge-limit slider.</summary>
    BatteryChargeLimit,

    /// <summary>The mutually-exclusive operating-mode selector.</summary>
    OperatingMode,

    /// <summary>The mutually-exclusive laptop-screen selector.</summary>
    LaptopDisplayMode,

    /// <summary>The mutually-exclusive MiniLED local-dimming selector.</summary>
    MiniLedMode,

    /// <summary>The ASUS Splendid visual-preset selector.</summary>
    SplendidVisualMode,

    /// <summary>The ASUS Splendid color-gamut selector.</summary>
    SplendidGamutMode,

    /// <summary>The ASUS Splendid color-temperature selector.</summary>
    SplendidColorTemperature,
}

/// <summary>Identifies one selector inside the grouped ASUS Splendid visual profile.</summary>
internal enum SplendidProfileSelector
{
    /// <summary>The GameVisual preset selector.</summary>
    VisualMode,

    /// <summary>The color-gamut selector.</summary>
    GamutMode,

    /// <summary>The color-temperature selector.</summary>
    ColorTemperature,
}

/// <summary>DPI-independent geometry for the interactive Windows 11-style settings fly-out.</summary>
internal static class SettingsFlyoutLayout
{
    internal const double WidthDip = 360.0;
    internal const double HeightDip = 604.0;
    internal const double EdgeMarginDip = 12.0;

    internal static readonly DipRect TitleRect = new(20.0, 12.0, 292.0, 38.0);
    internal static readonly DipRect ValueRect = new(292.0, 12.0, 340.0, 38.0);
    internal static readonly DipRect BatteryIconRect = new(20.0, 45.0, 44.0, 77.0);
    internal static readonly DipRect TrackRect = new(56.0, 59.0, 332.0, 63.0);
    internal static readonly DipRect SliderVisualRect = new(48.0, 45.0, 340.0, 79.0);

    internal static readonly DipRect OperatingModeTitleRect = new(20.0, 84.0, 340.0, 108.0);

    internal static readonly DipRect EcoButtonRect = new(20.0, 112.0, 120.0, 160.0);
    internal static readonly DipRect BalancedButtonRect = new(130.0, 112.0, 230.0, 160.0);
    internal static readonly DipRect TurboButtonRect = new(240.0, 112.0, 340.0, 160.0);
    internal static readonly DipRect EcoLabelRect = new(20.0, 164.0, 120.0, 188.0);
    internal static readonly DipRect BalancedLabelRect = new(130.0, 164.0, 230.0, 188.0);
    internal static readonly DipRect TurboLabelRect = new(240.0, 164.0, 340.0, 188.0);

    internal static readonly DipRect LaptopDisplayModeTitleRect = new(20.0, 194.0, 340.0, 218.0);
    internal static readonly DipRect DisplayAutoButtonRect = new(20.0, 222.0, 120.0, 270.0);
    internal static readonly DipRect Display60ButtonRect = new(130.0, 222.0, 230.0, 270.0);
    internal static readonly DipRect Display240ButtonRect = new(240.0, 222.0, 340.0, 270.0);
    internal static readonly DipRect DisplayAutoLabelRect = new(20.0, 274.0, 120.0, 298.0);
    internal static readonly DipRect Display60LabelRect = new(130.0, 274.0, 230.0, 298.0);
    internal static readonly DipRect Display240LabelRect = new(240.0, 274.0, 340.0, 298.0);

    internal static readonly DipRect MiniLedModeTitleRect = new(20.0, 304.0, 340.0, 328.0);
    internal static readonly DipRect MiniLedOneZoneButtonRect = new(20.0, 332.0, 120.0, 380.0);
    internal static readonly DipRect MiniLedMultiZoneButtonRect = new(130.0, 332.0, 230.0, 380.0);
    internal static readonly DipRect MiniLedStrongButtonRect = new(240.0, 332.0, 340.0, 380.0);
    internal static readonly DipRect MiniLedOneZoneLabelRect = new(20.0, 384.0, 120.0, 408.0);
    internal static readonly DipRect MiniLedMultiZoneLabelRect = new(130.0, 384.0, 230.0, 408.0);
    internal static readonly DipRect MiniLedStrongLabelRect = new(240.0, 384.0, 340.0, 408.0);

    internal static readonly DipRect SplendidTitleRect = new(20.0, 414.0, 340.0, 438.0);
    internal static readonly DipRect SplendidVisualSelectorRect = new(20.0, 442.0, 340.0, 482.0);
    internal static readonly DipRect SplendidGamutSelectorRect = new(20.0, 486.0, 340.0, 526.0);
    internal static readonly DipRect SplendidTemperatureSelectorRect = new(20.0, 530.0, 340.0, 570.0);
    internal static readonly DipRect SplendidVisualLabelRect = new(32.0, 442.0, 170.0, 482.0);
    internal static readonly DipRect SplendidGamutLabelRect = new(32.0, 486.0, 170.0, 526.0);
    internal static readonly DipRect SplendidTemperatureLabelRect = new(32.0, 530.0, 170.0, 570.0);
    internal static readonly DipRect SplendidVisualValueRect = new(170.0, 442.0, 300.0, 482.0);
    internal static readonly DipRect SplendidGamutValueRect = new(170.0, 486.0, 300.0, 526.0);
    internal static readonly DipRect SplendidTemperatureValueRect = new(170.0, 530.0, 300.0, 570.0);
    internal static readonly DipRect SplendidVisualChevronRect = new(300.0, 442.0, 332.0, 482.0);
    internal static readonly DipRect SplendidGamutChevronRect = new(300.0, 486.0, 332.0, 526.0);
    internal static readonly DipRect SplendidTemperatureChevronRect = new(300.0, 530.0, 332.0, 570.0);
    internal static readonly DipRect SplendidPopupAreaRect = new(20.0, 102.0, 340.0, 438.0);
    internal const double SplendidPopupPaddingDip = 8.0;
    internal const double SplendidPopupItemHeightDip = 32.0;
    internal static readonly DipRect StatusRect = new(20.0, 576.0, 340.0, 598.0);

    /// <summary>Gets a touch-friendly hit target around the visible slider track.</summary>
    internal static PixelRect GetSliderHitRect(uint dpi)
    {
        var track = OsdLayout.ToPixels(TrackRect, dpi);
        var horizontalPadding = DipToPx(6.0, dpi);
        var verticalPadding = DipToPx(18.0, dpi);
        return new PixelRect(
            track.Left - horizontalPadding,
            track.Top - verticalPadding,
            track.Right + horizontalPadding,
            track.Bottom + verticalPadding);
    }

    /// <summary>Gets the operating-mode preset under the supplied client point, if available.</summary>
    internal static bool TryGetOperatingModeAtPoint(
        uint dpi,
        int x,
        int y,
        out OperatingModePreset operatingMode)
    {
        if (Contains(OsdLayout.ToPixels(GetOperatingModeHitRect(OperatingModePreset.Eco), dpi), x, y))
        {
            operatingMode = OperatingModePreset.Eco;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetOperatingModeHitRect(OperatingModePreset.Normal), dpi), x, y))
        {
            operatingMode = OperatingModePreset.Normal;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetOperatingModeHitRect(OperatingModePreset.Turbo), dpi), x, y))
        {
            operatingMode = OperatingModePreset.Turbo;
            return true;
        }

        operatingMode = default;
        return false;
    }

    /// <summary>Gets the laptop-screen preset under the supplied client point, if available.</summary>
    internal static bool TryGetLaptopDisplayModeAtPoint(
        uint dpi,
        int x,
        int y,
        out LaptopDisplayMode laptopDisplayMode)
    {
        if (Contains(OsdLayout.ToPixels(GetLaptopDisplayModeHitRect(LaptopDisplayMode.Auto), dpi), x, y))
        {
            laptopDisplayMode = LaptopDisplayMode.Auto;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetLaptopDisplayModeHitRect(LaptopDisplayMode.Hz60), dpi), x, y))
        {
            laptopDisplayMode = LaptopDisplayMode.Hz60;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetLaptopDisplayModeHitRect(LaptopDisplayMode.Hz240Overdrive), dpi), x, y))
        {
            laptopDisplayMode = LaptopDisplayMode.Hz240Overdrive;
            return true;
        }

        laptopDisplayMode = default;
        return false;
    }

    /// <summary>Gets the action-tile geometry for an operating-mode option.</summary>
    internal static DipRect GetOperatingModeRect(OperatingModePreset operatingMode) => operatingMode switch
    {
        OperatingModePreset.Eco => EcoButtonRect,
        OperatingModePreset.Normal => BalancedButtonRect,
        OperatingModePreset.Turbo => TurboButtonRect,
        _ => throw new ArgumentOutOfRangeException(nameof(operatingMode), operatingMode, "Unsupported operating mode."),
    };

    /// <summary>Gets the label geometry beneath an operating-mode action tile.</summary>
    internal static DipRect GetOperatingModeLabelRect(OperatingModePreset operatingMode) => operatingMode switch
    {
        OperatingModePreset.Eco => EcoLabelRect,
        OperatingModePreset.Normal => BalancedLabelRect,
        OperatingModePreset.Turbo => TurboLabelRect,
        _ => throw new ArgumentOutOfRangeException(nameof(operatingMode), operatingMode, "Unsupported operating mode."),
    };

    /// <summary>Gets the combined tile-and-label pointer target for an operating-mode action.</summary>
    internal static DipRect GetOperatingModeHitRect(OperatingModePreset operatingMode)
    {
        var tile = GetOperatingModeRect(operatingMode);
        var label = GetOperatingModeLabelRect(operatingMode);
        return new DipRect(tile.Left, tile.Top, tile.Right, label.Bottom);
    }

    /// <summary>Gets the action-tile geometry for a laptop-screen option.</summary>
    internal static DipRect GetLaptopDisplayModeRect(LaptopDisplayMode laptopDisplayMode) => laptopDisplayMode switch
    {
        LaptopDisplayMode.Auto => DisplayAutoButtonRect,
        LaptopDisplayMode.Hz60 => Display60ButtonRect,
        LaptopDisplayMode.Hz240Overdrive => Display240ButtonRect,
        _ => throw new ArgumentOutOfRangeException(
            nameof(laptopDisplayMode),
            laptopDisplayMode,
            "Unsupported laptop display mode."),
    };

    /// <summary>Gets the label geometry beneath a laptop-screen action tile.</summary>
    internal static DipRect GetLaptopDisplayModeLabelRect(LaptopDisplayMode laptopDisplayMode) => laptopDisplayMode switch
    {
        LaptopDisplayMode.Auto => DisplayAutoLabelRect,
        LaptopDisplayMode.Hz60 => Display60LabelRect,
        LaptopDisplayMode.Hz240Overdrive => Display240LabelRect,
        _ => throw new ArgumentOutOfRangeException(
            nameof(laptopDisplayMode),
            laptopDisplayMode,
            "Unsupported laptop display mode."),
    };

    /// <summary>Gets the combined tile-and-label pointer target for a laptop-screen action.</summary>
    internal static DipRect GetLaptopDisplayModeHitRect(LaptopDisplayMode laptopDisplayMode)
    {
        var tile = GetLaptopDisplayModeRect(laptopDisplayMode);
        var label = GetLaptopDisplayModeLabelRect(laptopDisplayMode);
        return new DipRect(tile.Left, tile.Top, tile.Right, label.Bottom);
    }

    /// <summary>Gets the MiniLED local-dimming mode under the supplied client point, if available.</summary>
    internal static bool TryGetMiniLedModeAtPoint(uint dpi, int x, int y, out MiniLedMode miniLedMode)
    {
        if (Contains(OsdLayout.ToPixels(GetMiniLedModeHitRect(MiniLedMode.OneZone), dpi), x, y))
        {
            miniLedMode = MiniLedMode.OneZone;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetMiniLedModeHitRect(MiniLedMode.MultiZone), dpi), x, y))
        {
            miniLedMode = MiniLedMode.MultiZone;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(GetMiniLedModeHitRect(MiniLedMode.MultiZoneStrong), dpi), x, y))
        {
            miniLedMode = MiniLedMode.MultiZoneStrong;
            return true;
        }

        miniLedMode = default;
        return false;
    }

    /// <summary>Gets the action-tile geometry for a MiniLED option.</summary>
    internal static DipRect GetMiniLedModeRect(MiniLedMode miniLedMode) => miniLedMode switch
    {
        MiniLedMode.OneZone => MiniLedOneZoneButtonRect,
        MiniLedMode.MultiZone => MiniLedMultiZoneButtonRect,
        MiniLedMode.MultiZoneStrong => MiniLedStrongButtonRect,
        _ => throw new ArgumentOutOfRangeException(nameof(miniLedMode), miniLedMode, "Unsupported MiniLED mode."),
    };

    /// <summary>Gets the label geometry beneath a MiniLED action tile.</summary>
    internal static DipRect GetMiniLedModeLabelRect(MiniLedMode miniLedMode) => miniLedMode switch
    {
        MiniLedMode.OneZone => MiniLedOneZoneLabelRect,
        MiniLedMode.MultiZone => MiniLedMultiZoneLabelRect,
        MiniLedMode.MultiZoneStrong => MiniLedStrongLabelRect,
        _ => throw new ArgumentOutOfRangeException(nameof(miniLedMode), miniLedMode, "Unsupported MiniLED mode."),
    };

    /// <summary>Gets the combined tile-and-label pointer target for a MiniLED action.</summary>
    internal static DipRect GetMiniLedModeHitRect(MiniLedMode miniLedMode)
    {
        var tile = GetMiniLedModeRect(miniLedMode);
        var label = GetMiniLedModeLabelRect(miniLedMode);
        return new DipRect(tile.Left, tile.Top, tile.Right, label.Bottom);
    }

    /// <summary>Gets the setting-row geometry for an ASUS Splendid profile selector.</summary>
    internal static DipRect GetSplendidSelectorRect(SplendidProfileSelector selector) => selector switch
    {
        SplendidProfileSelector.VisualMode => SplendidVisualSelectorRect,
        SplendidProfileSelector.GamutMode => SplendidGamutSelectorRect,
        SplendidProfileSelector.ColorTemperature => SplendidTemperatureSelectorRect,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
    };

    /// <summary>Gets the value-text geometry for an ASUS Splendid profile selector.</summary>
    internal static DipRect GetSplendidValueRect(SplendidProfileSelector selector) => selector switch
    {
        SplendidProfileSelector.VisualMode => SplendidVisualValueRect,
        SplendidProfileSelector.GamutMode => SplendidGamutValueRect,
        SplendidProfileSelector.ColorTemperature => SplendidTemperatureValueRect,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
    };

    /// <summary>Gets the chevron geometry for an ASUS Splendid profile selector.</summary>
    internal static DipRect GetSplendidChevronRect(SplendidProfileSelector selector) => selector switch
    {
        SplendidProfileSelector.VisualMode => SplendidVisualChevronRect,
        SplendidProfileSelector.GamutMode => SplendidGamutChevronRect,
        SplendidProfileSelector.ColorTemperature => SplendidTemperatureChevronRect,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
    };

    /// <summary>Gets the ASUS Splendid selector under the supplied client point, if available.</summary>
    internal static bool TryGetSplendidSelectorAtPoint(
        uint dpi,
        int x,
        int y,
        out SplendidProfileSelector selector)
    {
        foreach (var candidate in Enum.GetValues<SplendidProfileSelector>())
        {
            if (Contains(OsdLayout.ToPixels(GetSplendidSelectorRect(candidate), dpi), x, y))
            {
                selector = candidate;
                return true;
            }
        }

        selector = default;
        return false;
    }

    /// <summary>Gets the popup geometry for an ASUS Splendid profile selector.</summary>
    internal static DipRect GetSplendidPopupRect(SplendidProfileSelector selector)
    {
        var itemCount = GetSplendidPopupItemCount(selector);
        var height = (itemCount * SplendidPopupItemHeightDip) + (2.0 * SplendidPopupPaddingDip);
        return new DipRect(
            SplendidPopupAreaRect.Left,
            SplendidPopupAreaRect.Bottom - height,
            SplendidPopupAreaRect.Right,
            SplendidPopupAreaRect.Bottom);
    }

    /// <summary>Gets the number of choices displayed by an ASUS Splendid selector.</summary>
    internal static int GetSplendidPopupItemCount(SplendidProfileSelector selector) => selector switch
    {
        SplendidProfileSelector.VisualMode => 10,
        SplendidProfileSelector.GamutMode => 4,
        SplendidProfileSelector.ColorTemperature => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unsupported Splendid selector."),
    };

    /// <summary>Gets the popup-row geometry for an ASUS Splendid choice.</summary>
    internal static DipRect GetSplendidPopupItemRect(SplendidProfileSelector selector, int index)
    {
        var popup = GetSplendidPopupRect(selector);
        var top = popup.Top + SplendidPopupPaddingDip + (index * SplendidPopupItemHeightDip);
        return new DipRect(
            popup.Left + SplendidPopupPaddingDip,
            top,
            popup.Right - SplendidPopupPaddingDip,
            top + SplendidPopupItemHeightDip);
    }

    /// <summary>Gets the zero-based popup row under the supplied client point.</summary>
    internal static bool TryGetSplendidPopupIndexAtPoint(
        uint dpi,
        SplendidProfileSelector selector,
        int x,
        int y,
        out int index)
    {
        for (var candidate = 0; candidate < GetSplendidPopupItemCount(selector); candidate++)
        {
            if (Contains(OsdLayout.ToPixels(GetSplendidPopupItemRect(selector, candidate), dpi), x, y))
            {
                index = candidate;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool Contains(PixelRect rect, int x, int y) =>
        x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
}

/// <summary>Immutable paint state for a settings flyout frame.</summary>
internal readonly record struct SettingsFlyoutViewModel(
    int BatteryChargeLimit,
    int BatteryChargeLimitMinimum,
    int BatteryChargeLimitMaximum,
    OperatingModePreset? OperatingMode,
    OperatingModePreset? HoveredOperatingMode,
    OperatingModePreset? PressedOperatingMode,
    LaptopDisplayMode LaptopDisplayMode,
    LaptopDisplayMode? HoveredLaptopDisplayMode,
    LaptopDisplayMode? PressedLaptopDisplayMode,
    MiniLedMode MiniLedMode,
    MiniLedMode? HoveredMiniLedMode,
    MiniLedMode? PressedMiniLedMode,
    SplendidVisualMode SplendidVisualMode,
    SplendidGamutMode SplendidGamutMode,
    SplendidColorTemperature SplendidColorTemperature,
    SplendidProfileSelector? HoveredSplendidSelector,
    SplendidProfileSelector? PressedSplendidSelector,
    SplendidProfileSelector? OpenSplendidPopup,
    int? HoveredSplendidPopupIndex,
    int? PressedSplendidPopupIndex,
    bool IsAvailable,
    bool IsApplying,
    bool IsDragging,
    bool ShowFocusVisual,
    SettingsFlyoutFocusedControl FocusedControl,
    string? StatusText);
