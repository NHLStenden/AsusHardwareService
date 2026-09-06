using System.Runtime.InteropServices;

namespace AsusHardwareService;

/// <summary>
/// Contains the Win32 contract: constants, DIP helpers, native structures, and P/Invoke declarations.
/// </summary>
internal static class HardwareUiNative
{
    internal const string WindowClassName = "AsusHardwareService.HardwareUiWindow";
    internal const string InstanceMutexName = @"Local\AsusHardwareService.HardwareUi";
    internal const string ServiceName = "ASUS Hardware Service";
    internal const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    internal const string SystemUsesLightThemeRegistryValue = "SystemUsesLightTheme";
    internal const string IndicatorPositionRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\ConfirmatorPosition";
    internal const string IndicatorPositionRegistryValue = "PositionIndex";
    internal const string AccentRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    internal const string AccentPaletteRegistryValue = "AccentPalette";
    internal const uint ErrorAlreadyExists = 183;
    internal const int ErrorSuccess = 0;

    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmSysColorChange = 0x0015;
    internal const uint WmEraseBackground = 0x0014;
    internal const uint WmPaint = 0x000F;
    internal const uint WmTimer = 0x0113;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmPrintClient = 0x0318;
    internal const uint WmDwmCompositionChanged = 0x031E;
    internal const uint WmApp = 0x8000;
    internal const uint WmMicStatusChanged = WmApp + 0x31;
    internal const uint WmKeyboardBacklightChanged = WmApp + 0x32;
    internal const uint WmDisplayBrightnessChanged = WmApp + 0x33;
    internal const uint WmPerformanceGpuChanged = WmApp + 0x34;
    internal const uint WmAnimationFrame = WmApp + 0x35;

    internal const int MaNoActivate = 3;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;

    internal const uint WsPopup = 0x80000000;
    internal const uint WsExTopmost = 0x00000008;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExNoActivate = 0x08000000;

    internal const uint MonitorDefaultToPrimary = 0x00000001;
    internal const uint MonitorDefaultToNearest = 0x00000002;

    internal const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaSystemBackdropType = 38;
    internal const int DwmwcpRound = 2;
    internal const int DwmsbtNone = 1;
    internal const int DwmsbtTransientWindow = 3;
    internal const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    internal const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    internal const int WcaAccentPolicy = 19;
    internal const int AccentDisabled = 0;
    internal const int AccentEnableAcrylicBlurBehind = 4;
    // Accent-policy colours are AABBGGRR. The values below are the pixel-matched
    // dark/light material tints used by the current Windows 11-style indicator.
    internal const uint DarkAcrylicGradientColor = 0xD2303032;
    internal const uint LightAcrylicGradientColor = 0xDCF3F3F0;

    internal const uint SpiGetHighContrast = 0x0042;
    internal const uint SpiGetClientAreaAnimation = 0x1042;
    internal const uint HcfHighContrastOn = 0x00000001;

    internal const int ColorWindow = 5;
    internal const int ColorWindowText = 8;
    internal const int ColorHighlight = 13;

    internal const uint RrfRtRegBinary = 0x00000008;
    internal const uint RrfRtRegDword = 0x00000010;
    internal const uint ScManagerConnect = 0x0001;
    internal const uint ServiceQueryStatus = 0x0004;
    internal const int ScStatusProcessInfo = 0;
    internal const uint ServiceStopped = 0x00000001;
    internal const uint ServiceStopPending = 0x00000003;
    internal const int ErrorServiceDoesNotExist = 1060;
    internal static readonly UIntPtr HkeyCurrentUser = new(0x80000001u);

    internal const int Transparent = 1;
    internal const uint DtLeft = 0x00000000;
    internal const uint DtCenter = 0x00000001;
    internal const uint DtVCenter = 0x00000004;
    internal const uint DtSingleLine = 0x00000020;
    internal const int PsSolid = 0;
    internal const int NullBrush = 5;
    internal const int NullPen = 8;
    internal const uint SrcCopy = 0x00CC0020;
    internal const uint DttTextColor = 0x00000001;
    internal const uint DttComposited = 0x00002000;
    internal const uint BiRgb = 0;
    internal const uint DibRgbColors = 0;

    // HKCU\...\Explorer\Accent\AccentPalette is eight 4-byte entries:
    // Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, Extra.
    internal const int AccentPaletteLight2 = 1;
    internal const int AccentPaletteDark1 = 4;

    // WinUI's ControlFastAnimationDuration resource is 167 ms. Use it as the actual clock for
    // both directions rather than as a timeout around an independently-timed DWM transition.
    internal const uint ControlFastAnimationDurationMilliseconds = 167;
    internal const int EntranceTranslationDip = 20;
    internal const int HardwareIndicatorHeightDip = 48;
    internal const int HardwareIndicatorEdgeMarginDip = 12;
    internal const uint HideDelayMilliseconds = 2000;
    // Graceful service/session changes send WM_CLOSE immediately. This low-frequency Win32
    // watchdog is only a fallback for abrupt service termination or a missed session transition.
    internal const uint ServiceWatchIntervalMilliseconds = 5000;

    // Windows 11 ships these glyphs in Segoe Fluent Icons. Keep the font glyph optically
    // centered inside the 32-DIP leading icon slot used by this compact indicator.
    internal const string BrightnessGlyph = "\uE706";
    internal const string KeyboardGlyph = "\uE765";
    internal const string MicrophoneOffGlyph = "\uEC54";
    internal const string MicrophoneOnGlyph = "\uE720";
    internal const string SpeedMediumGlyph = "\uEC49";
    internal const string SpeedHighGlyph = "\uEC4A";

