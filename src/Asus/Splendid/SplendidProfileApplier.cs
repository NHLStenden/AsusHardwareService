using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Configuration;
using AsusHardwareService.Windows.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Asus.Splendid;

/// <summary>Locates and invokes ASUS Splendid color-profile commands inside an user session.</summary>
internal sealed class SplendidProfileApplier
{
    private const string SplendidExecutableName = "AsusSplendid.exe";
    private const int InitializeCommand = 10;
    private const int GamutModeCommand = 200;
    private const int DefaultVisualCommand = 11;
    private const int DefaultIntensity = 50;

    private readonly ILogger<SplendidProfileApplier> _logger;
    private readonly SessionProcessLauncher _processLauncher;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes the ASUS Splendid adapter.</summary>
    public SplendidProfileApplier(
        ILogger<SplendidProfileApplier> logger,
        SessionProcessLauncher processLauncher,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Runs the ASUS Splendid command sequence in the specified user session.</summary>
    public Task<bool> ApplyConfiguredProfileAsync(
        int sessionId,
        CancellationToken cancellationToken = default) =>
        ApplyProfileAsync(
            sessionId,
            _options.CurrentValue.SplendidVisualMode,
            _options.CurrentValue.SplendidGamutMode,
            (SplendidColorTemperature)_options.CurrentValue.ColorTemperature,
            cancellationToken);

    /// <summary>Applies one complete ASUS Splendid display-color profile.</summary>
    /// <param name="sessionId">The user session that owns the display profile.</param>
    /// <param name="visualMode">The visual preset to apply.</param>
    /// <param name="gamutMode">The color gamut to apply.</param>
    /// <param name="colorTemperature">The color-temperature preset to apply.</param>
    /// <param name="cancellationToken">Cancels the command sequence.</param>
    /// <returns><see langword="true"/> when every ASUS Splendid command was launched.</returns>
    public async Task<bool> ApplyProfileAsync(
        int sessionId,
        SplendidVisualMode visualMode,
        SplendidGamutMode gamutMode,
        SplendidColorTemperature colorTemperature,
        CancellationToken cancellationToken = default)
    {
        var executablePath = AsusDriverLocator.TryResolveCompanionFile(SplendidExecutableName, _logger);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var options = _options.CurrentValue;
        foreach (var command in BuildCommandSequence(options, visualMode, gamutMode, colorTemperature))
        {
            var arguments = BuildArguments(command);
            _logger.LogInformation(
                "Launching ASUS Splendid in session {SessionId}: \"{ExecutablePath}\" {Arguments}",
                sessionId,
                executablePath,
                arguments);

            if (!_processLauncher.TryStart(sessionId, executablePath, arguments, _logger))
            {
                return false;
            }

            if (command.DelayAfterCommand)
            {
                await Task.Delay(
                    _options.CurrentValue.ColorProfileCommandDelayMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    private static IReadOnlyList<SplendidCommand> BuildCommandSequence(
        HardwareOptions options,
        SplendidVisualMode visualMode,
        SplendidGamutMode gamutMode,
        SplendidColorTemperature colorTemperature)
    {
        List<SplendidCommand> commands = [new(InitializeCommand)];
        if (options.ResetColorProfileBeforeApply)
        {
            commands.Add(new(GamutModeCommand, 0, (int)SplendidGamutMode.Native));
            commands.Add(new(DefaultVisualCommand, 0, DefaultIntensity));
        }

        commands.Add(new(GamutModeCommand, 0, (int)gamutMode));
        commands.Add(new((int)visualMode, 0, (int)colorTemperature));
        return commands;
    }

    private static string BuildArguments(SplendidCommand command)
    {
        List<string> parts = [command.Command.ToString()];
        if (command.Parameter1.HasValue)
        {
            parts.Add(command.Parameter1.Value.ToString());
        }

        if (command.Parameter2.HasValue)
        {
            parts.Add(command.Parameter2.Value.ToString());
        }

        if (command.Parameter3.HasValue)
        {
            parts.Add(command.Parameter3.Value.ToString());
        }

        return string.Join(" ", parts);
    }

    private sealed record SplendidCommand(
        int Command,
        int? Parameter1 = null,
        int? Parameter2 = null,
        int? Parameter3 = null,
        bool DelayAfterCommand = true);
}
