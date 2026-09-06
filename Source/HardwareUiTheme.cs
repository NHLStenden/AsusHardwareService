using System.Runtime.InteropServices;
using static AsusHardwareService.HardwareUiNative;

namespace AsusHardwareService;

internal enum IndicatorPosition
{
    BottomCenter = 1,
    TopLeft = 2,
    TopCenter = 3,
}

/// <summary>
/// Reads Windows appearance preferences and configures Acrylic, colors, and accessibility behavior.
/// </summary>
internal static class HardwareUiTheme
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

    internal static uint GetAccentColor()
    {
        if (_highContrast)
        {
            return GetSysColor(ColorHighlight);
        }

        // Use the same semantic accent shades as WinUI's AccentFillColorDefaultBrush:
        // dark theme -> SystemAccentColorLight2, light theme -> SystemAccentColorDark1.
        // AccentPalette order is Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, Extra.
        // Therefore the dark value is slot 1, not slot 2 (Light1). Light2 is the slightly
        // aqua/cyan-tinted Windows 11 fill visible in the native hardware flyout.
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
        // This is a shell preference rather than an app contract. Read it best-effort and keep
        // bottom-centre as the stable fallback if a Windows build does not expose the value.
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
        // Set the edge explicitly in both themes; high contrast returns ownership to the system.
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
            // DWMSBT_TRANSIENTWINDOW has no tint parameter and on current Windows 11 builds can
            // expose the bright Desktop Acrylic underpaint even with immersive dark mode enabled.
            // The Shell's dark flyouts add a #2C2C2C Acrylic tint, so use the accent-policy path
            // only for dark mode to reproduce that layer while keeping the same DWM blur.
            var noBackdrop = DwmsbtNone;
            DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));
            if (frameResult >= 0 && SetAcrylicAccentPolicy(window, enabled: true, gradientColor: DarkAcrylicGradientColor))
            {
                _systemBackdropEnabled = true;
                return;
            }

            // If the tint-capable path is ever unavailable, prefer WinUI's #2C2C2C fallback over
            // the visibly washed-out untinted transient backdrop.
            _systemBackdropEnabled = false;
            return;
        }

        // The transient backdrop is too opaque/flat for the native light hardware flyout on
        // current Windows 11 builds. Use the same tint-capable native Acrylic path as dark mode,
        // with the light Shell surface tint. Fall back to DWMSBT_TRANSIENTWINDOW if unavailable.
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
