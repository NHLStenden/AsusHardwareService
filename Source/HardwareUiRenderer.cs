using System.Runtime.InteropServices;
using static AsusHardwareService.HardwareUiNative;

namespace AsusHardwareService;

/// <summary>
/// Paints the flyout with Win32 GDI/GDI+ and manages the flicker-free back buffer.
/// </summary>
internal static class HardwareUiRenderer
{
    // Persistent back buffer: the compositor only sees complete frames.
    private static IntPtr _backBufferDc;
    private static IntPtr _backBufferBitmap;
    private static IntPtr _backBufferOldBitmap;
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
                // Safe fallback: preserve the old direct-paint path if allocation ever fails.
                DrawStatus(window, paintDc);
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

            // GDI dark text on an extended glass frame is interpreted as transparent pixels.
            // Repaint the light-theme foreground with DrawThemeTextEx(DTT_COMPOSITED), which
            // writes the alpha channel DWM expects for dark glyphs/text on glass.
            if (!HardwareUiTheme.IsDarkTheme && !HardwareUiTheme.HighContrast)
            {
                DrawLightForegroundComposited(window, paintDc);
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

        var bitmap = CreateCompatibleBitmap(targetDc, width, height);
        if (bitmap == IntPtr.Zero)
        {
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
        _backBufferWidth = 0;
        _backBufferHeight = 0;
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
            dpi = HardwareUiHost.WindowDpi == 0 ? 96u : HardwareUiHost.WindowDpi;
        }

        FillStatusBackground(deviceContext, ref clientRect);
        SetBkMode(deviceContext, Transparent);

        switch (HardwareUiHost.Notification.Kind)
        {
            case HardwareUiNotificationKind.KeyboardBacklight:
                DrawKeyboardBacklightStatus(deviceContext, dpi, HardwareUiHost.Notification.Value);
                break;

            case HardwareUiNotificationKind.DisplayBrightness:
                DrawDisplayBrightnessStatus(deviceContext, dpi, HardwareUiHost.Notification.Value);
                break;

            case HardwareUiNotificationKind.PerformanceGpuMode:
                DrawPerformanceGpuStatus(deviceContext, dpi, HardwareUiHost.Notification.Value);
                break;

            case HardwareUiNotificationKind.Microphone:
            default:
                DrawMicrophoneStatus(deviceContext, dpi, HardwareUiHost.Notification.Value != 0);
                break;
        }
    }

    private static void FillStatusBackground(IntPtr deviceContext, ref Rect clientRect)
    {
        // A black GDI fill has zeroed pixel data on an extended DWM frame, exposing the Desktop
        // Acrylic backdrop instead of covering it with the opaque fallback colour.
        if (HardwareUiTheme.SystemBackdropEnabled)
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
        var backgroundColor = HardwareUiTheme.HighContrast
            ? GetSysColor(ColorWindow)
            : HardwareUiTheme.IsDarkTheme
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
        var foreground = HardwareUiTheme.GetPrimaryTextColor();
        DrawMicrophoneIcon(deviceContext, dpi, foreground, muted);
        DrawPrimaryText(
            deviceContext,
            dpi,
            muted ? "Microphone muted" : "Microphone unmuted",
            Scale(48, dpi),
            0,
            HardwareUiHost.WindowWidth - Scale(16, dpi),
            HardwareUiHost.WindowHeight);
    }

    private static void DrawMicrophoneIcon(
        IntPtr deviceContext,
        uint dpi,
        uint color,
        bool muted)
    {
        DrawFluentIcon(
            deviceContext,
            dpi,
            muted ? MicrophoneOffGlyph : MicrophoneOnGlyph,
            color);
    }

    private static void DrawKeyboardBacklightStatus(IntPtr deviceContext, uint dpi, int level)
    {
        var foreground = HardwareUiTheme.GetPrimaryTextColor();
        DrawKeyboardIcon(deviceContext, dpi, foreground);
        DrawLevelTrack(
            deviceContext,
            dpi,
            Math.Clamp(level, 0, 3) / 3.0,
            leftPaddingDip: 42,
            rightPaddingDip: 40);
        DrawLevelValue(deviceContext, dpi, Math.Clamp(level, 0, 3).ToString());
    }

    private static void DrawKeyboardIcon(IntPtr deviceContext, uint dpi, uint color)
    {
        // The compact 192-DIP level template has a 40-DIP leading slot. Moving the window edges
        // inward without moving the glyph on screen requires the icon box to be 4..36 rather
        // than the 8..40 box used by the 48-DIP leading-slot templates.
        DrawFluentIcon(deviceContext, dpi, KeyboardGlyph, color, 2, 34);
    }

    private static void DrawDisplayBrightnessStatus(IntPtr deviceContext, uint dpi, int brightness)
    {
        var foreground = HardwareUiTheme.GetPrimaryTextColor();
        DrawSunIcon(deviceContext, dpi, foreground);
        DrawLevelTrack(
            deviceContext,
            dpi,
            Math.Clamp(brightness, 0, 100) / 100.0,
            leftPaddingDip: 48,
            rightPaddingDip: 16);
    }

