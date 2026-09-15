using System.Runtime.InteropServices;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Osd;

internal enum IndicatorPosition
{
    BottomCenter = 1,
    TopLeft = 2,
    TopCenter = 3,
}

/// <summary>
/// Reads Windows appearance preferences and configures Acrylic, colors, and accessibility behavior.
/// </summary>
internal static class OsdTheme
{
    private static bool _isDarkTheme = true;
    private static bool _highContrast;
    private static bool _animationsEnabled = true;
    private static IndicatorPosition _indicatorPosition = IndicatorPosition.BottomCenter;
    private static bool _systemBackdropEnabled;

    internal static bool IsDarkTheme => _isDarkTheme;
    internal static bool HighContrast => _highContrast;
    internal static bool AnimationsEnabled => _animationsEnabled;
    internal static IndicatorPosition IndicatorPosition => _indicatorPosition;
    internal static bool SystemBackdropEnabled => _systemBackdropEnabled;

    internal static uint GetPrimaryTextColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorWindowText);
        }

        return _isDarkTheme ? Rgb(255, 255, 255) : Rgb(28, 28, 28);
    }

    internal static uint GetSecondaryTextColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorWindowText);
        }

        return _isDarkTheme ? Rgb(201, 201, 201) : Rgb(92, 92, 92);
    }

    internal static uint GetLevelTrackArgb()
    {
        if (_highContrast)
        {
            return ColorRefToOpaqueArgb(GetSysColor(ColorWindowText));
        }

        return _isDarkTheme ? 0x8BFFFFFFu : 0x72000000u;
    }

    internal static uint GetAccentArgb()
    {
        return ColorRefToOpaqueArgb(GetAccentColor());
    }

    internal static uint GetFallbackSurfaceArgb()
    {
        if (_highContrast)
        {
            return ColorRefToOpaqueArgb(GetSysColor(ColorWindow));
        }

        return _isDarkTheme ? 0xFF2C2C2Cu : 0xFFF9F9F9u;
    }

    internal static uint CompositeArgbOverFallbackToColorRef(uint argb)
    {
        var background = GetFallbackSurfaceArgb();
        var alpha = (argb >> 24) & 0xffu;
        var inverseAlpha = 255u - alpha;
        var red = (((argb >> 16) & 0xffu) * alpha + ((background >> 16) & 0xffu) * inverseAlpha + 127u) / 255u;
        var green = (((argb >> 8) & 0xffu) * alpha + ((background >> 8) & 0xffu) * inverseAlpha + 127u) / 255u;
        var blue = ((argb & 0xffu) * alpha + (background & 0xffu) * inverseAlpha + 127u) / 255u;
        return Rgb((byte)red, (byte)green, (byte)blue);
    }

    private static uint ColorRefToOpaqueArgb(uint color)
    {
        var red = color & 0xffu;
        var green = (color >> 8) & 0xffu;
        var blue = (color >> 16) & 0xffu;
        return 0xff000000u | (red << 16) | (green << 8) | blue;
    }

    internal static uint GetAccentColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorHighlight);
        }

        var accentShadeIndex = _isDarkTheme ? AccentPaletteLight2 : AccentPaletteDark1;
        if (TryReadAccentPaletteColor(accentShadeIndex, out var themedAccent))
        {
            return themedAccent;
        }

        if (DwmGetColorizationColor(out var argbColor, out _) != 0)
        {
            return GetSysColor(ColorHighlight);
        }

        var red = (byte)((argbColor >> 16) & 0xff);
        var green = (byte)((argbColor >> 8) & 0xff);
        var blue = (byte)(argbColor & 0xff);
        return Rgb(red, green, blue);
    }

    private static bool TryReadAccentPaletteColor(int shadeIndex, out uint color)
    {
        color = 0;
        var palette = new byte[32];
        uint dataSize = (uint)palette.Length;
        if (RegGetValueBytes(
                HkeyCurrentUser,
                AccentRegistryPath,
                AccentPaletteRegistryValue,
                RrfRtRegBinary,
                IntPtr.Zero,
                palette,
                ref dataSize) != ErrorSuccess ||
            shadeIndex < 0 ||
            shadeIndex >= (int)(dataSize / 4))
        {
            return false;
        }

        var offset = shadeIndex * 4;
        // AccentPalette entries expose the RGB components in byte order for this palette.
        color = Rgb(palette[offset], palette[offset + 1], palette[offset + 2]);
        return true;
    }

    internal static void RefreshSystemPreferences()
    {
        _highContrast = IsHighContrastEnabled();
        _isDarkTheme = !_highContrast && IsDarkSystemThemeEnabled();
        _animationsEnabled = AreClientAreaAnimationsEnabled();
        _indicatorPosition = ReadIndicatorPosition();
    }

    private static bool IsDarkSystemThemeEnabled()
    {
        return TryReadRegistryDword(
                   PersonalizeRegistryPath,
                   SystemUsesLightThemeRegistryValue,
                   out var lightTheme) &&
               lightTheme == 0;
    }

    private static IndicatorPosition ReadIndicatorPosition()
    {
        // This is a shell preference rather than an app contract.
        if (!TryReadRegistryDword(
                IndicatorPositionRegistryPath,
                IndicatorPositionRegistryValue,
                out var position))
        {
            return IndicatorPosition.BottomCenter;
        }

        return position switch
        {
            2 => IndicatorPosition.TopLeft,
            3 => IndicatorPosition.TopCenter,
            _ => IndicatorPosition.BottomCenter,
        };
    }

    private static bool TryReadRegistryDword(string subKey, string valueName, out uint value)
    {
        value = 0;
        uint dataSize = sizeof(uint);
        return RegGetValue(
                   HkeyCurrentUser,
                   subKey,
                   valueName,
                   RrfRtRegDword,
                   IntPtr.Zero,
                   ref value,
                   ref dataSize) == ErrorSuccess;
    }

    private static bool IsHighContrastEnabled()
    {
        var highContrast = new HighContrast
        {
            cbSize = (uint)Marshal.SizeOf<HighContrast>(),
        };
        return SystemParametersInfoHighContrast(
                   SpiGetHighContrast,
                   highContrast.cbSize,
                   ref highContrast,
                   0) &&
               (highContrast.dwFlags & HcfHighContrastOn) != 0;
    }

    private static bool AreClientAreaAnimationsEnabled()
    {
        var enabled = 1;
        if (!SystemParametersInfoInt(SpiGetClientAreaAnimation, 0, ref enabled, 0))
        {
            return true;
        }

        return enabled != 0;
    }

    internal static void ConfigureWindows11Appearance(IntPtr window)
    {
        var darkMode = _isDarkTheme ? 1 : 0;
        DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        var cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        // AccentPolicy can make DWM choose a brighter generic outline than the Shell flyout edge.
        var borderColor = _highContrast ? DwmColorDefault : DwmColorNone;
        DwmSetWindowAttribute(window, DwmwaBorderColor, ref borderColor, sizeof(int));

        // Clear the legacy accent policy first so theme changes cannot leave a stale dark tint.
        SetAcrylicAccentPolicy(window, enabled: false);

        var margins = _highContrast
            ? new Margins()
            : new Margins
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1,
            };
        var frameResult = DwmExtendFrameIntoClientArea(window, ref margins);

        if (_highContrast)
        {
            var noBackdrop = DwmsbtNone;
            DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));
            _systemBackdropEnabled = false;
            return;
        }

        if (_isDarkTheme)
        {
            var noBackdrop = DwmsbtNone;
            DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));
            if (frameResult >= 0 && SetAcrylicAccentPolicy(window, enabled: true, gradientColor: DarkAcrylicGradientColor))
            {
                _systemBackdropEnabled = true;
                return;
            }

            var darkFallbackBackdropType = DwmsbtTransientWindow;
            var darkFallbackBackdropResult = DwmSetWindowAttribute(
                window,
                DwmwaSystemBackdropType,
                ref darkFallbackBackdropType,
                sizeof(int));
            _systemBackdropEnabled = darkFallbackBackdropResult >= 0 && frameResult >= 0;
            return;
        }

        var noLightBackdrop = DwmsbtNone;
        DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noLightBackdrop, sizeof(int));
        if (frameResult >= 0 &&
            SetAcrylicAccentPolicy(window, enabled: true, gradientColor: LightAcrylicGradientColor))
        {
            _systemBackdropEnabled = true;
            return;
        }

        var backdropType = DwmsbtTransientWindow;
        var backdropResult = DwmSetWindowAttribute(
            window,
            DwmwaSystemBackdropType,
            ref backdropType,
            sizeof(int));
        _systemBackdropEnabled = backdropResult >= 0 && frameResult >= 0;
    }

    private static bool SetAcrylicAccentPolicy(IntPtr window, bool enabled, uint gradientColor = 0)
    {
        var policy = new AccentPolicy
        {
            AccentState = enabled ? AccentEnableAcrylicBlurBehind : AccentDisabled,
            AccentFlags = 0,
            GradientColor = enabled ? gradientColor : 0,
            AnimationId = 0,
        };

        var policyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttribData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = (nuint)Marshal.SizeOf<AccentPolicy>(),
            };
            return SetWindowCompositionAttribute(window, ref data);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

}
