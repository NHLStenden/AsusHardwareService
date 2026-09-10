using System.Runtime.InteropServices;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Osd;

/// <summary>
/// Paints the flyout with Win32 GDI/GDI+ and manages the flicker-free back buffer.
/// </summary>
internal static class OsdRenderer
{
    // The 14-DIP composited icon was one source pixel thinner than the native Windows glyph at
    // 153 DPI. 14.5 DIP becomes 23 px there (vs. 22 px), which fixes coverage without the much
    // larger jump to 15/16 DIP. Body/value text remains 14 DIP.
    private const double IconFontSizeDip = 14.5;

    // Persistent back buffer: the compositor only sees complete frames.
    private static IntPtr _backBufferDc;
    private static IntPtr _backBufferBitmap;
    private static IntPtr _backBufferOldBitmap;
    private static IntPtr _backBufferBits;
    private static int _backBufferWidth;
    private static int _backBufferHeight;

    // One GDI+ token for the resident HWND lifetime.
    private static bool _gdiPlusStartupAttempted;
    private static bool _gdiPlusAvailable;
    private static UIntPtr _gdiPlusToken;

    internal static void PaintStatus(IntPtr window)
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
            if (width <= 0 || height <= 0 || !EnsureBackBuffer(paintDc, width, height))
            {
                // Safe fallback: preserve direct painting if DIB allocation ever fails.
                DrawStatus(window, paintDc);
                if (!OsdTheme.HighContrast)
                {
                    DrawForegroundComposited(window, paintDc);
                }
                return;
            }

            // Draw the *entire* client into memory first. This is important for the extended
            // DWM/Acrylic frame: FillStatusBackground deliberately writes black before the
            // foreground. Direct painting lets the compositor occasionally sample that temporary
            // state during rapid key repeats, which is perceived as a blinking progress bar.
            DrawStatus(window, _backBufferDc);

            // Present the finished surface atomically from GDI's point of view.
            BitBlt(
                paintDc,
                0,
                0,
                width,
                height,
                _backBufferDc,
                0,
                0,
                SrcCopy);

