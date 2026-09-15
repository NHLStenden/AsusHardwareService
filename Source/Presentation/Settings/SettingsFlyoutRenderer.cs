using System.Runtime.InteropServices;
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
    private const string EcoGlyph = "\uE8BE"; // Leaf
    private const int ColorHighlightText = 14;
    // Segoe Fluent Icons is optically hinted at 20 DIP; avoid fractional/non-standard glyph sizes.
    private const double FluentIconFontSizeDip = 20.0;

    // The settings fly-out repaints frequently while the pointer moves between mode tiles. Keep
    // the backdrop and control surfaces in a persistent DIB so DWM only sees complete frames.
    private static IntPtr _backBufferDc;
    private static IntPtr _backBufferBitmap;
    private static IntPtr _backBufferOldBitmap;
    private static IntPtr _backBufferBits;
    private static int _backBufferWidth;
    private static int _backBufferHeight;

    /// <summary>Paints the current fly-out state into the supplied window.</summary>
    internal static void Paint(IntPtr window, SettingsFlyoutViewModel model)
    {
        var paintDc = BeginPaint(window, out var paintStruct);
        if (paintDc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!GetClientRect(window, out var clientRect))
            {
                return;
            }

            var width = clientRect.Right - clientRect.Left;
            var height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var dpi = GetDpiForWindow(window);
            if (dpi == 0)
            {
                dpi = 96;
            }

            if (!EnsureBackBuffer(paintDc, width, height))
            {
                // Allocation failure is non-fatal. Preserve the original direct-paint path.
                DrawSurface(paintDc, dpi, ref clientRect, model);
                DrawForeground(window, paintDc, dpi, model);
                return;
            }

            DrawSurface(_backBufferDc, dpi, ref clientRect, model);
            var paintForeground = IntersectsForeground(paintDc, dpi);
            if (OsdTheme.HighContrast && paintForeground)
            {
                // High contrast text is ordinary GDI output and can be committed atomically too.
                DrawForeground(window, _backBufferDc, dpi, model);
            }

            var updateWidth = paintStruct.rcPaint.Right - paintStruct.rcPaint.Left;
            var updateHeight = paintStruct.rcPaint.Bottom - paintStruct.rcPaint.Top;
            if (updateWidth > 0 && updateHeight > 0)
            {
                BitBlt(
                    paintDc,
                    paintStruct.rcPaint.Left,
                    paintStruct.rcPaint.Top,
                    updateWidth,
                    updateHeight,
                    _backBufferDc,
                    paintStruct.rcPaint.Left,
                    paintStruct.rcPaint.Top,
                    SrcCopy);
            }

            if (!OsdTheme.HighContrast && paintForeground)
            {
                // DTT_COMPOSITED is intentionally drawn after the atomic surface presentation;
                // its transparent text DIBs need the real glass-backed window as their target.
                // Interactive tile invalidations do not intersect these regions, so a hover now
                // presents as one opaque buffered blit instead of repainting the Acrylic text layer.
                DrawForeground(window, paintDc, dpi, model);
            }
        }
        finally
        {
            EndPaint(window, ref paintStruct);
        }
    }

    /// <summary>Releases the persistent settings-fly-out paint buffer.</summary>
    internal static void DestroyBackBuffer()
    {
        if (_backBufferDc != IntPtr.Zero && _backBufferOldBitmap != IntPtr.Zero)
        {
            SelectObject(_backBufferDc, _backBufferOldBitmap);
        }

        if (_backBufferBitmap != IntPtr.Zero)
        {
            DeleteObject(_backBufferBitmap);
        }

        if (_backBufferDc != IntPtr.Zero)
        {
            DeleteDC(_backBufferDc);
        }

        _backBufferDc = IntPtr.Zero;
        _backBufferBitmap = IntPtr.Zero;
        _backBufferOldBitmap = IntPtr.Zero;
        _backBufferBits = IntPtr.Zero;
        _backBufferWidth = 0;
        _backBufferHeight = 0;
    }

    private static void DrawSurface(
        IntPtr deviceContext,
        uint dpi,
        ref Rect clientRect,
        SettingsFlyoutViewModel model)
    {
        FillBackground(deviceContext, ref clientRect);
        SetBkMode(deviceContext, Transparent);

        DrawSlider(deviceContext, dpi, model);
        if (model.ShowFocusVisual &&
            model.IsAvailable &&
            model.FocusedControl == SettingsFlyoutFocusedControl.BatteryChargeLimit)
        {
            DrawSliderFocus(deviceContext, dpi, model);
        }

        DrawOperatingModeTile(deviceContext, dpi, model, OperatingModePreset.Eco, EcoGlyph);
        DrawOperatingModeTile(deviceContext, dpi, model, OperatingModePreset.Normal, SpeedMediumGlyph);
        DrawOperatingModeTile(deviceContext, dpi, model, OperatingModePreset.Turbo, SpeedHighGlyph);

        if (model.ShowFocusVisual &&
            model.IsAvailable &&
            model.FocusedControl == SettingsFlyoutFocusedControl.OperatingMode)
        {
            DrawOperatingModeFocus(deviceContext, dpi, model.OperatingMode ?? OperatingModePreset.Normal);
        }
    }

    private static void DrawForeground(
        IntPtr window,
        IntPtr deviceContext,
        uint dpi,
        SettingsFlyoutViewModel model)
    {
        SetBkMode(deviceContext, Transparent);
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

        DrawOperatingModeLabel(window, deviceContext, dpi, model, OperatingModePreset.Eco, "Eco");
        // Keep the serialized protocol enum name (Normal) stable while matching the existing OSD label.
        DrawOperatingModeLabel(window, deviceContext, dpi, model, OperatingModePreset.Normal, "Balanced");
        DrawOperatingModeLabel(window, deviceContext, dpi, model, OperatingModePreset.Turbo, "Turbo");

        // Normal operation is intentionally silent, like Quick Settings. Only exceptional
        // states use the small secondary line; there is no instructional or success copy.
        if (!string.IsNullOrWhiteSpace(model.StatusText))
        {
            DrawTextOnSurface(
                window,
                deviceContext,
                dpi,
                model.StatusText,
                SettingsFlyoutLayout.StatusRect,
                400,
                12,
                DtLeft,
                secondary);
        }
    }

    /// <summary>Returns whether the current paint clip actually reaches any Acrylic-backed text region.</summary>
    private static bool IntersectsForeground(IntPtr paintDc, uint dpi)
    {
        return IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.TitleRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.ValueRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.BatteryIconRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.OperatingModeTitleRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.EcoLabelRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.BalancedLabelRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.TurboLabelRect, dpi)) ||
            IsVisible(paintDc, OsdLayout.ToPixels(SettingsFlyoutLayout.StatusRect, dpi));
    }

    /// <summary>Tests a pixel rectangle against BeginPaint's real update-region clip, not its bounding box.</summary>
    private static bool IsVisible(IntPtr paintDc, PixelRect pixelRect)
    {
        var nativeRect = new Rect
        {
            Left = pixelRect.Left,
            Top = pixelRect.Top,
            Right = pixelRect.Right,
            Bottom = pixelRect.Bottom,
        };
        return RectVisible(paintDc, ref nativeRect);
    }

    private static bool EnsureBackBuffer(IntPtr targetDc, int width, int height)
    {
        if (_backBufferDc != IntPtr.Zero &&
            _backBufferBitmap != IntPtr.Zero &&
            _backBufferBits != IntPtr.Zero &&
            _backBufferWidth == width &&
            _backBufferHeight == height)
        {
            return true;
        }

        DestroyBackBuffer();

        var memoryDc = CreateCompatibleDC(targetDc);
        if (memoryDc == IntPtr.Zero)
        {
            return false;
        }

        var bitmapInfo = new BitmapInfo
        {
            bmiHeader = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BiRgb,
            },
        };

        var bitmap = CreateDIBSection(
            targetDc, ref bitmapInfo, DibRgbColors, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
            DeleteDC(memoryDc);
            return false;
        }

        var oldBitmap = SelectObject(memoryDc, bitmap);
        if (oldBitmap == IntPtr.Zero || oldBitmap == new IntPtr(-1))
        {
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            return false;
        }

        _backBufferDc = memoryDc;
        _backBufferBitmap = bitmap;
        _backBufferOldBitmap = oldBitmap;
        _backBufferBits = bits;
        _backBufferWidth = width;
        _backBufferHeight = height;
        return true;
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

    private static void DrawOperatingModeTile(
        IntPtr deviceContext,
        uint dpi,
        SettingsFlyoutViewModel model,
        OperatingModePreset operatingMode,
        string glyph)
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
        var glyphColor = GetOperatingModeGlyphColor(
            selected,
            pressed,
            model.IsAvailable,
            surfaceColor);

        // Quick Settings actions keep the icon inside the tile and the text label outside it.
        // Keeping all stateful pixels inside this opaque surface also lets hover updates repaint
        // only the tile, without touching Acrylic-backed text elsewhere in the fly-out.
        DrawOpaqueControlText(
            deviceContext,
            dpi,
            glyph,
            SettingsFlyoutLayout.GetOperatingModeRect(operatingMode),
            400,
            FluentIconFontSizeDip,
            DtCenter,
            glyphColor,
            IconFontFamily);
    }

    private static void DrawOperatingModeLabel(
        IntPtr window,
        IntPtr deviceContext,
        uint dpi,
        SettingsFlyoutViewModel model,
        OperatingModePreset operatingMode,
        string label)
    {
        DrawTextOnSurface(
            window,
            deviceContext,
            dpi,
            label,
            SettingsFlyoutLayout.GetOperatingModeLabelRect(operatingMode),
            400,
            12,
            DtCenter,
            model.IsAvailable ? OsdTheme.GetPrimaryTextColor() : OsdTheme.GetSecondaryTextColor());
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
        var fillArgb = GetOperatingModeTileArgb(selected, hovered, pressed, isAvailable);
        var fillColor = OsdTheme.CompositeArgbOverFallbackToColorRef(fillArgb);
        var brush = CreateSolidBrush(fillColor);
        if (brush == IntPtr.Zero)
        {
            return fillColor;
        }

        var drawStroke = OsdTheme.HighContrast || !selected;
        var strokeColor = OsdTheme.HighContrast
            ? selected ? GetSysColor(ColorHighlightText) : GetSysColor(ColorWindowText)
            : CompositeArgbOverColorRef(
                GetOperatingModeStrokeArgb(hovered, pressed, isAvailable),
                fillColor);
        var pen = drawStroke
            ? CreatePen(PsSolid, Math.Max(1, DipToPx(1.0, dpi)), strokeColor)
            : IntPtr.Zero;
        var oldBrush = SelectObject(deviceContext, brush);
        var oldPen = pen != IntPtr.Zero
            ? SelectObject(deviceContext, pen)
            : SelectObject(deviceContext, GetStockObject(NullPen));
        try
        {
            // Match the compact rounded Quick Settings tile silhouette rather than a pill.
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

    private static uint GetOperatingModeTileArgb(
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
            return OsdTheme.IsDarkTheme ? 0xFF333333u : 0xFFF3F3F3u;
        }

        if (selected)
        {
            // Quick Settings uses a solid system-accent tile for an active action. Keep the same
            // accent shade that OsdTheme already resolves for the current Windows theme.
            var accent = OsdTheme.GetAccentArgb();
            if (pressed)
            {
                return BlendArgb(accent, 0xFF000000u, 0.10);
            }

            return hovered ? BlendArgb(accent, 0xFFFFFFFFu, 0.04) : accent;
        }

        // Quick Settings tiles are more substantial than a stock 32-DIP command button. Use
        // solid neutral surfaces so hover/press feedback remains visible over Acrylic in both
        // themes instead of collapsing toward the fly-out background.
        if (OsdTheme.IsDarkTheme)
        {
            return pressed ? 0xFF343434u : hovered ? 0xFF424242u : 0xFF3A3A3Au;
        }

        return pressed ? 0xFFECECECu : hovered ? 0xFFFFFFFFu : 0xFFF9F9F9u;
    }

    private static uint GetOperatingModeGlyphColor(
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

            // Match the WinUI TextOnAccentFillColor tokens used with the system accent palette:
            // dark theme accents are deliberately light and use dark foreground text; light
            // theme accents are darker and use white foreground text.
            var textArgb = OsdTheme.IsDarkTheme
                ? pressed ? 0xB3000000u : 0xFF000000u
                : pressed ? 0xB3FFFFFFu : 0xFFFFFFFFu;
            return CompositeArgbOverColorRef(textArgb, surfaceColor);
        }

        return pressed ? OsdTheme.GetSecondaryTextColor() : OsdTheme.GetPrimaryTextColor();
    }

    private static uint GetOperatingModeStrokeArgb(
        bool hovered,
        bool pressed,
        bool isAvailable)
    {
        if (!isAvailable)
        {
            return OsdTheme.IsDarkTheme ? 0x18FFFFFFu : 0x10000000u;
        }

        if (OsdTheme.IsDarkTheme)
        {
            return pressed ? 0x20FFFFFFu : hovered ? 0x38FFFFFFu : 0x28FFFFFFu;
        }

        return pressed ? 0x1A000000u : hovered ? 0x24000000u : 0x18000000u;
    }

    private static uint BlendArgb(uint source, uint target, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        static byte BlendComponent(byte sourceComponent, byte targetComponent, double blendAmount) =>
            (byte)Math.Round(sourceComponent + ((targetComponent - sourceComponent) * blendAmount));

        var red = BlendComponent(
            (byte)((source >> 16) & 0xffu),
            (byte)((target >> 16) & 0xffu),
            amount);
        var green = BlendComponent(
            (byte)((source >> 8) & 0xffu),
            (byte)((target >> 8) & 0xffu),
            amount);
        var blue = BlendComponent(
            (byte)(source & 0xffu),
            (byte)(target & 0xffu),
            amount);
        return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
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

    private static uint ColorRefToOpaqueArgb(uint colorRef)
    {
        var red = colorRef & 0xffu;
        var green = (colorRef >> 8) & 0xffu;
        var blue = (colorRef >> 16) & 0xffu;
        return 0xFF000000u | (red << 16) | (green << 8) | blue;
    }

    private static void DrawOpaqueControlText(
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

        var rect = OsdLayout.ToPixels(dipRect, dpi);
        var nativeRect = new Rect
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
        };
        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, color);
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

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RectVisible(IntPtr deviceContext, ref Rect rect);

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
