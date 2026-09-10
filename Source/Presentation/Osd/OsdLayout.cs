using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Osd;

/// <summary>
/// Declarative, DPI-independent geometry for Windows-11-style hardware indicators.
///
/// Coordinates are absolute DIPs measured from the top-left of the visible surface. Keeping
/// right/bottom edges absolute is intentional: subtracting separately-rounded paddings from an
/// already-rounded window size produces one-pixel errors at fractional DPIs such as 153 DPI.
/// </summary>
internal static class OsdLayout
{
    // Shared shell-style metrics. Brightness is directly measured from the supplied native
    // Windows 11 captures at 153 DPI; the other templates reuse those proven family metrics and
    // retain their content-driven widths until equivalent native captures are available.
    internal const double CompactSurfaceHeightDip = 45.0;
    internal const double EdgeMarginDip = 15.0;

    // Native/custom capture alignment leaves the standard glyph about one physical pixel left at
    // 153 DPI. A half-DIP nudge keeps the 32-DIP slot and lands on that missing pixel without
    // disturbing the already-correct vertical optical centre.
    internal static readonly DipRect StandardIconRect = new(7.5, 2.0, 39.5, 45.0);

    // Native brightness level geometry measured from the supplied captures.
    internal static readonly DipRect BrightnessTrackRect = new(44.5, 20.0, 155.0, 24.0);

    // Shared text-family inset. This is not claimed as a private Shell XAML constant; it is a
    // stable layout primitive for custom icon+text indicators that preserves the existing design.
    internal const double TextLeftDip = 48.0;
    internal const double TextRightInsetDip = 16.0;
    internal const double TextTopDip = 2.0;

    // Keyboard-backlight keeps its existing optical icon X calibration because we do not have a
    // native keyboard-backlight capture to justify moving it. Its level track was already 110 DIPs
    // wide; expressing it as absolute edges makes that shared level-family metric explicit.
    internal static readonly DipRect KeyboardIconRect = new(2.0, 2.0, 34.0, 45.0);
    internal static readonly DipRect KeyboardTrackRect = new(42.0, 20.0, 152.0, 24.0);
    internal static readonly DipRect KeyboardValueRect = new(152.0, -2.0, 192.0, 44.0);

    internal static OsdTemplate Get(OnScreenDisplayNotificationKind kind)
    {
        return kind switch
        {
            OnScreenDisplayNotificationKind.DisplayBrightness => new OsdTemplate(
                WidthDip: 170.0,
                HeightDip: CompactSurfaceHeightDip,
                EdgeMarginDip: EdgeMarginDip,
                IconRect: StandardIconRect,
                LevelTrackRect: BrightnessTrackRect,
                ValueRect: null,
                TextRect: null),

            OnScreenDisplayNotificationKind.KeyboardBacklight => new OsdTemplate(
                WidthDip: 192.0,
                HeightDip: CompactSurfaceHeightDip,
                EdgeMarginDip: EdgeMarginDip,
                IconRect: KeyboardIconRect,
                LevelTrackRect: KeyboardTrackRect,
                ValueRect: KeyboardValueRect,
                TextRect: null),

            OnScreenDisplayNotificationKind.Microphone => new OsdTemplate(
                WidthDip: 224.0,
                HeightDip: CompactSurfaceHeightDip,
                EdgeMarginDip: EdgeMarginDip,
                IconRect: StandardIconRect,
                LevelTrackRect: null,
                ValueRect: null,
                TextRect: new DipRect(TextLeftDip, TextTopDip, 224.0 - TextRightInsetDip, CompactSurfaceHeightDip)),

            OnScreenDisplayNotificationKind.PerformanceGpuMode => new OsdTemplate(
                WidthDip: 236.0,
                HeightDip: CompactSurfaceHeightDip,
                EdgeMarginDip: EdgeMarginDip,
                IconRect: StandardIconRect,
                LevelTrackRect: null,
                ValueRect: null,
                TextRect: new DipRect(TextLeftDip, TextTopDip, 236.0 - TextRightInsetDip, CompactSurfaceHeightDip)),

            _ => new OsdTemplate(
                WidthDip: 200.0,
                HeightDip: CompactSurfaceHeightDip,
                EdgeMarginDip: EdgeMarginDip,
                IconRect: StandardIconRect,
                LevelTrackRect: null,
                ValueRect: null,
                TextRect: new DipRect(TextLeftDip, TextTopDip, 200.0 - TextRightInsetDip, CompactSurfaceHeightDip)),
        };
    }

    internal static PixelRect ToPixels(DipRect rect, uint dpi)
    {
        return new PixelRect(
            DipToPx(rect.Left, dpi),
            DipToPx(rect.Top, dpi),
            DipToPx(rect.Right, dpi),
            DipToPx(rect.Bottom, dpi));
    }
}

internal readonly record struct DipRect(double Left, double Top, double Right, double Bottom);
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);

internal readonly record struct OsdTemplate(
    double WidthDip,
    double HeightDip,
    double EdgeMarginDip,
    DipRect IconRect,
    DipRect? LevelTrackRect,
    DipRect? ValueRect,
    DipRect? TextRect);