    internal static readonly IntPtr HwndTopmost = new(-1);
    internal static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    internal static readonly UIntPtr HideTimerId = (UIntPtr)1u;
    internal static readonly UIntPtr ServiceWatchTimerId = (UIntPtr)3u;

    internal static int GetLogicalWindowWidth(HardwareUiNotificationKind kind)
    {
        return kind switch
        {
            HardwareUiNotificationKind.KeyboardBacklight => 192,
            HardwareUiNotificationKind.DisplayBrightness => 176,
            HardwareUiNotificationKind.Microphone => 224,
            HardwareUiNotificationKind.PerformanceGpuMode => 236,
            _ => 200,
        };
    }

    internal static int Scale(int value, uint dpi)
    {
        return (int)((value * dpi + 48) / 96);
    }

    internal static int ScaleHalfDip(int halfDipUnits, uint dpi)
    {
        // halfDipUnits is expressed in 0.5-DIP units. Keep optical nudges DPI-relative rather
        // than baking in physical pixels (3 means 1.5 DIP).
        return (int)(((long)halfDipUnits * dpi + 96) / 192);
    }

    internal static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateMutex(IntPtr mutexAttributes, bool initialOwner, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    internal static extern void Sleep(uint milliseconds);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        ref ServiceStatusProcess status,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll")]
    internal static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegGetValue(
        UIntPtr hKey,
        string? subKey,
        string? value,
        uint flags,
        IntPtr valueType,
        ref uint data,
        ref uint dataSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW", ExactSpelling = true)]
    internal static extern int RegGetValueBytes(
        UIntPtr hKey,
        string? subKey,
        string? value,
        uint flags,
        IntPtr valueType,
        [Out] byte[] data,
        ref uint dataSize);

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Message message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        IntPtr window,
        IntPtr windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    internal static extern bool UpdateWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    internal static extern UIntPtr SetTimer(IntPtr window, UIntPtr timerId, uint milliseconds, IntPtr timerProcedure);

    [DllImport("user32.dll")]
    internal static extern bool KillTimer(IntPtr window, UIntPtr timerId);

    [DllImport("user32.dll")]
    internal static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paintStruct);

    [DllImport("user32.dll")]
    internal static extern bool EndPaint(IntPtr window, ref PaintStruct paintStruct);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern int FillRect(IntPtr deviceContext, ref Rect rect, IntPtr brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(IntPtr deviceContext, string text, int textLength, ref Rect rect, uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    internal static extern bool SystemParametersInfoInt(
        uint action,
        uint parameter,
        ref int value,
        uint updateFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    internal static extern bool SystemParametersInfoHighContrast(
        uint action,
        uint parameter,
        ref HighContrast highContrast,
        uint updateFlags);

    [DllImport("user32.dll")]
    internal static extern uint GetSysColor(int index);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreatePen(int penStyle, int width, uint color);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(
        IntPtr destinationDc,
        int x,
        int y,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr GetStockObject(int objectType);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll")]
    internal static extern bool RoundRect(
        IntPtr deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int width,
        int height);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        bool italic,
        bool underline,
        bool strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenThemeData(IntPtr window, string classList);

    [DllImport("uxtheme.dll")]
    internal static extern int CloseThemeData(IntPtr theme);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawThemeTextEx(
        IntPtr theme,
        IntPtr deviceContext,
        int partId,
        int stateId,
        string text,
        int textLength,
        uint textFlags,
        ref Rect rect,
        ref DttOpts options);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowCompositionAttribute(
        IntPtr window,
        ref WindowCompositionAttribData data);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdiplusStartup(
        out UIntPtr token,
        ref GdiplusStartupInput input,
        IntPtr output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern void GdiplusShutdown(UIntPtr token);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipCreateFromHDC(IntPtr deviceContext, out IntPtr graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipDeleteGraphics(IntPtr graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipSetSmoothingMode(IntPtr graphics, int smoothingMode);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipCreateSolidFill(uint color, out IntPtr brush);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipFillEllipseI(
        IntPtr graphics, IntPtr brush, int x, int y, int width, int height);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    internal static extern int GdipFillRectangleI(
        IntPtr graphics, IntPtr brush, int x, int y, int width, int height);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetColorizationColor(out uint colorizationColor, out bool opaqueBlend);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WindowProcedureDelegate(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);


    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RgbQuad
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public RgbQuad bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DttOpts
    {
        public uint dwSize;
        public uint dwFlags;
        public uint crText;
        public uint crBorder;
        public uint crShadow;
        public int iTextShadowType;
        public Point ptShadowOffset;
        public int iBorderSize;
        public int iFontPropId;
        public int iColorPropId;
        public int iStateId;
        [MarshalAs(UnmanagedType.Bool)] public bool fApplyOverlay;
        public int iGlowSize;
        public IntPtr pfnDrawTextCallback;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        [MarshalAs(UnmanagedType.Bool)] public bool SuppressBackgroundThread;
        [MarshalAs(UnmanagedType.Bool)] public bool SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatusProcess
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public WindowProcedureDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point point;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttribData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct HighContrast
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStruct
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fErase;
        public Rect rcPaint;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIncUpdate;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
        public int reserved8;
    }
}