    private static void DrawSunIcon(IntPtr deviceContext, uint dpi, uint color)
    {
        DrawFluentIcon(deviceContext, dpi, BrightnessGlyph, color);
    }

    private static void DrawPerformanceGpuStatus(IntPtr deviceContext, uint dpi, int modeValue)
    {
        var foreground = HardwareUiTheme.GetPrimaryTextColor();
        var silent = (modeValue & 1) != 0;
        DrawPerformanceIcon(deviceContext, dpi, foreground, silent);

        var performanceMode = silent ? "Silent" : "Performance";
        var gpuMode = (modeValue & 2) != 0 ? "Eco" : "Standard";
        DrawPrimaryText(
            deviceContext,
            dpi,
            $"{performanceMode} · {gpuMode}",
            Scale(48, dpi),
            0,
            HardwareUiHost.WindowWidth - Scale(16, dpi),
            HardwareUiHost.WindowHeight);
    }

    private static void DrawPerformanceIcon(
        IntPtr deviceContext,
        uint dpi,
        uint color,
        bool silent)
    {
        DrawFluentIcon(
            deviceContext,
            dpi,
            silent ? SpeedMediumGlyph : SpeedHighGlyph,
            color);
    }

    private static void DrawLevelTrack(
        IntPtr deviceContext,
        uint dpi,
        double progress,
        int leftPaddingDip,
        int rightPaddingDip)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var trackColor = HardwareUiTheme.HighContrast
            ? GetSysColor(ColorWindowText)
            : HardwareUiTheme.IsDarkTheme
                ? Rgb(160, 160, 160)
                : Rgb(124, 124, 124);
        var accentColor = HardwareUiTheme.GetAccentColor();

        // Brightness uses the 48-DIP leading slot. The compact level+value template is 192 DIPs
        // wide and uses 40 + 112 + 40 DIPs; keeping these as logical values makes the relationship
        // survive arbitrary per-monitor scaling instead of tuning physical pixels for one DPI.
        var trackLeft = Scale(leftPaddingDip, dpi);
        var trackTop = Scale(20, dpi);
        var trackRight = HardwareUiHost.WindowWidth - Scale(rightPaddingDip, dpi);
        var trackBottom = Scale(24, dpi);
        DrawFilledCapsule(
            deviceContext,
            trackLeft,
            trackTop,
            trackRight,
            trackBottom,
            trackColor);

