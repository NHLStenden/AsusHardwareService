using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Configuration;
using AsusHardwareService.Windows.Display;
using AsusHardwareService.Windows.Power;
using AsusHardwareService.Windows.Processes;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Asus.Display;

/// <summary>
/// Applies ASUS panel overdrive/MiniLED firmware state and delegates refresh-rate changes to the interactive session.
/// </summary>
internal sealed class LaptopDisplayController
{
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly AsusAcpiCapabilities _capabilities;
    private readonly SessionProcessLauncher _processLauncher;
    private readonly ILogger<LaptopDisplayController> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="LaptopDisplayController"/> class.</summary>
    public LaptopDisplayController(
        AsusAcpiClientFactory acpiFactory,
        AsusAcpiCapabilities capabilities,
        SessionProcessLauncher processLauncher,
        ILogger<LaptopDisplayController> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Applies MiniLED and panel-overdrive settings that are safe to change from Session 0.</summary>
    public void ApplyConfiguredServiceSettings()
    {
        var options = _options.CurrentValue;
        _logger.LogInformation(
            "Applying service display settings. LaptopDisplayMode={LaptopDisplayMode}, MiniLedMode={MiniLedMode}.",
            options.LaptopDisplayMode,
            options.MiniLedMode);

        ApplyOverdrive(options.LaptopDisplayMode);
        ApplyMiniLedMode(options.MiniLedMode);
    }

    /// <summary>Starts the configured refresh-rate helper in the active user session.</summary>
    public bool ApplyConfiguredUserSessionSettings(InteractiveSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ApplyLaptopDisplayMode(_options.CurrentValue.LaptopDisplayMode, session);
    }

    /// <summary>Applies one laptop-panel preset to the active user session.</summary>
    /// <param name="mode">The refresh-rate and overdrive preset to apply.</param>
    /// <param name="session">The user session that owns the laptop display.</param>
    /// <returns><see langword="true"/> when the user-session refresh-rate helper was started.</returns>
    public bool ApplyLaptopDisplayMode(LaptopDisplayMode mode, InteractiveSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return mode switch
        {
            LaptopDisplayMode.Auto => StartDisplayCommand(
                session,
                DisplayCommand.ScreenModeAuto,
                mode),
            LaptopDisplayMode.Hz60 => StartDisplayCommand(
                session,
                DisplayCommand.ScreenMode60Hz,
                mode),
            LaptopDisplayMode.Hz240Overdrive => StartDisplayCommand(
                session,
                DisplayCommand.ScreenModeMaxRefreshOverdrive,
                mode),
            _ => LogUnknownDisplayMode(mode),
        };
    }

    private bool LogUnknownDisplayMode(LaptopDisplayMode mode)
    {
        _logger.LogWarning("Unknown laptop display mode {Mode}.", mode);
        return false;
    }

    private bool StartDisplayCommand(
        InteractiveSession session,
        string screenMode,
        LaptopDisplayMode laptopDisplayMode)
    {
        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogError("Could not resolve current executable path. ResolvedPath={ExecutablePath}", executablePath);
            return false;
        }

        var arguments = $"{DisplayCommand.CommandName} {DisplayCommand.ScreenCommandName} {screenMode}";
        _logger.LogInformation(
            "Starting display command in session {SessionId}: {Path} {Arguments}",
            session.SessionId,
            executablePath,
            arguments);

        var started = _processLauncher.TryStart(session.SessionId, executablePath, arguments, _logger);
        if (!started)
        {
            _logger.LogWarning("Failed to start display command in session {SessionId}.", session.SessionId);
        }

        // Refresh rate changes run in the user session; overdrive is applied directly.
        ApplyOverdrive(laptopDisplayMode);
        return started;
    }

