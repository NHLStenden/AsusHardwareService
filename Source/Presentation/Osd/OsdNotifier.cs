using AsusHardwareService.Presentation;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Windows.Processes;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Presentation.Osd;

/// <summary>
/// Publishes hardware state by launching or notifying the resident Win32 on-screen display in the active user session.
/// </summary>
internal sealed class OsdNotifier : IHardwareStatusPublisher, IOnScreenDisplayLifecycle
{
    private readonly ILogger<OsdNotifier> _logger;
    private readonly UserSessionService _interactiveSessions;
    private readonly SessionProcessLauncher _processLauncher;

    /// <summary>Initializes the OSD presentation adapter.</summary>
    public OsdNotifier(
        ILogger<OsdNotifier> logger,
        UserSessionService interactiveSessions,
        SessionProcessLauncher processLauncher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interactiveSessions = interactiveSessions ?? throw new ArgumentNullException(nameof(interactiveSessions));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    /// <inheritdoc />
    public void Publish(HardwareStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        switch (status)
        {
            case MicrophoneMuteStatus microphone:
                ShowStatus(
                    $"{OsdCommand.MicrophoneCommandName} {(microphone.Muted ? "muted" : "unmuted")}",
                    "microphone");
                break;

            case KeyboardBacklightStatus keyboardBacklight:
                ValidateKeyboardBacklightLevel(keyboardBacklight.Level);
                ShowStatus(
                    $"{OsdCommand.KeyboardBacklightCommandName} {keyboardBacklight.Level}",
                    "keyboard backlight");
                break;

            case DisplayBrightnessStatus displayBrightness:
                ValidateBrightness(displayBrightness.Percentage);
                ShowStatus(
                    $"{OsdCommand.DisplayBrightnessCommandName} {displayBrightness.Percentage}",
                    "display brightness");
                break;

            case OperatingModeStatus operatingMode:
                ValidateOperatingMode(operatingMode.Mode);
                ShowStatus(
                    $"{OsdCommand.PerformanceGpuCommandName} " +
                    $"{operatingMode.Mode.Performance} {operatingMode.Mode.Gpu}",
                    "performance/GPU mode");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported hardware status type.");
        }
    }

    /// <inheritdoc />
    public void StopInSession(int sessionId)
    {
        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        _processLauncher.TryStart(
            sessionId,
            executablePath,
            $"{OsdCommand.CommandName} {OsdCommand.ShutdownCommandName}",
            _logger,
            createConsoleWindow: false);
    }

    private void ShowStatus(string statusArguments, string statusName)
    {
        var session = _interactiveSessions.GetActiveSession();
        if (session is null)
        {
            _logger.LogDebug(
                "Skipping {StatusName} OSD because no active interactive user session is available.",
                statusName);
            return;
        }

        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogWarning("Could not resolve the current executable path for the on-screen display.");
            return;
        }

        var arguments = $"{OsdCommand.CommandName} {statusArguments}";
        if (!_processLauncher.TryStart(
                session.SessionId,
                executablePath,
                arguments,
                _logger,
                createConsoleWindow: false))
        {
            _logger.LogWarning(
                "Failed to start or notify the {StatusName} OSD in session {SessionId}.",
                statusName,
                session.SessionId);
        }
    }

    private static void ValidateKeyboardBacklightLevel(int level)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }
    }

    private static void ValidateBrightness(int brightness)
    {
        if (brightness is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "Display brightness must be between 0 and 100.");
        }
    }

    private static void ValidateOperatingMode(HardwareOperatingMode mode)
    {
        if (mode.Performance is not (PerformanceMode.Balanced or PerformanceMode.Silent))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported performance mode.");
        }

        if (mode.Gpu is not (GpuMode.Eco or GpuMode.Standard))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported GPU mode.");
        }
    }
    private static string? ResolveCurrentExecutablePath() =>
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

}
