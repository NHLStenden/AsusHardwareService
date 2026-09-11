using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Osd;

/// <summary>
/// Declarative, DPI-independent geometry for Windows-11-style hardware indicators.
///
/// Coordinates are absolute DIPs measured from the top-left of the visible surface. Keeping
/// right/bottom edges absolute is intentional: rounding each edge independently avoids cumulative
/// one-pixel errors at fractional display scales.
/// </summary>
internal static class OsdLayout
{
    // Shared shell-style metrics expressed in DIPs so every template scales consistently across
    // per-monitor DPI settings while retaining its content-driven width.
    internal const double CompactSurfaceHeightDip = 45.0;
    internal const double EdgeMarginDip = 15.0;

    // The half-DIP horizontal offset provides optical alignment for the standard Fluent glyph slot
    // without changing its 32-DIP width or vertical centre.
    internal static readonly DipRect StandardIconRect = new(7.5, 2.0, 39.5, 45.0);

    // Brightness level geometry in DIPs.
    internal static readonly DipRect BrightnessTrackRect = new(44.5, 20.0, 155.0, 24.0);

    // Shared text-family inset. This is not claimed as a private Shell XAML constant; it is a
    // stable layout primitive for custom icon+text indicators that preserves the existing design.
    internal const double TextLeftDip = 48.0;
    internal const double TextRightInsetDip = 16.0;
    internal const double TextTopDip = 2.0;

    // Keyboard backlight level geometry in DIPs. The level track is 110 DIPs wide and is
    // expressed with absolute edges to keep scaling and rounding deterministic.
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
                IconRect: StandardIconRect,
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
