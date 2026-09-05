namespace AsusHardwareService;

/// <summary>
/// Starts or notifies the lightweight hardware status UI in the active user session.
/// </summary>
public sealed class HardwareUiNotifier
{
    private readonly ILogger<HardwareUiNotifier> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="HardwareUiNotifier"/> class.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    public HardwareUiNotifier(ILogger<HardwareUiNotifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Shows the current microphone mute state in the active interactive user session.
    /// </summary>
    /// <param name="muted"><see langword="true"/> when the microphone is muted.</param>
    public void ShowMicrophoneStatus(bool muted)
    {
        var state = muted ? "muted" : "unmuted";
        ShowStatus(
            $"{HardwareUiCommand.MicrophoneCommandName} {state}",
            "microphone");
    }

    /// <summary>
    /// Shows the current ASUS keyboard-backlight level in the active interactive user session.
    /// </summary>
    /// <param name="level">The keyboard-backlight level from <c>0</c> through <c>3</c>.</param>
    public void ShowKeyboardBacklightStatus(int level)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        ShowStatus(
            $"{HardwareUiCommand.KeyboardBacklightCommandName} {level}",
            "keyboard backlight");
    }

    /// <summary>
    /// Shows the current built-in display brightness in the active interactive user session.
    /// </summary>
    /// <param name="brightness">The display brightness percentage from <c>0</c> through <c>100</c>.</param>
    public void ShowDisplayBrightnessStatus(int brightness)
    {
        if (brightness is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "Display brightness must be between 0 and 100.");
        }

        ShowStatus(
            $"{HardwareUiCommand.DisplayBrightnessCommandName} {brightness}",
            "display brightness");
    }

    /// <summary>
    /// Shows the current combined ASUS performance and GPU mode in the active interactive user session.
    /// </summary>
    /// <param name="performanceMode">The active or requested ASUS performance mode.</param>
    /// <param name="gpuMode">The active or requested ASUS GPU mode.</param>
    public void ShowPerformanceGpuStatus(
        PerformanceMode performanceMode,
        GpuMode gpuMode)
    {
        if (performanceMode is not (PerformanceMode.Balanced or PerformanceMode.Silent))
        {
            throw new ArgumentOutOfRangeException(nameof(performanceMode), performanceMode, "Unsupported performance mode.");
        }

        if (gpuMode is not (GpuMode.Eco or GpuMode.Standard))
        {
            throw new ArgumentOutOfRangeException(nameof(gpuMode), gpuMode, "Unsupported GPU mode.");
        }

        ShowStatus(
            $"{HardwareUiCommand.PerformanceGpuCommandName} {performanceMode} {gpuMode}",
            "performance/GPU mode");
    }

    /// <summary>
    /// Requests that the resident hardware UI in a specific user session exits cleanly.
    /// </summary>
    /// <param name="sessionId">The Windows session containing the resident UI.</param>
    public void StopUiInSession(int sessionId)
    {
        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        SessionProcessLauncher.TryStartProcessInSession(
            sessionId,
            executablePath,
            $"{HardwareUiCommand.CommandName} {HardwareUiCommand.ShutdownCommandName}",
            _logger,
            createConsoleWindow: false);
    }

    /// <summary>
    /// Starts the UI-mode invocation that forwards a status update inside the active user session.
    /// </summary>
    /// <param name="statusArguments">Arguments following the main <c>ui</c> command.</param>
    /// <param name="statusName">Human-readable status name used for diagnostics.</param>
    private void ShowStatus(string statusArguments, string statusName)
    {
        var session = UserSessionHelper.GetActiveInteractiveSession();
        if (session is null)
        {
            _logger.LogDebug(
                "Skipping {StatusName} UI because no active interactive user session is available.",
                statusName);
            return;
        }

        var executablePath = ResolveCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogWarning("Could not resolve the current executable path for the hardware UI.");
            return;
        }

        var arguments = $"{HardwareUiCommand.CommandName} {statusArguments}";
        if (!SessionProcessLauncher.TryStartProcessInSession(
                session.SessionId,
                executablePath,
                arguments,
                _logger,
                createConsoleWindow: false))
        {
            _logger.LogWarning(
                "Failed to start or notify the {StatusName} UI in session {SessionId}.",
                statusName,
                session.SessionId);
        }
    }

    private static string? ResolveCurrentExecutablePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }
}
