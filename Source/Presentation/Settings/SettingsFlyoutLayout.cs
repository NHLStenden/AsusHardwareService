using AsusHardwareService.Presentation.Osd;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>DPI-independent geometry for the interactive Windows 11-style settings fly-out.</summary>
internal static class SettingsFlyoutLayout
{
    // Windows 11 Quick Settings is a compact, right-anchored ~360-DIP surface. Keep this fly-out
    // deliberately small so it reads as Shell UI rather than as an application settings dialog.
    internal const double WidthDip = 360.0;
    internal const double HeightDip = 108.0;
    internal const double EdgeMarginDip = 12.0;
    internal const double EntranceTranslationDip = 8.0;

    internal static readonly DipRect TitleRect = new(20.0, 12.0, 292.0, 38.0);
    internal static readonly DipRect ValueRect = new(292.0, 12.0, 340.0, 38.0);
    internal static readonly DipRect BatteryIconRect = new(20.0, 46.0, 44.0, 78.0);
    internal static readonly DipRect TrackRect = new(56.0, 59.0, 332.0, 63.0);
    internal static readonly DipRect StatusRect = new(56.0, 77.0, 340.0, 101.0);

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
}

/// <summary>Immutable paint state for one settings-fly-out frame.</summary>
internal readonly record struct SettingsFlyoutViewModel(
    int Value,
    int Minimum,
    int Maximum,
    bool IsAvailable,
    bool IsApplying,
    bool IsDragging,
    bool ShowFocusVisual,
    string? StatusText);
