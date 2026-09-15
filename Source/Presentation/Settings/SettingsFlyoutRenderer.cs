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
    private static SettingsFlyoutArgbSurface? _backBufferSurface;

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
                DrawSurface(window, paintDc, dpi, ref clientRect, model);
                DrawForeground(window, paintDc, dpi, model);
                return;
            }

            DrawSurface(window, _backBufferDc, dpi, ref clientRect, model);
            CommitBackBufferPixels();
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
        _backBufferSurface = null;
    }

    private static void DrawSurface(
        IntPtr window,
        IntPtr deviceContext,
        uint dpi,
        ref Rect clientRect,
        SettingsFlyoutViewModel model)
    {
        if (deviceContext == _backBufferDc && _backBufferSurface is not null)
        {
            PrepareBackBufferPixels();
        }
        else
        {
            FillBackground(deviceContext, ref clientRect);
        }
        SetBkMode(deviceContext, Transparent);

        DrawSlider(deviceContext, dpi, model);
        if (model.ShowFocusVisual &&
            model.IsAvailable &&
            model.FocusedControl == SettingsFlyoutFocusedControl.BatteryChargeLimit)
        {
            DrawSliderFocus(deviceContext, dpi, model);
        }

        DrawOperatingModeTile(window, deviceContext, dpi, model, OperatingModePreset.Eco, EcoGlyph);
        DrawOperatingModeTile(window, deviceContext, dpi, model, OperatingModePreset.Normal, SpeedMediumGlyph);
        DrawOperatingModeTile(window, deviceContext, dpi, model, OperatingModePreset.Turbo, SpeedHighGlyph);

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
        _backBufferSurface = new SettingsFlyoutArgbSurface(width, height);
        return true;
    }

    /// <summary>Resets the ARGB composition surface for the current fly-out backdrop mode.</summary>
    private static void PrepareBackBufferPixels()
    {
        if (_backBufferSurface is null)
        {
            return;
        }

        _backBufferSurface.Clear(
            OsdTheme.SystemBackdropEnabled ? 0u : OsdTheme.GetFallbackSurfaceArgb());
    }

    /// <summary>Copies the alpha-correct composed frame into the persistent 32-bpp DIB.</summary>
    private static void CommitBackBufferPixels() => _backBufferSurface?.CopyTo(_backBufferBits);

    private static void DrawSlider(IntPtr deviceContext, uint dpi, SettingsFlyoutViewModel model)
    {
        var track = OsdLayout.ToPixels(SettingsFlyoutLayout.TrackRect, dpi);
        DrawArgbCapsule(
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
            DrawArgbCapsule(
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
        IntPtr window,
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
        DrawOperatingModeSurface(
            deviceContext,
            dpi,
            rect,
            selected,
            hovered,
            pressed,
            model.IsAvailable);

        // Render the glyph through DTT_COMPOSITED into a temporary alpha surface, then blend that
        // surface into the persistent fly-out DIB. Ordinary GDI text leaves undefined alpha in a
        // 32-bpp Acrylic buffer, which is why light-theme icons could disappear in the prior build.
        DrawControlText(
            window,
            deviceContext,
            dpi,
            glyph,
            SettingsFlyoutLayout.GetOperatingModeRect(operatingMode),
            400,
            FluentIconFontSizeDip,
            DtCenter,
            GetOperatingModeGlyphColor(selected, pressed, model.IsAvailable),
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
        var thickness = Math.Max(1, DipToPx(2.0, dpi));

        if (deviceContext == _backBufferDc && _backBufferSurface is not null)
        {
            // GDI RoundRect receives an ellipse diameter; the alpha rasterizer receives a corner radius.
            DrawArgbRoundedRectStroke(
                deviceContext,
                left,
                top,
                right,
                bottom,
                Math.Max(1, radius / 2),
                thickness,
                ColorRefToOpaqueArgb(color),
                ColorRefToOpaqueArgb(color));
            return;
        }

        var pen = CreatePen(PsSolid, thickness, color);
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
        DrawArgbCapsule(
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

    private static void DrawArgbCapsule(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        uint argb)
    {
        var radius = Math.Max(1, Math.Min(right - left, bottom - top) / 2);
        DrawArgbRoundedRect(deviceContext, left, top, right, bottom, radius, argb);
    }

    /// <summary>
    /// Draws an antialiased rounded ARGB surface while preserving premultiplied alpha when the
    /// settings back buffer is used; the direct-paint fallback is flattened over the theme surface.
    /// </summary>
    private static void DrawArgbRoundedRect(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint argb)
    {
        if (right <= left || bottom <= top || (argb >> 24) == 0)
        {
            return;
        }

        if (deviceContext == _backBufferDc && _backBufferSurface is not null)
        {
            _backBufferSurface.DrawRoundedRect(left, top, right, bottom, radius, argb);
            return;
        }

        var brush = CreateSolidBrush(OsdTheme.CompositeArgbOverFallbackToColorRef(argb));
        if (brush == IntPtr.Zero)
        {
            return;
        }

        var oldBrush = SelectObject(deviceContext, brush);
        var oldPen = SelectObject(deviceContext, GetStockObject(NullPen));
        try
        {
            var ellipse = Math.Max(1, radius * 2);
            RoundRect(deviceContext, left, top, right, bottom, ellipse, ellipse);
        }
        finally
        {
            SelectObject(deviceContext, oldPen);
            SelectObject(deviceContext, oldBrush);
            DeleteObject(brush);
        }
    }

    /// <summary>
    /// Draws the two-tone elevation stroke used by Windows 11 controls without flattening its
    /// alpha into the Acrylic surface.
    /// </summary>
    private static void DrawArgbRoundedRectStroke(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        int thickness,
        uint topArgb,
        uint bottomArgb)
    {
        if (right <= left || bottom <= top || thickness <= 0 ||
            (((topArgb | bottomArgb) >> 24) == 0))
        {
            return;
        }

        if (deviceContext == _backBufferDc && _backBufferSurface is not null)
        {
            _backBufferSurface.DrawRoundedRectStroke(
                left,
                top,
                right,
                bottom,
                radius,
                thickness,
                topArgb,
                bottomArgb);
            return;
        }

        var midpointArgb = SettingsFlyoutArgbSurface.LerpArgb(topArgb, bottomArgb, 0.5);
        var pen = CreatePen(
            PsSolid,
            thickness,
            OsdTheme.CompositeArgbOverFallbackToColorRef(midpointArgb));
        if (pen == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, pen);
        var oldBrush = SelectObject(deviceContext, GetStockObject(NullBrush));
        try
        {
            var ellipse = Math.Max(1, radius * 2);
            RoundRect(deviceContext, left, top, right, bottom, ellipse, ellipse);
        }
        finally
        {
            SelectObject(deviceContext, oldBrush);
            SelectObject(deviceContext, oldPen);
            DeleteObject(pen);
        }
    }

    private static void DrawOperatingModeSurface(
        IntPtr deviceContext,
        uint dpi,
        PixelRect rect,
        bool selected,
        bool hovered,
        bool pressed,
        bool isAvailable)
    {
        var radius = Math.Max(1, DipToPx(4.0, dpi));
        DrawArgbRoundedRect(
            deviceContext,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom,
            radius,
            GetOperatingModeTileArgb(selected, hovered, pressed, isAvailable));

        GetOperatingModeStrokeArgb(
            selected,
            pressed,
            isAvailable,
            out var topStrokeArgb,
            out var bottomStrokeArgb);
        if (((topStrokeArgb | bottomStrokeArgb) >> 24) != 0)
        {
            DrawArgbRoundedRectStroke(
                deviceContext,
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom,
                radius,
                Math.Max(1, DipToPx(1.0, dpi)),
                topStrokeArgb,
                bottomStrokeArgb);
        }
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

        if (selected)
        {
            var accent = OsdTheme.GetAccentArgb();
            if (!isAvailable)
            {
                return OsdTheme.IsDarkTheme ? 0x28FFFFFFu : 0x37000000u;
            }

            // WinUI AccentButton uses AccentFillColorDefault, then the same accent at 90% for
            // pointer-over and 80% while pressed.
            return pressed
                ? WithAlpha(accent, 0xCC)
                : hovered
                    ? WithAlpha(accent, 0xE6)
                    : accent;
        }

        // These are the public WinUI ControlFillColor* tokens used by the standard Button style.
        // Keeping their alpha intact is important: Quick Settings tiles are material overlays, not
        // opaque gray/white rectangles flattened over Acrylic.
        if (OsdTheme.IsDarkTheme)
        {
            if (!isAvailable)
            {
                return 0x0BFFFFFFu; // ControlFillColorDisabled
            }

            return pressed
                ? 0x08FFFFFFu // ControlFillColorTertiary
                : hovered
                    ? 0x15FFFFFFu // ControlFillColorSecondary
                    : 0x0FFFFFFFu; // ControlFillColorDefault
        }

        if (!isAvailable)
        {
            return 0x4DF9F9F9u; // ControlFillColorDisabled
        }

        return pressed
            ? 0x4DF9F9F9u // ControlFillColorTertiary
            : hovered
                ? 0x80F9F9F9u // ControlFillColorSecondary
                : 0xB3FFFFFFu; // ControlFillColorDefault
    }

    private static uint GetOperatingModeGlyphColor(
        bool selected,
        bool pressed,
        bool isAvailable)
    {
        if (!isAvailable)
        {
            return OsdTheme.GetSecondaryTextColor();
        }

        if (!selected)
        {
            return pressed ? OsdTheme.GetSecondaryTextColor() : OsdTheme.GetPrimaryTextColor();
        }

        if (OsdTheme.HighContrast)
        {
            return GetSysColor(ColorHighlightText);
        }

        // TextOnAccentFillColorPrimary is black in dark theme and white in light theme. Use the
        // higher-contrast choice as a safety net for custom/legacy accent palettes whose registry
        // shade may not satisfy the usual Windows light/dark accent assumptions.
        var accent = OsdTheme.GetAccentArgb();
        var blackContrast = GetContrastRatio(accent, 0xFF000000u);
        var whiteContrast = GetContrastRatio(accent, 0xFFFFFFFFu);
        return blackContrast >= whiteContrast ? Rgb(0, 0, 0) : Rgb(255, 255, 255);
    }

    private static void GetOperatingModeStrokeArgb(
        bool selected,
        bool pressed,
        bool isAvailable,
        out uint topArgb,
        out uint bottomArgb)
    {
        if (OsdTheme.HighContrast)
        {
            var color = selected ? GetSysColor(ColorHighlightText) : GetSysColor(ColorWindowText);
            topArgb = bottomArgb = ColorRefToOpaqueArgb(color);
            return;
        }

        if (selected)
        {
            if (pressed || !isAvailable)
            {
                topArgb = bottomArgb = 0;
                return;
            }

            // AccentControlElevationBorderBrush: the selected tile keeps the subtle Windows 11
            // elevation edge instead of the bright flat outline used by the previous revision.
            topArgb = 0x14FFFFFFu; // ControlStrokeColorOnAccentDefault
            bottomArgb = OsdTheme.IsDarkTheme ? 0x23000000u : 0x66000000u;
            return;
        }

        var defaultStroke = OsdTheme.IsDarkTheme ? 0x12FFFFFFu : 0x0F000000u;
        if (pressed)
        {
            topArgb = bottomArgb = defaultStroke;
            return;
        }

        if (OsdTheme.IsDarkTheme)
        {
            topArgb = 0x18FFFFFFu;
            bottomArgb = defaultStroke;
        }
        else
        {
            // WinUI flips ControlElevationBorderBrush in light theme.
            topArgb = defaultStroke;
            bottomArgb = 0x29000000u;
        }
    }

    private static uint WithAlpha(uint argb, byte alpha) =>
        ((uint)alpha << 24) | (argb & 0x00FFFFFFu);

    private static double GetContrastRatio(uint firstArgb, uint secondArgb)
    {
        static double Linearize(byte component)
        {
            var value = component / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(uint argb)
        {
            var red = Linearize((byte)((argb >> 16) & 0xffu));
            var green = Linearize((byte)((argb >> 8) & 0xffu));
            var blue = Linearize((byte)(argb & 0xffu));
            return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        }

        var first = Luminance(firstArgb);
        var second = Luminance(secondArgb);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static uint ColorRefToOpaqueArgb(uint colorRef)
    {
        var red = colorRef & 0xffu;
        var green = (colorRef >> 8) & 0xffu;
        var blue = (colorRef >> 16) & 0xffu;
        return 0xFF000000u | (red << 16) | (green << 8) | blue;
    }

    private static void DrawControlText(
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
        if (deviceContext == _backBufferDc && _backBufferSurface is not null &&
            DrawCompositedControlTextIntoBackBuffer(
                window,
                dpi,
                text,
                rect,
                weight,
                fontSizeDip,
                alignment,
                color,
                fontFamily))
        {
            return;
        }

        // Allocation/theme failure fallback. The normal Acrylic path above is alpha-correct; this
        // direct GDI branch exists only to keep the control usable if a transient resource fails.
        var drawingIntoBackBuffer = deviceContext == _backBufferDc && _backBufferSurface is not null;
        if (drawingIntoBackBuffer)
        {
            // The managed pixels are authoritative until CommitBackBufferPixels. Commit first so
            // fallback GDI text is not immediately overwritten by the final frame copy.
            CommitBackBufferPixels();
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

        var nativeRect = new Rect
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
        };
        var oldFont = SelectObject(deviceContext, font);
        if (oldFont == IntPtr.Zero || oldFont == new IntPtr(-1))
        {
            DeleteObject(font);
            return;
        }

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
            if (drawingIntoBackBuffer)
            {
                _backBufferSurface?.CopyFrom(_backBufferBits);
            }
        }
    }

    /// <summary>
    /// Rasterizes control text as premultiplied-alpha glyphs and source-over blends it into the
    /// persistent Acrylic back buffer without replacing the tile pixels underneath.
    /// </summary>
    private static bool DrawCompositedControlTextIntoBackBuffer(
        IntPtr window,
        uint dpi,
        string text,
        PixelRect targetRect,
        int weight,
        double fontSizeDip,
        uint alignment,
        uint color,
        string fontFamily)
    {
        var surface = _backBufferSurface;
        if (surface is null || _backBufferDc == IntPtr.Zero)
        {
            return false;
        }

        var width = targetRect.Right - targetRect.Left;
        var height = targetRect.Bottom - targetRect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var memoryDc = CreateCompatibleDC(_backBufferDc);
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
            _backBufferDc,
            ref bitmapInfo,
            DibRgbColors,
            out var bits,
            IntPtr.Zero,
            0);
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
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            return false;
        }

        var oldFont = SelectObject(memoryDc, font);
        if (oldFont == IntPtr.Zero || oldFont == new IntPtr(-1))
        {
            DeleteObject(font);
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            return false;
        }

        var theme = OpenThemeData(window, "CompositedWindow::Window");

        try
        {
            if (theme == IntPtr.Zero)
            {
                return false;
            }

            var glyphPixels = new int[checked(width * height)];
            Marshal.Copy(glyphPixels, 0, bits, glyphPixels.Length);

            var localRect = new Rect
            {
                Left = 0,
                Top = 0,
                Right = width,
                Bottom = height,
            };
            var options = new DttOpts
            {
                dwSize = (uint)Marshal.SizeOf<DttOpts>(),
                dwFlags = DttComposited | DttTextColor,
                crText = color,
            };

            if (DrawThemeTextEx(
                    theme,
                    memoryDc,
                    0,
                    0,
                    text,
                    -1,
                    alignment | DtVCenter | DtSingleLine,
                    ref localRect,
                    ref options) < 0)
            {
                return false;
            }

            Marshal.Copy(bits, glyphPixels, 0, glyphPixels.Length);
            surface.BlendPremultiplied(
                glyphPixels,
                width,
                height,
                targetRect.Left,
                targetRect.Top);
            return true;
        }
        finally
        {
            if (theme != IntPtr.Zero)
            {
                CloseThemeData(theme);
            }

            if (font != IntPtr.Zero)
            {
                if (oldFont != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldFont);
                }
                DeleteObject(font);
            }

            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memoryDc, oldBitmap);
            }
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
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
