using AsusHardwareService.Asus.Performance;
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
}

/// <summary>DPI-independent geometry for the interactive Windows 11-style settings fly-out.</summary>
internal static class SettingsFlyoutLayout
{
    // Keep the Shell-like 360-DIP width. The operating-mode selector follows the Windows 11
    // Quick Settings tile rhythm, so the fly-out grows vertically rather than compressing labels
    // into command-button rectangles.
    internal const double WidthDip = 360.0;
    internal const double HeightDip = 224.0;
    internal const double EdgeMarginDip = 12.0;

    internal static readonly DipRect TitleRect = new(20.0, 12.0, 292.0, 38.0);
    internal static readonly DipRect ValueRect = new(292.0, 12.0, 340.0, 38.0);
    internal static readonly DipRect BatteryIconRect = new(20.0, 45.0, 44.0, 77.0);
    internal static readonly DipRect TrackRect = new(56.0, 59.0, 332.0, 63.0);
    internal static readonly DipRect SliderVisualRect = new(48.0, 45.0, 340.0, 79.0);

    internal static readonly DipRect OperatingModeTitleRect = new(20.0, 84.0, 340.0, 108.0);

    // Windows 11 Quick Settings separates the action surface from its label. Three equal tiles fit
    // the existing 320-DIP content width with a calm 10-DIP gutter between actions.
    internal static readonly DipRect EcoButtonRect = new(20.0, 112.0, 120.0, 160.0);
    internal static readonly DipRect BalancedButtonRect = new(130.0, 112.0, 230.0, 160.0);
    internal static readonly DipRect TurboButtonRect = new(240.0, 112.0, 340.0, 160.0);
    internal static readonly DipRect EcoLabelRect = new(20.0, 164.0, 120.0, 188.0);
    internal static readonly DipRect BalancedLabelRect = new(130.0, 164.0, 230.0, 188.0);
    internal static readonly DipRect TurboLabelRect = new(240.0, 164.0, 340.0, 188.0);
    internal static readonly DipRect StatusRect = new(20.0, 194.0, 340.0, 216.0);

    /// <summary>Returns a touch-friendly hit target around the visible slider track.</summary>
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

    /// <summary>Returns the operating-mode preset under the supplied client point, when present.</summary>
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

    /// <summary>Returns the Quick Settings-style action-tile geometry for one operating-mode option.</summary>
    internal static DipRect GetOperatingModeRect(OperatingModePreset operatingMode) => operatingMode switch
    {
        OperatingModePreset.Eco => EcoButtonRect,
        OperatingModePreset.Normal => BalancedButtonRect,
        OperatingModePreset.Turbo => TurboButtonRect,
        _ => throw new ArgumentOutOfRangeException(nameof(operatingMode), operatingMode, "Unsupported operating mode."),
    };

    /// <summary>Returns the label geometry beneath one operating-mode action tile.</summary>
    internal static DipRect GetOperatingModeLabelRect(OperatingModePreset operatingMode) => operatingMode switch
    {
        OperatingModePreset.Eco => EcoLabelRect,
        OperatingModePreset.Normal => BalancedLabelRect,
        OperatingModePreset.Turbo => TurboLabelRect,
        _ => throw new ArgumentOutOfRangeException(nameof(operatingMode), operatingMode, "Unsupported operating mode."),
    };

    /// <summary>Returns the combined tile-and-label pointer target for one operating-mode action.</summary>
    internal static DipRect GetOperatingModeHitRect(OperatingModePreset operatingMode)
    {
        var tile = GetOperatingModeRect(operatingMode);
        var label = GetOperatingModeLabelRect(operatingMode);
        return new DipRect(tile.Left, tile.Top, tile.Right, label.Bottom);
    }

    private static bool Contains(PixelRect rect, int x, int y) =>
        x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
}

/// <summary>Immutable paint state for one settings-fly-out frame.</summary>
internal readonly record struct SettingsFlyoutViewModel(
    int BatteryChargeLimit,
    int BatteryChargeLimitMinimum,
    int BatteryChargeLimitMaximum,
    OperatingModePreset? OperatingMode,
    OperatingModePreset? HoveredOperatingMode,
    OperatingModePreset? PressedOperatingMode,
    bool IsAvailable,
    bool IsApplying,
    bool IsDragging,
    bool ShowFocusVisual,
    SettingsFlyoutFocusedControl FocusedControl,
    string? StatusText);