            // Draw all non-high-contrast glyphs/text through the same DTT_COMPOSITED path.
            // DTT_COMPOSITED is the documented UxTheme mechanism for antialiased alpha text on
            // glass and avoids dark/light rasterization differences in the previous code.
            if (!OsdTheme.HighContrast)
            {
                DrawForegroundComposited(window, paintDc);
            }
        }
        finally
        {
            EndPaint(window, ref paintStruct);
        }
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
                biHeight = -height, // top-down BGRA DIB; required for reliable per-pixel alpha.
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

    internal static void DrawStatusForPrint(IntPtr window, IntPtr deviceContext)
    {
        DrawStatus(window, deviceContext);
        if (!OsdTheme.HighContrast)
        {
            DrawForegroundComposited(window, deviceContext);
        }
    }

    internal static void DrawStatus(IntPtr window, IntPtr deviceContext)
    {
        if (deviceContext == IntPtr.Zero || !GetClientRect(window, out var clientRect))
        {
            return;
        }

        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = OsdHost.WindowDpi == 0 ? 96u : OsdHost.WindowDpi;
        }

        if (deviceContext == _backBufferDc && _backBufferBits != IntPtr.Zero)
        {
            PrepareBackBuffer();
        }
        else
        {
            FillStatusBackground(deviceContext, ref clientRect);
        }
        SetBkMode(deviceContext, Transparent);

        switch (OsdHost.Notification.Kind)
        {
            case OnScreenDisplayNotificationKind.KeyboardBacklight:
                DrawKeyboardBacklightStatus(deviceContext, dpi, OsdHost.Notification.Value);
                break;

            case OnScreenDisplayNotificationKind.DisplayBrightness:
                DrawDisplayBrightnessStatus(deviceContext, dpi, OsdHost.Notification.Value);
                break;

            case OnScreenDisplayNotificationKind.PerformanceGpuMode:
                DrawPerformanceGpuStatus(deviceContext, dpi, OsdHost.Notification.Value);
                break;

            case OnScreenDisplayNotificationKind.Microphone:
            default:
                DrawMicrophoneStatus(deviceContext, dpi, OsdHost.Notification.Value != 0);
                break;
        }
    }

    private static void PrepareBackBuffer()
    {
        if (_backBufferBits == IntPtr.Zero || _backBufferWidth <= 0 || _backBufferHeight <= 0)
        {
            return;
        }

        var argb = OsdTheme.SystemBackdropEnabled
            ? 0x00000000u
            : OsdTheme.GetFallbackSurfaceArgb();
        var pixelCount = checked(_backBufferWidth * _backBufferHeight);
        var pixels = new int[pixelCount];
        if (argb != 0)
        {
            Array.Fill(pixels, unchecked((int)argb));
        }
        Marshal.Copy(pixels, 0, _backBufferBits, pixelCount);
    }

    private static void FillStatusBackground(IntPtr deviceContext, ref Rect clientRect)
    {
        // A black GDI fill has zeroed pixel data on an extended DWM frame, exposing the Desktop
        // Acrylic backdrop instead of covering it with the opaque fallback colour.
        if (OsdTheme.SystemBackdropEnabled)
        {
            var transparentBrush = CreateSolidBrush(Rgb(0, 0, 0));
            if (transparentBrush != IntPtr.Zero)
            {
                FillRect(deviceContext, ref clientRect, transparentBrush);
                DeleteObject(transparentBrush);
            }
            return;
        }

        // WinUI flyouts use these colours as the solid fallback when Acrylic cannot be shown.
        var backgroundColor = OsdTheme.HighContrast
            ? GetSysColor(ColorWindow)
            : OsdTheme.IsDarkTheme
                ? Rgb(44, 44, 44)
                : Rgb(249, 249, 249);
        var backgroundBrush = CreateSolidBrush(backgroundColor);
        if (backgroundBrush == IntPtr.Zero)
        {
            return;
        }

        try
        {
            FillRect(deviceContext, ref clientRect, backgroundBrush);
        }
        finally
        {
            DeleteObject(backgroundBrush);
        }
    }

    private static void DrawMicrophoneStatus(IntPtr deviceContext, uint dpi, bool muted)
    {
        var layout = OsdLayout.Get(OnScreenDisplayNotificationKind.Microphone);
        var foreground = OsdTheme.GetPrimaryTextColor();
        DrawFluentIcon(
            deviceContext,
            dpi,
            muted ? MicrophoneOffGlyph : MicrophoneOnGlyph,
            foreground,
            layout.IconRect);

        if (layout.TextRect is { } textRect)
        {
            DrawPrimaryText(
                deviceContext,
                dpi,
                muted ? "Microphone muted" : "Microphone unmuted",
                textRect);
        }
    }

    private static void DrawKeyboardBacklightStatus(IntPtr deviceContext, uint dpi, int level)
    {
        var layout = OsdLayout.Get(OnScreenDisplayNotificationKind.KeyboardBacklight);
        var foreground = OsdTheme.GetPrimaryTextColor();
        DrawFluentIcon(deviceContext, dpi, KeyboardGlyph, foreground, layout.IconRect);

        if (layout.LevelTrackRect is { } trackRect)
        {
            DrawLevelTrack(
                deviceContext,
                dpi,
                Math.Clamp(level, 0, 3) / 3.0,
                trackRect);
        }

        if (layout.ValueRect is { } valueRect)
        {
            DrawLevelValue(deviceContext, dpi, Math.Clamp(level, 0, 3).ToString(), valueRect);
        }
    }

    private static void DrawDisplayBrightnessStatus(IntPtr deviceContext, uint dpi, int brightness)
    {
        var layout = OsdLayout.Get(OnScreenDisplayNotificationKind.DisplayBrightness);
        var foreground = OsdTheme.GetPrimaryTextColor();
        DrawFluentIcon(deviceContext, dpi, BrightnessGlyph, foreground, layout.IconRect);

        if (layout.LevelTrackRect is { } trackRect)
        {
            DrawLevelTrack(
                deviceContext,
                dpi,
                Math.Clamp(brightness, 0, 100) / 100.0,
                trackRect);
        }
    }

    private static void DrawPerformanceGpuStatus(IntPtr deviceContext, uint dpi, int modeValue)
    {
        var layout = OsdLayout.Get(OnScreenDisplayNotificationKind.PerformanceGpuMode);
        var foreground = OsdTheme.GetPrimaryTextColor();
        var silent = (modeValue & 1) != 0;
        DrawFluentIcon(
            deviceContext,
            dpi,
            silent ? SpeedMediumGlyph : SpeedHighGlyph,
            foreground,
            layout.IconRect);

        var performanceMode = silent ? "Silent" : "Performance";
        var gpuMode = (modeValue & 2) != 0 ? "Eco" : "Standard";
        if (layout.TextRect is { } textRect)
        {
            DrawPrimaryText(
                deviceContext,
                dpi,
                $"{performanceMode} · {gpuMode}",
                textRect);
        }
    }

    private static void DrawLevelTrack(
        IntPtr deviceContext,
        uint dpi,
        double progress,
        DipRect trackRectDip)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var trackArgb = OsdTheme.GetLevelTrackArgb();
        var accentArgb = OsdTheme.GetAccentArgb();
        var track = OsdLayout.ToPixels(trackRectDip, dpi);

        DrawFilledCapsule(
            deviceContext,
            track.Left,
            track.Top,
            track.Right,
            track.Bottom,
            trackArgb);

        var trackWidth = track.Right - track.Left;
        var progressRight = track.Left + (int)Math.Round(trackWidth * progress);
        if (progress > 0.0)
        {
            DrawFilledCapsule(
                deviceContext,
                track.Left,
                track.Top,
                progressRight,
                track.Bottom,
                accentArgb);
        }
    }

    private static void DrawLevelValue(IntPtr deviceContext, uint dpi, string value, DipRect rectDip)
    {
        var rect = OsdLayout.ToPixels(rectDip, dpi);
        DrawTextCore(
            deviceContext,
            dpi,
            value,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom,
            400,
            14,
            DtCenter);
    }

    private static void DrawPrimaryText(
        IntPtr deviceContext,
        uint dpi,
        string text,
        DipRect rectDip)
    {
        var rect = OsdLayout.ToPixels(rectDip, dpi);
        DrawTextCore(
            deviceContext,
            dpi,
            text,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom,
            400,
            14);
    }

    private static void DrawFluentIcon(
        IntPtr deviceContext,
        uint dpi,
        string glyph,
        uint color,
        DipRect rectDip)
    {
        // Normal themes use one common DTT_COMPOSITED foreground path after the back buffer is
        // presented. High contrast deliberately remains ordinary GDI on its opaque system surface.
        if (!OsdTheme.HighContrast)
        {
            return;
        }

        var font = CreateFont(
            -DipToPx(IconFontSizeDip, dpi),
            0,
            0,
            0,
            400,
            false,
            false,
            false,
            1,
            0,
            0,
            4,
            0,
            "Segoe Fluent Icons");
        if (font == IntPtr.Zero)
        {
            return;
        }

        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, color);
            var pixelRect = OsdLayout.ToPixels(rectDip, dpi);
            var iconRect = new Rect
            {
                Left = pixelRect.Left,
                Top = pixelRect.Top,
                Right = pixelRect.Right,
                Bottom = pixelRect.Bottom,
            };
            DrawText(
                deviceContext,
                glyph,
                -1,
                ref iconRect,
                DtCenter | DtVCenter | DtSingleLine);
        }
        finally
        {
            SelectObject(deviceContext, oldFont);
            DeleteObject(font);
        }
    }

    private static void DrawTextCore(
        IntPtr deviceContext,
        uint dpi,
        string text,
        int left,
        int top,
        int right,
        int bottom,
        int weight,
        int fontSizeDip,
        uint horizontalAlignment = DtLeft)
    {
        if (!OsdTheme.HighContrast)
        {
            return;
        }
        var font = CreateFont(
            -Scale(fontSizeDip, dpi),
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
            4, // ANTIALIASED_QUALITY; avoid ClearType colour fringes over Acrylic.
            0,
            "Segoe UI Variable Text");
        if (font == IntPtr.Zero)
        {
            return;
        }

        var oldFont = SelectObject(deviceContext, font);
        try
        {
            SetTextColor(deviceContext, OsdTheme.GetPrimaryTextColor());
            var textRect = new Rect
            {
                Left = left,
                Top = top,
                Right = right,
                Bottom = bottom,
            };
            DrawText(deviceContext, text, -1, ref textRect, horizontalAlignment | DtVCenter | DtSingleLine);
        }
        finally
        {
            SelectObject(deviceContext, oldFont);
            DeleteObject(font);
        }
    }

    private static void DrawForegroundComposited(IntPtr window, IntPtr deviceContext)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = OsdHost.WindowDpi == 0 ? 96u : OsdHost.WindowDpi;
        }

        var kind = OsdHost.Notification.Kind;
        var layout = OsdLayout.Get(kind);
        var color = OsdTheme.GetPrimaryTextColor();
        var iconRect = OsdLayout.ToPixels(layout.IconRect, dpi);

        switch (kind)
        {
            case OnScreenDisplayNotificationKind.KeyboardBacklight:
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, KeyboardGlyph,
                    iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom,
                    "Segoe Fluent Icons", IconFontSizeDip, 400, DtCenter, color);

                if (layout.ValueRect is { } valueRectDip)
                {
                    var valueRect = OsdLayout.ToPixels(valueRectDip, dpi);
                    DrawCompositedTextOnGlass(
                        window, deviceContext, dpi,
                        Math.Clamp(OsdHost.Notification.Value, 0, 3).ToString(),
                        valueRect.Left, valueRect.Top, valueRect.Right, valueRect.Bottom,
                        "Segoe UI Variable Text", 14, 400, DtCenter, color);
                }
                break;

            case OnScreenDisplayNotificationKind.DisplayBrightness:
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, BrightnessGlyph,
                    iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom,
                    "Segoe Fluent Icons", IconFontSizeDip, 400, DtCenter, color);
                break;

            case OnScreenDisplayNotificationKind.PerformanceGpuMode:
                var silent = (OsdHost.Notification.Value & 1) != 0;
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, silent ? SpeedMediumGlyph : SpeedHighGlyph,
                    iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom,
                    "Segoe Fluent Icons", IconFontSizeDip, 400, DtCenter, color);

                var performanceMode = silent ? "Silent" : "Performance";
                var gpuMode = (OsdHost.Notification.Value & 2) != 0 ? "Eco" : "Standard";
                if (layout.TextRect is { } performanceTextRectDip)
                {
                    var textRect = OsdLayout.ToPixels(performanceTextRectDip, dpi);
                    DrawCompositedTextOnGlass(
                        window, deviceContext, dpi, $"{performanceMode} · {gpuMode}",
                        textRect.Left, textRect.Top, textRect.Right, textRect.Bottom,
                        "Segoe UI Variable Text", 14, 400, DtLeft, color);
                }
                break;

            case OnScreenDisplayNotificationKind.Microphone:
            default:
                var muted = OsdHost.Notification.Value != 0;
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, muted ? MicrophoneOffGlyph : MicrophoneOnGlyph,
                    iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom,
                    "Segoe Fluent Icons", IconFontSizeDip, 400, DtCenter, color);

                if (layout.TextRect is { } microphoneTextRectDip)
                {
                    var textRect = OsdLayout.ToPixels(microphoneTextRectDip, dpi);
                    DrawCompositedTextOnGlass(
                        window, deviceContext, dpi,
                        muted ? "Microphone muted" : "Microphone unmuted",
                        textRect.Left, textRect.Top, textRect.Right, textRect.Bottom,
                        "Segoe UI Variable Text", 14, 400, DtLeft, color);
                }
                break;
        }
    }

    private static void DrawCompositedTextOnGlass(
        IntPtr window,
        IntPtr targetDc,
        uint dpi,
        string text,
        int left,
        int top,
        int right,
        int bottom,
        string fontFace,
        double fontSizeDip,
        int weight,
        uint horizontalAlignment,
        uint color)
    {
        var width = right - left;
        var height = bottom - top;
        if (targetDc == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var memoryDc = CreateCompatibleDC(targetDc);
        if (memoryDc == IntPtr.Zero)
        {
            return;
        }

        var bitmapInfo = new BitmapInfo
        {
            bmiHeader = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height, // DrawThemeTextEx compositing requires a top-down 32-bpp DIB.
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BiRgb,
            },
        };

        var bitmap = CreateDIBSection(
            targetDc, ref bitmapInfo, DibRgbColors, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(memoryDc);
            return;
        }

        var oldBitmap = SelectObject(memoryDc, bitmap);
        var font = CreateFont(
            -DipToPx(fontSizeDip, dpi),
            0, 0, 0, weight,
            false, false, false,
            1, 0, 0, 4, 0,
            fontFace);
        var oldFont = font != IntPtr.Zero ? SelectObject(memoryDc, font) : IntPtr.Zero;
        var theme = OpenThemeData(window, "CompositedWindow::Window");

        try
        {
            // CreateDIBSection memory isn't guaranteed to be initialized. Acrylic uses fully
            // transparent black; the accessibility/transparency-off fallback uses the opaque
            // WinUI solid fallback so SRCCOPY does not punch a transparent rectangle around text.
            var pixelCount = checked(width * height);
            var initialArgb = OsdTheme.SystemBackdropEnabled
                ? 0u
                : OsdTheme.GetFallbackSurfaceArgb();
            var initialPixels = new int[pixelCount];
            if (initialArgb != 0)
            {
                Array.Fill(initialPixels, unchecked((int)initialArgb));
            }
            Marshal.Copy(initialPixels, 0, bits, pixelCount);

            if (theme == IntPtr.Zero || font == IntPtr.Zero)
            {
                return;
            }

            var rect = new Rect
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
                    horizontalAlignment | DtVCenter | DtSingleLine,
                    ref rect,
                    ref options) >= 0)
            {
                BitBlt(targetDc, left, top, width, height, memoryDc, 0, 0, SrcCopy);
            }
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

    private static void DrawRoundRectOutline(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint color)
    {
        var pen = CreatePen(PsSolid, Math.Max(1, Scale(1, OsdHost.WindowDpi)), color);
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

    private static void DrawFilledCapsule(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        uint argb)
    {
        if (right <= left || bottom <= top || deviceContext == IntPtr.Zero)
        {
            return;
        }

        // The persistent top-down DIB is the important path. Draw the capsule ourselves so the
        // WinUI semantic brush alpha survives all the way to DWM instead of being flattened by
        // COLORREF/GDI. The old opaque track could never reproduce ControlStrongStrokeColorDefault.
        if (deviceContext == _backBufferDc && _backBufferBits != IntPtr.Zero)
        {
            DrawArgbCapsuleIntoBackBuffer(left, top, right, bottom, argb);
            return;
        }

        // Allocation-failure/WM_PRINTCLIENT fallback. GDI+ understands ARGB; if it is unavailable,
        // flatten the semantic color over the solid fallback surface rather than discarding alpha.
        if (!EnsureGdiPlus())
        {
            DrawFilledRoundRect(
                deviceContext,
                left,
                top,
                right,
                bottom,
                bottom - top,
                OsdTheme.CompositeArgbOverFallbackToColorRef(argb));
            return;
        }

        IntPtr graphics = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;
        try
        {
            if (GdipCreateFromHDC(deviceContext, out graphics) != 0 || graphics == IntPtr.Zero)
            {
                DrawFilledRoundRect(
                    deviceContext,
                    left,
                    top,
                    right,
                    bottom,
                    bottom - top,
                    OsdTheme.CompositeArgbOverFallbackToColorRef(argb));
                return;
            }

            GdipSetSmoothingMode(graphics, 4); // SmoothingModeAntiAlias
            if (GdipCreateSolidFill(argb, out brush) != 0 || brush == IntPtr.Zero)
            {
                DrawFilledRoundRect(
                    deviceContext,
                    left,
                    top,
                    right,
                    bottom,
                    bottom - top,
                    OsdTheme.CompositeArgbOverFallbackToColorRef(argb));
                return;
            }

            var height = bottom - top;
            var width = right - left;
            if (width <= height)
            {
                GdipFillEllipseI(graphics, brush, left, top, width, height);
                return;
            }

            GdipFillEllipseI(graphics, brush, left, top, height, height);
            GdipFillEllipseI(graphics, brush, right - height, top, height, height);
            GdipFillRectangleI(
                graphics,
                brush,
                left + (height / 2),
                top,
                width - height,
                height);
        }
        finally
        {
            if (brush != IntPtr.Zero)
            {
                GdipDeleteBrush(brush);
            }

            if (graphics != IntPtr.Zero)
            {
                GdipDeleteGraphics(graphics);
            }
        }
    }

    private static void DrawArgbCapsuleIntoBackBuffer(
        int left,
        int top,
        int right,
        int bottom,
        uint argb)
    {
        var clippedLeft = Math.Clamp(left - 1, 0, _backBufferWidth);
        var clippedTop = Math.Clamp(top - 1, 0, _backBufferHeight);
        var clippedRight = Math.Clamp(right + 1, 0, _backBufferWidth);
        var clippedBottom = Math.Clamp(bottom + 1, 0, _backBufferHeight);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return;
        }

        var pixelCount = checked(_backBufferWidth * _backBufferHeight);
        var pixels = new int[pixelCount];
        Marshal.Copy(_backBufferBits, pixels, 0, pixelCount);

        const int sampleGrid = 4;
        const int sampleCount = sampleGrid * sampleGrid;
        for (var y = clippedTop; y < clippedBottom; y++)
        {
            for (var x = clippedLeft; x < clippedRight; x++)
            {
                var insideSamples = 0;
                for (var sy = 0; sy < sampleGrid; sy++)
                {
                    for (var sx = 0; sx < sampleGrid; sx++)
                    {
                        var sampleX = x + ((sx + 0.5) / sampleGrid);
                        var sampleY = y + ((sy + 0.5) / sampleGrid);
                        if (PointInsideCapsule(sampleX, sampleY, left, top, right, bottom))
                        {
                            insideSamples++;
                        }
                    }
                }

                if (insideSamples == 0)
                {
                    continue;
                }

                var coverage = (insideSamples * 255 + (sampleCount / 2)) / sampleCount;
                var index = (y * _backBufferWidth) + x;
                pixels[index] = BlendPremultipliedArgb(
                    unchecked((uint)pixels[index]),
                    argb,
                    coverage);
            }
        }

        Marshal.Copy(pixels, 0, _backBufferBits, pixelCount);
    }

    private static bool PointInsideCapsule(
        double x,
        double y,
        int left,
        int top,
        int right,
        int bottom)
    {
        if (x < left || x >= right || y < top || y >= bottom)
        {
            return false;
        }

        var width = right - left;
        var height = bottom - top;
        if (width <= height)
        {
            var radiusX = width / 2.0;
            var radiusY = height / 2.0;
            var centerX = (left + right) / 2.0;
            var centerY = (top + bottom) / 2.0;
            var nx = (x - centerX) / radiusX;
            var ny = (y - centerY) / radiusY;
            return (nx * nx) + (ny * ny) <= 1.0;
        }

        var radius = height / 2.0;
        var centerYCapsule = (top + bottom) / 2.0;
        var leftCenterX = left + radius;
        var rightCenterX = right - radius;
        if (x >= leftCenterX && x <= rightCenterX)
        {
            return true;
        }

        var centerXCircle = x < leftCenterX ? leftCenterX : rightCenterX;
        var dx = x - centerXCircle;
        var dy = y - centerYCapsule;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static int BlendPremultipliedArgb(uint destination, uint source, int coverage)
    {
        var sourceAlpha = (int)((source >> 24) & 0xffu);
        sourceAlpha = (sourceAlpha * coverage + 127) / 255;
        if (sourceAlpha <= 0)
        {
            return unchecked((int)destination);
        }

        var inverseAlpha = 255 - sourceAlpha;
        var destinationAlpha = (int)((destination >> 24) & 0xffu);
        var destinationRed = (int)((destination >> 16) & 0xffu);
        var destinationGreen = (int)((destination >> 8) & 0xffu);
        var destinationBlue = (int)(destination & 0xffu);

        var sourceRed = (int)((source >> 16) & 0xffu);
        var sourceGreen = (int)((source >> 8) & 0xffu);
        var sourceBlue = (int)(source & 0xffu);

        var outAlpha = sourceAlpha + ((destinationAlpha * inverseAlpha + 127) / 255);
        var outRed = ((sourceRed * sourceAlpha + 127) / 255) +
            ((destinationRed * inverseAlpha + 127) / 255);
        var outGreen = ((sourceGreen * sourceAlpha + 127) / 255) +
            ((destinationGreen * inverseAlpha + 127) / 255);
        var outBlue = ((sourceBlue * sourceAlpha + 127) / 255) +
            ((destinationBlue * inverseAlpha + 127) / 255);

        return unchecked((int)(
            ((uint)Math.Clamp(outAlpha, 0, 255) << 24) |
            ((uint)Math.Clamp(outRed, 0, 255) << 16) |
            ((uint)Math.Clamp(outGreen, 0, 255) << 8) |
            (uint)Math.Clamp(outBlue, 0, 255)));
    }

    private static bool EnsureGdiPlus()
    {
        if (_gdiPlusStartupAttempted)
        {
            return _gdiPlusAvailable;
        }

        _gdiPlusStartupAttempted = true;
        var startupInput = new GdiplusStartupInput
        {
            GdiplusVersion = 1,
            DebugEventCallback = IntPtr.Zero,
            SuppressBackgroundThread = false,
            SuppressExternalCodecs = true,
        };

        _gdiPlusAvailable =
            GdiplusStartup(out _gdiPlusToken, ref startupInput, IntPtr.Zero) == 0;
        return _gdiPlusAvailable;
    }

    internal static void ShutdownGdiPlus()
    {
        if (_gdiPlusAvailable)
        {
            GdiplusShutdown(_gdiPlusToken);
        }

        _gdiPlusToken = UIntPtr.Zero;
        _gdiPlusAvailable = false;
        _gdiPlusStartupAttempted = false;
    }

    private static void DrawFilledRoundRect(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint color)
    {
        if (right <= left || bottom <= top)
        {
            return;
        }

        var brush = CreateSolidBrush(color);
        if (brush == IntPtr.Zero)
        {
            return;
        }

        var oldPen = SelectObject(deviceContext, GetStockObject(NullPen));
        var oldBrush = SelectObject(deviceContext, brush);
        try
        {
            RoundRect(deviceContext, left, top, right, bottom, radius, radius);
        }
        finally
        {
            SelectObject(deviceContext, oldBrush);
            SelectObject(deviceContext, oldPen);
            DeleteObject(brush);
        }
    }

}
