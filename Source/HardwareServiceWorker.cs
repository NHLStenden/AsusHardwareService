using Microsoft.Extensions.Options;

namespace AsusHardwareService;
/// <summary>
/// Coordinates the background lifecycle of the ASUS hardware service.
/// </summary>
/// <remarks>
/// This worker applies startup hardware state, listens for ASUS HID hotkey events, monitors the
/// active interactive user session, and dispatches hardware actions to the service controllers.
/// It handles battery charge limit initialisation, ASUS display settings, colour profile application
/// on session changes, display and keyboard brightness hotkeys, microphone hotkeys, and combined performance/GPU mode switching
/// through <c>Fn+M4</c>.
/// </remarks>
public sealed class HardwareServiceWorker : BackgroundService
{
    private static readonly TimeSpan SessionMonitorInterval = TimeSpan.FromSeconds(2);
    private const int FnPlusF2 = 197;
    private const int FnPlusF3 = 196;
    private const int FnPlusF7 = 16;
    private const int FnPlusF8 = 32;
    private const int FnPlusM3 = 124;
    private const int FnPlusM4 = 174;
    private const int FnPlusM5 = 56;
    private readonly ILogger<HardwareServiceWorker> _logger;
    private readonly AsusHidInput _hid;
    private readonly BatteryChargeLimiter _batteryChargeLimiter;
    private readonly BrightnessController _brightnessController;
    private readonly KeyboardBacklightController _keyboardBacklightController;
    private readonly DisplayController _displayController;
    private readonly SplendidProfileApplier _splendidProfileApplier;
    private readonly MicController _micController;
    private readonly PerformanceGpuController _performanceGpuController;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    private int? _lastSessionId;
    /// <summary>
    /// Initialises a new instance of the <see cref="HardwareServiceWorker"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and service lifecycle messages.</param>
    /// <param name="hid">The ASUS HID input listener used to receive hotkey events.</param>
    /// <param name="batteryChargeLimiter">The battery charge limiter controller.</param>
    /// <param name="brightnessController">The display brightness controller.</param>
    /// <param name="keyboardBacklightController">The keyboard backlight controller.</param>
    /// <param name="displayController">The display controller.</param>
    /// <param name="splendidProfileApplier">The colour profile launcher and applier.</param>
    /// <param name="micController">The microphone mute controller.</param>
    /// <param name="performanceGpuController">The combined performance and GPU mode manager.</param>
    /// <param name="options">The configured hardware service options.</param>
    public HardwareServiceWorker(
        ILogger<HardwareServiceWorker> logger,
        AsusHidInput hid,
        BatteryChargeLimiter batteryChargeLimiter,
        BrightnessController brightnessController,
        KeyboardBacklightController keyboardBacklightController,
        DisplayController displayController,
        SplendidProfileApplier splendidProfileApplier,
        MicController micController,
        PerformanceGpuController performanceGpuController,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hid = hid ?? throw new ArgumentNullException(nameof(hid));
        _batteryChargeLimiter = batteryChargeLimiter ?? throw new ArgumentNullException(nameof(batteryChargeLimiter));
        _brightnessController = brightnessController ?? throw new ArgumentNullException(nameof(brightnessController));
        _keyboardBacklightController = keyboardBacklightController ?? throw new ArgumentNullException(nameof(keyboardBacklightController));
        _displayController = displayController ?? throw new ArgumentNullException(nameof(displayController));
        _splendidProfileApplier = splendidProfileApplier ?? throw new ArgumentNullException(nameof(splendidProfileApplier));
        _micController = micController ?? throw new ArgumentNullException(nameof(micController));
        _performanceGpuController = performanceGpuController ?? throw new ArgumentNullException(nameof(performanceGpuController));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Runs the background worker until the host is stopped.
    /// </summary>
    /// <param name="stoppingToken">A token that signals when the service should stop.</param>
    /// <returns>A task that completes when the worker shuts down.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service started in Session 0.");
        _batteryChargeLimiter.ApplyChargeLimit();
        _displayController.ApplyConfiguredServiceDisplaySettings();
        await ApplyConfiguredModesAsync(stoppingToken);

        var hidTask = Task.Run(() => _hid.ListenAsync(HandleAsusEventAsync, stoppingToken), stoppingToken);
        var sessionTask = MonitorUserSessionAsync(stoppingToken);

        await Task.WhenAll(hidTask, sessionTask);
    }
    /// <summary>
    /// Restores the configured startup performance and GPU mode combination.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the restore operation finishes.</returns>
    private Task ApplyConfiguredModesAsync(CancellationToken cancellationToken)
    {
        return _performanceGpuController.ApplyCombinedModeAsync(
            _options.CurrentValue.PerformanceMode,
            _options.CurrentValue.GpuMode,
            cancellationToken);
    }
    /// <summary>
    /// Waits for interactive user sessions and reapplies user-session display and colour settings
    /// whenever a new session becomes active.
    /// </summary>
    /// <param name="stoppingToken">A token that signals when the service should stop.</param>
    /// <returns>A task that completes when session monitoring stops.</returns>
    private async Task MonitorUserSessionAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var session = await UserSessionHelper.WaitForActiveInteractiveSessionAsync(
                    SessionMonitorInterval,
                    stoppingToken);
                if (session is null)
                {
                    return;
                }
                await HandleInteractiveSessionAsync(session, stoppingToken);
                await WaitForSessionChangeAsync(session.SessionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while monitoring interactive user session.");
                await Task.Delay(SessionMonitorInterval, stoppingToken);
            }
        }
    }
    /// <summary>
    /// Applies settings that need an active interactive user session.
    /// </summary>
    /// <param name="session">The active interactive user session.</param>
    /// <param name="stoppingToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the session-specific startup actions finish.</returns>
    private async Task HandleInteractiveSessionAsync(SessionInfo session, CancellationToken stoppingToken)
    {
        if (_lastSessionId == session.SessionId)
        {
            return;
        }
        _logger.LogInformation(
            "Active interactive session detected. SessionId={SessionId}, User={Domain}\\{User}",
            session.SessionId,
            session.Domain,
            session.UserName);

        _lastSessionId = session.SessionId;

        _displayController.ApplyConfiguredLaptopScreenMode(session);
        await ApplyColorProfileForSessionAsync(session.SessionId, stoppingToken);
    }
    /// <summary>
    /// Waits until the current active session disappears or changes.
    /// </summary>
    /// <param name="sessionId">The session currently being tracked.</param>
    /// <param name="stoppingToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the session changes or monitoring stops.</returns>
    private async Task WaitForSessionChangeAsync(int sessionId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(SessionMonitorInterval, stoppingToken);
            var session = UserSessionHelper.GetActiveInteractiveSession();
            if (session?.SessionId == sessionId)
            {
                continue;
            }

            _logger.LogInformation(
                "Interactive session changed. Previous={PreviousSessionId}, Current={CurrentSessionId}",
                sessionId,
                session?.SessionId);

            if (session is null)
            {
                _lastSessionId = null;
            }
            return;
        }
    }
    /// <summary>
    /// Dispatches an ASUS HID event to the corresponding hardware action.
    /// </summary>
    /// <param name="eventId">The ASUS HID event identifier.</param>
    /// <returns>A task that completes when the event has been handled.</returns>
    private async Task HandleAsusEventAsync(int eventId)
    {
        try
        {
            switch (eventId)
            {
                case FnPlusF2:
                    _keyboardBacklightController.Decrease();
                    break;
                case FnPlusF3:
                    _keyboardBacklightController.Increase();
                    break;

                case FnPlusF7:
                    _brightnessController.Decrease();
                    break;
                case FnPlusF8:
                    _brightnessController.Increase();
                    break;

                case FnPlusM3:
                    _micController.Toggle();
                    break;

                case FnPlusM4:
                    await _performanceGpuController.ToggleCombinedModeAsync(CancellationToken.None);
                    break;
                case FnPlusM5:
                default:
                    _logger.LogDebug("Ignoring ASUS HID event {EventId}.", eventId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle ASUS HID event {EventId}.", eventId);
        }
    }
    /// <summary>
    /// Applies the configured colour profile to a newly detected interactive session.
    /// </summary>
    /// <param name="sessionId">The active session identifier.</param>
    /// <param name="stoppingToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the colour profile handling finishes.</returns>
    private async Task ApplyColorProfileForSessionAsync(int sessionId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Applying user-session colour profile for session {SessionId}.", sessionId);
        await Task.Delay(_options.CurrentValue.ColorProfileDelay, stoppingToken);
        var started = await _splendidProfileApplier.ApplyProfileAsync(sessionId, stoppingToken);

        if (started)
        {
            _logger.LogInformation("AsusSplendid launch request succeeded for session {SessionId}.", sessionId);
            return;
        }

        _logger.LogWarning("AsusSplendid launch request failed for session {SessionId}.", sessionId);
    }
}
