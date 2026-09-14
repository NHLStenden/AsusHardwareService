using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Presentation.Osd;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>Paints the interactive settings fly-out using the same Fluent surface tokens as the OSD.</summary>
internal static class SettingsFlyoutRenderer
{
    private const string TextFontFamily = "Segoe UI Variable Text";
    private const string IconFontFamily = "Segoe Fluent Icons";
    private const string BatteryGlyph = "\uE83F"; // Battery10
    private const int ColorHighlightText = 14;
    // Segoe Fluent Icons is optically hinted at 20 DIP; avoid fractional/non-standard glyph sizes.
    private const double FluentIconFontSizeDip = 20.0;

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
                model.IsAvailable ? $"{model.BatteryChargeLimit}%" : "—",
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
                FluentIconFontSizeDip,
                DtCenter,
                model.IsAvailable ? primary : secondary,
                IconFontFamily);

            DrawSlider(deviceContext, dpi, model);
            if (model.ShowFocusVisual &&
                model.IsAvailable &&
                model.FocusedControl == SettingsFlyoutFocusedControl.BatteryChargeLimit)
            {
                DrawSliderFocus(deviceContext, dpi, model);
            }

            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                "Operating mode",
                SettingsFlyoutLayout.OperatingModeTitleRect,
                400,
                14,
                DtLeft,
                primary);

            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                model.OperatingMode == OperatingModePreset.Eco ? SpeedMediumGlyph : SpeedHighGlyph,
                SettingsFlyoutLayout.OperatingModeIconRect,
                400,
                FluentIconFontSizeDip,
                DtCenter,
                model.IsAvailable ? primary : secondary,
                IconFontFamily);

            DrawOperatingModeButton(window, deviceContext, dpi, model, OperatingModePreset.Eco, "Eco");
            DrawOperatingModeButton(window, deviceContext, dpi, model, OperatingModePreset.Normal, "Normal");
            DrawOperatingModeButton(window, deviceContext, dpi, model, OperatingModePreset.Turbo, "Turbo");

            if (model.ShowFocusVisual &&
                model.IsAvailable &&
                model.FocusedControl == SettingsFlyoutFocusedControl.OperatingMode)
            {
                DrawOperatingModeFocus(deviceContext, dpi, model.OperatingMode ?? OperatingModePreset.Normal);
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

        if (!model.IsAvailable || model.BatteryChargeLimitMaximum <= model.BatteryChargeLimitMinimum)
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

    private static void DrawOperatingModeButton(
        IntPtr window,
        IntPtr deviceContext,
        uint dpi,
        SettingsFlyoutViewModel model,
        OperatingModePreset operatingMode,
        string label)
    {
        var rect = OsdLayout.ToPixels(SettingsFlyoutLayout.GetOperatingModeRect(operatingMode), dpi);
        var selected = model.OperatingMode == operatingMode;
        var hovered = model.HoveredOperatingMode == operatingMode;
        var pressed = model.PressedOperatingMode == operatingMode && hovered;

        var surfaceColor = DrawOperatingModeSurface(
            deviceContext,
            dpi,
            rect,
            selected,
            hovered,
            pressed,
            model.IsAvailable);

        var textColor = GetOperatingModeTextColor(selected, pressed, model.IsAvailable, surfaceColor);
        DrawTextOnSurface(
            window,
            deviceContext,
            dpi,
            label,
            SettingsFlyoutLayout.GetOperatingModeRect(operatingMode),
            400,
            14,
            DtCenter,
            textColor);
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
        DrawFocusOutline(
            deviceContext,
            centerX - half,
            centerY - half,
            centerX + half,
            centerY + half,
            diameter,
            dpi);
    }

    private static void DrawOperatingModeFocus(
        IntPtr deviceContext,
        uint dpi,
        OperatingModePreset operatingMode)
    {
        var rect = OsdLayout.ToPixels(SettingsFlyoutLayout.GetOperatingModeRect(operatingMode), dpi);
        var inset = DipToPx(2.0, dpi);
        DrawFocusOutline(
            deviceContext,
            rect.Left + inset,
            rect.Top + inset,
            rect.Right - inset,
            rect.Bottom - inset,
            Math.Max(1, DipToPx(8.0, dpi)),
            dpi);
    }

    private static void DrawFocusOutline(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint dpi)
    {
        var color = OsdTheme.HighContrast
            ? GetSysColor(ColorHighlight)
            : OsdTheme.GetPrimaryTextColor();
        var pen = CreatePen(PsSolid, Math.Max(1, DipToPx(2.0, dpi)), color);
        if (pen == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, pen);
        var oldBrush = SelectObject(deviceContext, GetStockObject(NullBrush));
        try
        {
            RoundRect(deviceContext, left, top, right, bottom, radius, radius);
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
            (model.BatteryChargeLimit - model.BatteryChargeLimitMinimum) /
            (double)(model.BatteryChargeLimitMaximum - model.BatteryChargeLimitMinimum),
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
            return ColorRefToOpaqueArgb(GetSysColor(ColorWindowText));
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

    private static uint DrawOperatingModeSurface(
        IntPtr deviceContext,
        uint dpi,
        PixelRect rect,
        bool selected,
        bool hovered,
        bool pressed,
        bool isAvailable)
    {
        var fillArgb = GetOperatingModeButtonArgb(selected, hovered, pressed, isAvailable);
        var fillColor = OsdTheme.CompositeArgbOverFallbackToColorRef(fillArgb);
        var brush = CreateSolidBrush(fillColor);
        if (brush == IntPtr.Zero)
        {
            return fillColor;
        }

        var strokeColor = OsdTheme.HighContrast
            ? selected ? GetSysColor(ColorHighlightText) : GetSysColor(ColorWindowText)
            : CompositeArgbOverColorRef(
                GetOperatingModeStrokeArgb(selected, hovered, pressed, isAvailable),
                fillColor);
        var pen = CreatePen(PsSolid, Math.Max(1, DipToPx(1.0, dpi)), strokeColor);
        var oldBrush = SelectObject(deviceContext, brush);
        var oldPen = pen != IntPtr.Zero
            ? SelectObject(deviceContext, pen)
            : SelectObject(deviceContext, GetStockObject(NullPen));
        try
        {
            // WinUI's 32-DIP buttons use the normal control corner radius rather than a pill.
            // An 8-DIP ellipse gives a 4-DIP logical corner radius to GDI RoundRect.
            var cornerEllipse = Math.Max(1, DipToPx(8.0, dpi));
            RoundRect(
                deviceContext,
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom,
                cornerEllipse,
                cornerEllipse);
        }
        finally
        {
            SelectObject(deviceContext, oldPen);
            SelectObject(deviceContext, oldBrush);
            if (pen != IntPtr.Zero)
            {
                DeleteObject(pen);
            }
            DeleteObject(brush);
        }

        return fillColor;
    }

    private static uint GetOperatingModeButtonArgb(
        bool selected,
        bool hovered,
        bool pressed,
        bool isAvailable)
    {
        if (OsdTheme.HighContrast)
        {
            return selected
                ? ColorRefToOpaqueArgb(GetSysColor(ColorHighlight))
                : OsdTheme.GetFallbackSurfaceArgb();
        }

        if (!isAvailable)
        {
            return OsdTheme.IsDarkTheme ? 0x0BFFFFFFu : 0x4DF9F9F9u;
        }

        if (selected)
        {
            var accent = OsdTheme.GetAccentArgb();
            return SetArgbAlpha(accent, pressed ? 0xCCu : hovered ? 0xE6u : 0xFFu);
        }

        // These are the public WinUI ButtonBackground resources: ControlFillColorDefault,
        // ControlFillColorSecondary (pointer over), and ControlFillColorTertiary (pressed).
        if (pressed)
        {
            return OsdTheme.IsDarkTheme ? 0x08FFFFFFu : 0x4DF9F9F9u;
        }

        if (hovered)
        {
            return OsdTheme.IsDarkTheme ? 0x15FFFFFFu : 0x80F9F9F9u;
        }

        return OsdTheme.IsDarkTheme ? 0x0FFFFFFFu : 0xB3FFFFFFu;
    }

    private static uint GetOperatingModeTextColor(
        bool selected,
        bool pressed,
        bool isAvailable,
        uint surfaceColor)
    {
        if (!isAvailable)
        {
            return OsdTheme.GetSecondaryTextColor();
        }

        if (selected)
        {
            if (OsdTheme.HighContrast)
            {
                return GetSysColor(ColorHighlightText);
            }

            // Match WinUI TextOnAccentFillColorPrimary exactly. The dark-theme accent fill is a
            // deliberately light shade, so Windows uses black text; light theme uses white text.
            // Pressed AccentButtons switch to TextOnAccentFillColorSecondary.
            var textArgb = OsdTheme.IsDarkTheme
                ? pressed ? 0x80000000u : 0xFF000000u
                : pressed ? 0xB3FFFFFFu : 0xFFFFFFFFu;
            return CompositeArgbOverColorRef(textArgb, surfaceColor);
        }

        return pressed ? OsdTheme.GetSecondaryTextColor() : OsdTheme.GetPrimaryTextColor();
    }

    private static uint GetOperatingModeStrokeArgb(
        bool selected,
        bool hovered,
        bool pressed,
        bool isAvailable)
    {
        if (!isAvailable)
        {
            return OsdTheme.IsDarkTheme ? 0x12FFFFFFu : 0x0F000000u;
        }

        if (selected)
        {
            // Approximate AccentControlElevationBorderBrush with the public on-accent stroke
            // tokens. The full accent fill communicates selection; this 1-DIP line is only edge
            // definition/elevation, not the selection indicator.
            return pressed
                ? 0x00000000u
                : hovered
                    ? (OsdTheme.IsDarkTheme ? 0x23000000u : 0x66000000u)
                    : 0x14FFFFFFu;
        }

        return hovered
            ? (OsdTheme.IsDarkTheme ? 0x18FFFFFFu : 0x29000000u)
            : (OsdTheme.IsDarkTheme ? 0x12FFFFFFu : 0x0F000000u);
    }

    private static uint CompositeArgbOverColorRef(uint argb, uint backgroundColorRef)
    {
        var alpha = (argb >> 24) & 0xffu;
        var inverseAlpha = 255u - alpha;
        var foregroundRed = (argb >> 16) & 0xffu;
        var foregroundGreen = (argb >> 8) & 0xffu;
        var foregroundBlue = argb & 0xffu;
        var backgroundRed = backgroundColorRef & 0xffu;
        var backgroundGreen = (backgroundColorRef >> 8) & 0xffu;
        var backgroundBlue = (backgroundColorRef >> 16) & 0xffu;
        var red = ((foregroundRed * alpha) + (backgroundRed * inverseAlpha) + 127u) / 255u;
        var green = ((foregroundGreen * alpha) + (backgroundGreen * inverseAlpha) + 127u) / 255u;
        var blue = ((foregroundBlue * alpha) + (backgroundBlue * inverseAlpha) + 127u) / 255u;
        return Rgb((byte)red, (byte)green, (byte)blue);
    }

    private static uint SetArgbAlpha(uint argb, uint alpha) =>
        (argb & 0x00FFFFFFu) | ((alpha & 0xFFu) << 24);

    private static uint ColorRefToOpaqueArgb(uint colorRef)
    {
        var red = colorRef & 0xffu;
        var green = (colorRef >> 8) & 0xffu;
        var blue = (colorRef >> 16) & 0xffu;
        return 0xFF000000u | (red << 16) | (green << 8) | blue;
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
