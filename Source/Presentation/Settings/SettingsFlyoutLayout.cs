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
    // Keep the same compact 360-DIP Shell-like width as v6. The added operating-mode row expands
    // vertically only; all dimensions remain DIPs and use the existing per-monitor conversion path.
    internal const double WidthDip = 360.0;
    internal const double HeightDip = 178.0;
    internal const double EdgeMarginDip = 12.0;

    internal static readonly DipRect TitleRect = new(20.0, 12.0, 292.0, 38.0);
    internal static readonly DipRect ValueRect = new(292.0, 12.0, 340.0, 38.0);
    internal static readonly DipRect BatteryIconRect = new(20.0, 45.0, 44.0, 77.0);
    internal static readonly DipRect TrackRect = new(56.0, 59.0, 332.0, 63.0);

    internal static readonly DipRect OperatingModeTitleRect = new(20.0, 82.0, 340.0, 106.0);
    internal static readonly DipRect OperatingModeIconRect = new(20.0, 108.0, 44.0, 140.0);
    internal static readonly DipRect EcoButtonRect = new(56.0, 108.0, 148.0, 140.0);
    internal static readonly DipRect NormalButtonRect = new(152.0, 108.0, 244.0, 140.0);
    internal static readonly DipRect TurboButtonRect = new(248.0, 108.0, 340.0, 140.0);
    internal static readonly DipRect StatusRect = new(56.0, 146.0, 340.0, 170.0);

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
        if (Contains(OsdLayout.ToPixels(EcoButtonRect, dpi), x, y))
        {
            operatingMode = OperatingModePreset.Eco;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(NormalButtonRect, dpi), x, y))
        {
            operatingMode = OperatingModePreset.Normal;
            return true;
        }

        if (Contains(OsdLayout.ToPixels(TurboButtonRect, dpi), x, y))
        {
            operatingMode = OperatingModePreset.Turbo;
            return true;
        }

        operatingMode = default;
        return false;
    }

    /// <summary>Returns the geometry for one operating-mode option.</summary>
    internal static DipRect GetOperatingModeRect(OperatingModePreset operatingMode) => operatingMode switch
    {
        OperatingModePreset.Eco => EcoButtonRect,
        OperatingModePreset.Normal => NormalButtonRect,
        OperatingModePreset.Turbo => TurboButtonRect,
        _ => throw new ArgumentOutOfRangeException(nameof(operatingMode), operatingMode, "Unsupported operating mode."),
    };

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
