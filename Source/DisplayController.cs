using Microsoft.Extensions.Options;

namespace AsusHardwareService;
/// <summary>
/// Applies display-related ASUS hardware settings.
/// </summary>
/// <remarks>
/// ASUS ACPI settings are applied directly by the service. Windows refresh-rate changes are
/// launched in the active interactive user session, because Session 0 cannot reliably enumerate
/// or modify the user's display devices.
/// </remarks>
public sealed class DisplayController
{
    private readonly AsusAcpi _acpi;
    private readonly ILogger<DisplayController> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    /// <summary>
    /// Initialises a new instance of the <see cref="DisplayController"/> class.
    /// </summary>
    /// <param name="acpi">The ASUS ACPI access wrapper.</param>
    /// <param name="logger">The logger used for diagnostic messages.</param>
    /// <param name="options">The live hardware options.</param>
    public DisplayController(
        AsusAcpi acpi,
        ILogger<DisplayController> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _acpi = acpi ?? throw new ArgumentNullException(nameof(acpi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Applies display settings that can safely run from the Windows service session.
    /// </summary>
    /// <remarks>
    /// This applies ASUS ACPI settings only. The refresh-rate command is intentionally not started
    /// here, because an interactive user session may not exist yet during service startup.
    /// </remarks>
    public void ApplyConfiguredServiceDisplaySettings()
    {
        var options = _options.CurrentValue;
        _logger.LogInformation(
            "Applying service display settings. LaptopScreenMode={LaptopScreenMode}, MiniLedMode={MiniLedMode}",
            options.LaptopScreenMode,
            options.MiniLedMode);

        ApplyConfiguredOverdriveMode();
        ApplyMiniLedMode(options.MiniLedMode);
    }
    /// <summary>
    /// Applies the configured laptop screen mode in the specified interactive user session.
    /// </summary>
    /// <param name="session">The active interactive user session.</param>
    /// <returns><see langword="true"/> when the display command was started; otherwise, <see langword="false"/>.</returns>
    public bool ApplyConfiguredLaptopScreenMode(SessionInfo session)
    {
        return ApplyLaptopScreenMode(_options.CurrentValue.LaptopScreenMode, session);
    }
    /// <summary>
    /// Applies the configured MiniLED setting only.
    /// </summary>
    public void ApplyConfiguredMiniLedMode()
    {
        ApplyMiniLedMode(_options.CurrentValue.MiniLedMode);
    }
    /// <summary>
    /// Applies the configured panel overdrive setting only.
    /// </summary>
    public void ApplyConfiguredOverdriveMode()
    {
        var mode = _options.CurrentValue.LaptopScreenMode;
        var overdrive = mode switch
        {
            LaptopScreenMode.Auto => PowerNative.IsOnAcPower() ? 1 : 0,
            LaptopScreenMode.Hz60 => 0,
            LaptopScreenMode.Hz240Overdrive => 1,
            _ => 0,
        };

        SetOverdrive(overdrive);
    }
    /// <summary>
    /// Applies the requested laptop panel refresh-rate and overdrive mode.
    /// </summary>
    /// <param name="mode">The laptop screen mode to apply.</param>
    /// <param name="session">The active interactive user session used for the refresh-rate command.</param>
    /// <returns><see langword="true"/> when the refresh-rate command was started; otherwise, <see langword="false"/>.</returns>
    public bool ApplyLaptopScreenMode(LaptopScreenMode mode, SessionInfo session)
    {
        return mode switch
        {
            LaptopScreenMode.Auto => ApplyAutoScreen(session),
            LaptopScreenMode.Hz60 => SetLaptopScreen(session, DisplayCommand.ScreenMode60Hz, overdrive: 0),
            LaptopScreenMode.Hz240Overdrive => SetLaptopScreen(session, DisplayCommand.ScreenMode240HzOverdrive, overdrive: 1),
            _ => UnknownLaptopScreenMode(mode),
        };
    }
    /// <summary>
    /// Applies the requested MiniLED backlight zone mode.
    /// </summary>
    /// <param name="mode">The MiniLED zone mode to apply.</param>
    public void ApplyMiniLedMode(MiniLedMode mode)
    {
        SetMiniLed(mode);
    }

    private bool ApplyAutoScreen(SessionInfo session)
    {
        var started = RunSelfInUserSession(
            session,
            $"{DisplayCommand.CommandName} {DisplayCommand.ScreenCommandName} {DisplayCommand.ScreenModeAuto}");
        SetOverdrive(PowerNative.IsOnAcPower() ? 1 : 0);
        return started;
    }

    private bool SetLaptopScreen(SessionInfo session, string screenMode, int overdrive)
    {
        var started = RunSelfInUserSession(
            session,
            $"{DisplayCommand.CommandName} {DisplayCommand.ScreenCommandName} {screenMode}");

        SetOverdrive(overdrive);
        return started;
    }
    private bool UnknownLaptopScreenMode(LaptopScreenMode mode)
    {
        _logger.LogWarning("Unknown laptop screen mode: {Mode}", mode);
        return false;
    }
    private void SetOverdrive(int overdrive)
    {
        try
        {
            var current = TryGetAdjustedDeviceValue(AsusAcpi.ScreenOverdrive, "ScreenOverdrive");
            if (current == overdrive)
            {
                _logger.LogInformation("Screen overdrive already has requested value {Overdrive}.", overdrive);
                return;
            }
            _acpi.SetDeviceValue(AsusAcpi.ScreenOverdrive, overdrive, "ScreenOverdrive");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set screen overdrive to {Overdrive}.", overdrive);
        }
    }
    private int? TryGetAdjustedDeviceValue(uint deviceId, string logName)
    {
        try
        {
            return _acpi.GetDeviceValue(deviceId, logName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read {LogName} before writing.", logName);
            return null;
        }
    }
    private void SetMiniLed(MiniLedMode mode)
    {
        var threeStateValue = ToMiniLed2Value(mode);
        if (TryWriteMiniLedEndpoint(AsusAcpi.ScreenMiniled2, threeStateValue, "MiniLED2", mode, sleepAfterWrite: true))
        {
            return;
        }

        if (mode == MiniLedMode.MultiZoneStrong)
        {
            _logger.LogWarning(
                "MiniLED2 did not accept MultiZoneStrong. Falling back to MiniLED1 MultiZone, because MiniLED1 has no strong mode.");
        }
        var twoStateValue = ToMiniLed1Value(mode);
        if (TryWriteMiniLedEndpoint(AsusAcpi.ScreenMiniled1, twoStateValue, "MiniLED1", mode, sleepAfterWrite: false))
        {
            return;
        }

        _logger.LogWarning("No supported MiniLED ACPI endpoint accepted mode {Mode}.", mode);
    }
    private static int ToMiniLed1Value(MiniLedMode mode)
    {
        return mode switch
        {
            MiniLedMode.OneZone => 0,
            MiniLedMode.MultiZone => 1,
            MiniLedMode.MultiZoneStrong => 1,
            _ => 1,
        };
    }
    private static int ToMiniLed2Value(MiniLedMode mode)
    {
        return mode switch
        {
            MiniLedMode.OneZone => 2,
            MiniLedMode.MultiZone => 0,
            MiniLedMode.MultiZoneStrong => 1,
            _ => 0,
        };
    }
    private bool TryWriteMiniLedEndpoint(
        uint endpoint,
        int endpointValue,
        string name,
        MiniLedMode requestedMode,
        bool sleepAfterWrite)
    {
        try
        {
            var result = _acpi.SetDeviceValue(endpoint, endpointValue, name);
            if (result != 1)
            {
                _logger.LogDebug(
                    "{Name} rejected {RequestedMode} using ACPI value {EndpointValue}. Result={Result}.",
                    name,
                    requestedMode,
                    endpointValue,
                    result);
                return false;
            }

            if (sleepAfterWrite)
            {
                Thread.Sleep(100);
            }

            _logger.LogInformation(
                "Applied {RequestedMode} through {Name} using ACPI value {EndpointValue}.",
                requestedMode,
                name,
                endpointValue);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to apply {RequestedMode} through {Name} using ACPI value {EndpointValue}.",
                requestedMode,
                name,
                endpointValue);

            return false;
        }
    }
    private bool RunSelfInUserSession(SessionInfo session, string arguments)
    {
        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogError("Could not resolve current executable path. ResolvedPath={ExecutablePath}", executablePath);
            return false;
        }
        _logger.LogInformation(
            "Starting display command in session {SessionId}: {Path} {Arguments}",
            session.SessionId,
            executablePath,
            arguments);

        var started = SessionProcessLauncher.TryStartProcessInSession(
            session.SessionId,
            executablePath,
            arguments,
            _logger);
        if (!started)
        {
            _logger.LogWarning("Failed to start display command in session {SessionId}.", session.SessionId);
        }

        return started;
    }

    private static string? ResolveCurrentExecutablePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }
}
/// <summary>
/// Laptop panel refresh-rate and overdrive presets.
/// </summary>
public enum LaptopScreenMode
{
    /// <summary>
    /// Uses 240 Hz with overdrive on AC power, and 60 Hz with overdrive off on battery power.
    /// </summary>
    Auto,

    /// <summary>
    /// Uses 60 Hz with panel overdrive disabled.
    /// </summary>
    Hz60,

    /// <summary>
    /// Uses 240 Hz with panel overdrive enabled.
    /// </summary>
    Hz240Overdrive,
}
/// <summary>
/// MiniLED backlight zone modes.
/// </summary>
/// <remarks>
/// ASUS exposes two different ACPI endpoints for MiniLED control. Endpoint <c>0x0005001E</c>
/// uses <c>0</c> for one-zone and <c>1</c> for multi-zone. Endpoint <c>0x0005002E</c>
/// uses <c>2</c> for one-zone, <c>0</c> for multi-zone, and <c>1</c> for strong multi-zone.
/// </remarks>
public enum MiniLedMode
{
    /// <summary>
    /// Disables local dimming by treating the MiniLED panel as one uniform lighting zone.
    /// </summary>
    OneZone,
    /// <summary>
    /// Enables normal local dimming with multiple lighting zones.
    /// </summary>
    MultiZone,

    /// <summary>
    /// Enables the stronger multi-zone local dimming mode on devices that support it.
    /// </summary>
    MultiZoneStrong,
}
