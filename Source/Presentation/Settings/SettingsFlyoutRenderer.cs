using AsusHardwareService.Presentation.Osd;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>Paints the interactive settings fly-out using the same Fluent surface tokens as the OSD.</summary>
internal static class SettingsFlyoutRenderer
{
    private const string TextFontFamily = "Segoe UI Variable Text";
    private const string IconFontFamily = "Segoe Fluent Icons";
    private const string BatteryGlyph = "\uE83F"; // Battery10
    // Segoe Fluent Icons is optically hinted at 20 DIP; avoid fractional/non-standard glyph sizes.
    private const double BatteryIconFontSizeDip = 20.0;

    /// <summary>Paints the current fly-out state into the supplied window.</summary>
    internal static void Paint(IntPtr window, SettingsFlyoutViewModel model)
    {
        var deviceContext = BeginPaint(window, out var paintStruct);
        if (deviceContext == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!GetClientRect(window, out var clientRect))
            {
                return;
            }

            FillBackground(deviceContext, ref clientRect);
            SetBkMode(deviceContext, Transparent);

            var dpi = GetDpiForWindow(window);
            if (dpi == 0)
            {
                dpi = 96;
            }

            var primary = OsdTheme.GetPrimaryTextColor();
            var secondary = OsdTheme.GetSecondaryTextColor();

            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                "Battery charge limit",
                SettingsFlyoutLayout.TitleRect,
                400,
                14,
                DtLeft,
                primary);

            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                model.IsAvailable ? $"{model.Value}%" : "—",
                SettingsFlyoutLayout.ValueRect,
                400,
                14,
                DtRight,
                model.IsAvailable ? primary : secondary);

            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                BatteryGlyph,
                SettingsFlyoutLayout.BatteryIconRect,
                400,
                BatteryIconFontSizeDip,
                DtCenter,
                model.IsAvailable ? primary : secondary,
                IconFontFamily);

            DrawSlider(deviceContext, dpi, model);
            if (model.ShowFocusVisual && model.IsAvailable)
            {
                DrawSliderFocus(deviceContext, dpi, model);
            }

            // Normal operation is intentionally silent, like Quick Settings. Only exceptional
            // states use the small secondary line; there is no instructional or success copy.
            var status = model.StatusText;
            if (!string.IsNullOrWhiteSpace(status))
            {
                DrawTextOnSurface(
                    window,
                    deviceContext,
                    dpi,
                    status,
                    SettingsFlyoutLayout.StatusRect,
                    400,
                    12,
                    DtLeft,
                    secondary);
            }
        }
        finally
        {
            EndPaint(window, ref paintStruct);
        }
    }

    private static void DrawSlider(IntPtr deviceContext, uint dpi, SettingsFlyoutViewModel model)
    {
        var track = OsdLayout.ToPixels(SettingsFlyoutLayout.TrackRect, dpi);
        OsdRenderer.DrawFilledCapsule(
            deviceContext,
            track.Left,
            track.Top,
            track.Right,
            track.Bottom,
            GetSliderTrackArgb(model.IsAvailable));

        if (!model.IsAvailable || model.Maximum <= model.Minimum)
        {
            return;
        }

        var thumbCenterX = GetThumbCenterX(track, model);
        if (thumbCenterX > track.Left)
        {
            OsdRenderer.DrawFilledCapsule(
                deviceContext,
                track.Left,
                track.Top,
                thumbCenterX,
                track.Bottom,
                OsdTheme.GetAccentArgb());
        }

        var thumbCenterY = (track.Top + track.Bottom) / 2;
        var outerDiameter = DipToPx(20.0, dpi);
        var innerDiameter = DipToPx(model.IsDragging ? 12.0 : 10.0, dpi);
        DrawCenteredCapsule(
            deviceContext,
            thumbCenterX,
            thumbCenterY,
            outerDiameter,
            GetThumbSurfaceArgb());
        DrawCenteredCapsule(
            deviceContext,
            thumbCenterX,
            thumbCenterY,
            innerDiameter,
            OsdTheme.GetAccentArgb());
    }

    private static void DrawSliderFocus(
        IntPtr deviceContext,
        uint dpi,
        SettingsFlyoutViewModel model)
    {
        var track = OsdLayout.ToPixels(SettingsFlyoutLayout.TrackRect, dpi);
        var centerX = GetThumbCenterX(track, model);
        var centerY = (track.Top + track.Bottom) / 2;
        var diameter = DipToPx(28.0, dpi);
        var half = diameter / 2;
        var color = OsdTheme.HighContrast
            ? GetSysColor(ColorHighlight)
            : OsdTheme.GetAccentColor();
        var pen = CreatePen(PsSolid, Math.Max(1, DipToPx(2.0, dpi)), color);
        if (pen == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, pen);
        var oldBrush = SelectObject(deviceContext, GetStockObject(NullBrush));
        try
        {
            RoundRect(
                deviceContext,
                centerX - half,
                centerY - half,
                centerX + half,
                centerY + half,
                diameter,
                diameter);
        }
        finally
        {
            SelectObject(deviceContext, oldBrush);
            SelectObject(deviceContext, oldPen);
            DeleteObject(pen);
        }
    }

    private static int GetThumbCenterX(PixelRect track, SettingsFlyoutViewModel model)
    {
        var progress = Math.Clamp(
            (model.Value - model.Minimum) / (double)(model.Maximum - model.Minimum),
            0.0,
            1.0);
        return Math.Clamp(
            track.Left + (int)Math.Round((track.Right - track.Left) * progress),
            track.Left,
            track.Right);
    }

    private static void DrawCenteredCapsule(
        IntPtr deviceContext,
        int centerX,
        int centerY,
        int diameter,
        uint argb)
    {
        var half = diameter / 2;
        OsdRenderer.DrawFilledCapsule(
            deviceContext,
            centerX - half,
            centerY - half,
            centerX + ((diameter + 1) / 2),
            centerY + ((diameter + 1) / 2),
            argb);
    }

    private static uint GetSliderTrackArgb(bool isAvailable)
    {
        if (OsdTheme.HighContrast)
        {
            return 0xFF000000u | ColorRefToRgb(GetSysColor(ColorWindowText));
        }

        // Match WinUI ControlStrongFillColorDefault/Disabled, which are the neutral strong-fill
        // tokens used for this class of compact slider surface.
        if (!isAvailable)
        {
            return OsdTheme.IsDarkTheme ? 0x3FFFFFFFu : 0x51000000u;
        }

        return OsdTheme.IsDarkTheme ? 0x8BFFFFFFu : 0x72000000u;
    }

    private static uint GetThumbSurfaceArgb()
    {
        if (OsdTheme.HighContrast)
        {
            return OsdTheme.GetFallbackSurfaceArgb();
        }

        // WinUI sliders use a neutral thumb surface with an accent-colored center.
        return OsdTheme.IsDarkTheme ? 0xFF454545u : 0xFFFFFFFFu;
    }

    private static uint ColorRefToRgb(uint colorRef)
    {
        var red = colorRef & 0xffu;
        var green = (colorRef >> 8) & 0xffu;
        var blue = (colorRef >> 16) & 0xffu;
        return (red << 16) | (green << 8) | blue;
    }

    private static void DrawTextOnSurface(
        IntPtr window,
        IntPtr deviceContext,
        uint dpi,
        string text,
        DipRect dipRect,
        int weight,
        double fontSizeDip,
        uint alignment,
        uint color,
        string fontFamily = TextFontFamily)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var rect = OsdLayout.ToPixels(dipRect, dpi);
        if (!OsdTheme.HighContrast)
        {
            OsdRenderer.DrawCompositedTextOnGlass(
                window,
                deviceContext,
                dpi,
                text,
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom,
                fontFamily,
                fontSizeDip,
                weight,
                alignment,
                color);
            return;
        }

        var font = CreateFont(
            -DipToPx(fontSizeDip, dpi),
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
            4,
            0,
            fontFamily);
        if (font == IntPtr.Zero)
        {
            return;
        }

        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, color);
            var nativeRect = new Rect
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom,
            };
            DrawText(
                deviceContext,
                text,
                -1,
                ref nativeRect,
                alignment | DtVCenter | DtSingleLine);
        }
        finally
        {
            SelectObject(deviceContext, oldFont);
            DeleteObject(font);
        }
    }

    private static void FillBackground(IntPtr deviceContext, ref Rect clientRect)
    {
        var color = OsdTheme.SystemBackdropEnabled
            ? Rgb(0, 0, 0)
            : OsdTheme.HighContrast
                ? GetSysColor(ColorWindow)
                : OsdTheme.IsDarkTheme
                    ? Rgb(44, 44, 44)
                    : Rgb(249, 249, 249);
        var brush = CreateSolidBrush(color);
        if (brush == IntPtr.Zero)
        {
            return;
        }

        try
        {
            FillRect(deviceContext, ref clientRect, brush);
        }
        finally
        {
            DeleteObject(brush);
        }
    }
}