    private void ApplyOverdrive(LaptopDisplayMode mode)
    {
        if (!_capabilities.IsScreenOverdriveSupported())
        {
            _logger.LogDebug("ASUS panel overdrive is not supported on this platform.");
            return;
        }

        var overdrive = mode switch
        {
            LaptopDisplayMode.Auto => PowerStatus.IsOnAcPower() ? 1 : 0,
            LaptopDisplayMode.Hz60 => 0,
            LaptopDisplayMode.Hz240Overdrive => 1,
            _ => 0,
        };

        try
        {
            using var acpi = _acpiFactory.Open();
            if (!acpi.IsConnected)
            {
                return;
            }

            int? current = null;
            try
            {
                current = acpi.ReadDeviceValue(AsusAcpiDeviceIds.ScreenOverdrive, "ScreenOverdrive");
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not read screen overdrive before writing.");
            }

            if (current == overdrive)
            {
                _logger.LogInformation("Screen overdrive already has requested value {Overdrive}.", overdrive);
                return;
            }

            acpi.WriteDeviceValue(AsusAcpiDeviceIds.ScreenOverdrive, overdrive, "ScreenOverdrive");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to set screen overdrive to {Overdrive}.", overdrive);
        }
    }

    /// <summary>Applies a MiniLED local-dimming mode through the supported ASUS firmware endpoint.</summary>
    /// <param name="mode">The MiniLED local-dimming mode to apply.</param>
    /// <returns><see langword="true"/> when a supported firmware endpoint accepted the mode.</returns>
    public bool ApplyMiniLedMode(MiniLedMode mode)
    {
        var endpoints = _capabilities.GetMiniLedEndpoints();
        if (endpoints.Count == 0)
        {
            _logger.LogDebug("No readable ASUS MiniLED firmware endpoint was detected.");
            return false;
        }

        using var acpi = _acpiFactory.Open();
        if (!acpi.IsConnected)
        {
            return false;
        }

        foreach (var endpoint in endpoints)
        {
            var endpointValue = endpoint.Kind switch
            {
                AsusMiniLedEndpointKind.ThreeState => ToThreeStateMiniLedValue(mode),
                _ => ToTwoStateMiniLedValue(mode),
            };

            if (mode == MiniLedMode.MultiZoneStrong && endpoint.Kind == AsusMiniLedEndpointKind.TwoState)
            {
                _logger.LogWarning(
                    "Three-state MiniLED is unavailable. Falling back to normal multi-zone local dimming.");
            }

            if (TryWriteMiniLedEndpoint(
                    acpi,
                    endpoint.DeviceId,
                    endpointValue,
                    endpoint.DiagnosticName,
                    mode,
                    sleepAfterWrite: endpoint.Kind == AsusMiniLedEndpointKind.ThreeState))
            {
                return true;
            }
        }

        _logger.LogWarning("No supported MiniLED ACPI endpoint accepted mode {Mode}.", mode);
        return false;
    }

    private static int ToTwoStateMiniLedValue(MiniLedMode mode) => mode switch
    {
        MiniLedMode.OneZone => 0,
        MiniLedMode.MultiZone => 1,
        MiniLedMode.MultiZoneStrong => 1,
        _ => 1,
    };

    private static int ToThreeStateMiniLedValue(MiniLedMode mode) => mode switch
    {
        MiniLedMode.OneZone => 2,
        MiniLedMode.MultiZone => 0,
        MiniLedMode.MultiZoneStrong => 1,
        _ => 0,
    };

    private bool TryWriteMiniLedEndpoint(
        AsusAcpiClient acpi,
        uint endpoint,
        int endpointValue,
        string endpointName,
        MiniLedMode requestedMode,
        bool sleepAfterWrite)
    {
        try
        {
            var result = acpi.WriteDeviceValue(endpoint, endpointValue, endpointName);
            if (result != 1)
            {
                _logger.LogDebug(
                    "{EndpointName} rejected {RequestedMode} using ACPI value {EndpointValue}. Result={Result}.",
                    endpointName,
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
                "Applied {RequestedMode} through {EndpointName} using ACPI value {EndpointValue}.",
                requestedMode,
                endpointName,
                endpointValue);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to apply {RequestedMode} through {EndpointName} using ACPI value {EndpointValue}.",
                requestedMode,
                endpointName,
                endpointValue);
            return false;
        }
    }

    private static string? ResolveCurrentExecutablePath() =>
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

}