        var trackWidth = trackRight - trackLeft;
        var progressRight = trackLeft + (int)Math.Round(trackWidth * progress);
        if (progress > 0.0)
        {
            DrawFilledCapsule(
                deviceContext,
                trackLeft,
                trackTop,
                progressRight,
                trackBottom,
                accentColor);
        }
    }

    private static void DrawLevelValue(IntPtr deviceContext, uint dpi, string value)
    {
        var opticalOffset = ScaleHalfDip(4, dpi); // 2 DIPs; 3 px at 125%.
        DrawTextCore(
            deviceContext,
            dpi,
            value,
            HardwareUiHost.WindowWidth - Scale(40, dpi),
            -opticalOffset,
            HardwareUiHost.WindowWidth,
            HardwareUiHost.WindowHeight - Scale(2, dpi) - opticalOffset,
            400,
            14,
            DtCenter);
    }

    private static void DrawPrimaryText(
        IntPtr deviceContext,
        uint dpi,
        string text,
        int left,
        int top,
        int right,
        int bottom)
    {
        DrawTextCore(deviceContext, dpi, text, left, top, right, bottom, 400, 14);
    }

    private static void DrawFluentIcon(
        IntPtr deviceContext,
        uint dpi,
        string glyph,
        uint color,
        int leftDip = 8,
        int rightDip = 40)
    {
        if (!HardwareUiTheme.IsDarkTheme && !HardwareUiTheme.HighContrast)
        {
            return;
        }
        var font = CreateFont(
            -Scale(14, dpi),
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
            4, // ANTIALIASED_QUALITY: grayscale AA is stable on a DWM-composited/transparent client.
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
            var iconRect = new Rect
            {
                Left = Scale(leftDip, dpi),
                Top = 0,
                Right = Scale(rightDip, dpi),
                Bottom = HardwareUiHost.WindowHeight - Scale(1, dpi),
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
        if (!HardwareUiTheme.IsDarkTheme && !HardwareUiTheme.HighContrast)
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
            SetTextColor(deviceContext, HardwareUiTheme.GetPrimaryTextColor());
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

    private static void DrawLightForegroundComposited(IntPtr window, IntPtr deviceContext)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = HardwareUiHost.WindowDpi == 0 ? 96u : HardwareUiHost.WindowDpi;
        }

        var color = HardwareUiTheme.GetPrimaryTextColor();
        switch (HardwareUiHost.Notification.Kind)
        {
            case HardwareUiNotificationKind.KeyboardBacklight:
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, KeyboardGlyph,
                    Scale(2, dpi), 0, Scale(34, dpi), HardwareUiHost.WindowHeight - Scale(1, dpi),
                    "Segoe Fluent Icons", 14, 400, DtCenter, color);

                var opticalOffset = ScaleHalfDip(4, dpi);
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, Math.Clamp(HardwareUiHost.Notification.Value, 0, 3).ToString(),
                    HardwareUiHost.WindowWidth - Scale(40, dpi), -opticalOffset,
                    HardwareUiHost.WindowWidth, HardwareUiHost.WindowHeight - Scale(2, dpi) - opticalOffset,
                    "Segoe UI Variable Text", 14, 400, DtCenter, color);
                break;

            case HardwareUiNotificationKind.DisplayBrightness:
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, BrightnessGlyph,
                    Scale(8, dpi), 0, Scale(40, dpi), HardwareUiHost.WindowHeight - Scale(1, dpi),
                    "Segoe Fluent Icons", 14, 400, DtCenter, color);
                break;

            case HardwareUiNotificationKind.PerformanceGpuMode:
                var silent = (HardwareUiHost.Notification.Value & 1) != 0;
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, silent ? SpeedMediumGlyph : SpeedHighGlyph,
                    Scale(8, dpi), 0, Scale(40, dpi), HardwareUiHost.WindowHeight - Scale(1, dpi),
                    "Segoe Fluent Icons", 14, 400, DtCenter, color);

                var performanceMode = silent ? "Silent" : "Performance";
                var gpuMode = (HardwareUiHost.Notification.Value & 2) != 0 ? "Eco" : "Standard";
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, $"{performanceMode} · {gpuMode}",
                    Scale(48, dpi), 0, HardwareUiHost.WindowWidth - Scale(16, dpi), HardwareUiHost.WindowHeight,
                    "Segoe UI Variable Text", 14, 400, DtLeft, color);
                break;

            case HardwareUiNotificationKind.Microphone:
            default:
                var muted = HardwareUiHost.Notification.Value != 0;
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi, muted ? MicrophoneOffGlyph : MicrophoneOnGlyph,
                    Scale(8, dpi), 0, Scale(40, dpi), HardwareUiHost.WindowHeight - Scale(1, dpi),
                    "Segoe Fluent Icons", 14, 400, DtCenter, color);
                DrawCompositedTextOnGlass(
                    window, deviceContext, dpi,
                    muted ? "Microphone muted" : "Microphone unmuted",
                    Scale(48, dpi), 0, HardwareUiHost.WindowWidth - Scale(16, dpi), HardwareUiHost.WindowHeight,
                    "Segoe UI Variable Text", 14, 400, DtLeft, color);
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
        int fontSizeDip,
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
            -Scale(fontSizeDip, dpi),
            0, 0, 0, weight,
            false, false, false,
            1, 0, 0, 4, 0,
            fontFace);
        var oldFont = font != IntPtr.Zero ? SelectObject(memoryDc, font) : IntPtr.Zero;
        var theme = OpenThemeData(window, "CompositedWindow::Window");

        try
        {
            // CreateDIBSection memory isn't guaranteed to be initialized. Zero means fully
            // transparent black on the extended DWM frame.
            Marshal.Copy(new byte[checked(width * height * 4)], 0, bits, checked(width * height * 4));

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
        var pen = CreatePen(PsSolid, Math.Max(1, Scale(1, HardwareUiHost.WindowDpi)), color);
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
        uint color)
    {
        if (right <= left || bottom <= top || deviceContext == IntPtr.Zero)
        {
            return;
        }

        // GDI RoundRect is hard-edged at these sizes. GDI+ remains a very small native-only
        // addition, but it is initialized once and draws into the off-screen buffer, so there is
        // no partial-frame flash while brightness/backlight notifications arrive rapidly.
        if (!EnsureGdiPlus())
        {
            DrawFilledRoundRect(
                deviceContext, left, top, right, bottom, bottom - top, color);
            return;
        }

        IntPtr graphics = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;
        try
        {
            if (GdipCreateFromHDC(deviceContext, out graphics) != 0 || graphics == IntPtr.Zero)
            {
                DrawFilledRoundRect(
                    deviceContext, left, top, right, bottom, bottom - top, color);
                return;
            }

            GdipSetSmoothingMode(graphics, 4); // SmoothingModeAntiAlias
            if (GdipCreateSolidFill(ColorRefToArgb(color), out brush) != 0 || brush == IntPtr.Zero)
            {
                DrawFilledRoundRect(
                    deviceContext, left, top, right, bottom, bottom - top, color);
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

    private static uint ColorRefToArgb(uint color)
    {
        var red = color & 0xffu;
        var green = (color >> 8) & 0xffu;
        var blue = (color >> 16) & 0xffu;
        return 0xff000000u | (red << 16) | (green << 8) | blue;
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
