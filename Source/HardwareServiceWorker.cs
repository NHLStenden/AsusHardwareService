using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Coordinates the background lifecycle of the ASUS hardware service.
/// </summary>
/// <remarks>
/// This worker applies startup hardware state, listens for ASUS HID hotkey events, monitors the
/// active interactive user session, and dispatches hardware actions to the service controllers.
/// It currently handles battery charge limit initialisation, colour profile application on session
/// changes, brightness and microphone hotkeys, and combined performance/GPU mode switching through
/// <c>Fn+M4</c>.
/// </remarks>
public sealed class HardwareServiceWorker : BackgroundService
{
    private static readonly TimeSpan SessionMonitorInterval = TimeSpan.FromSeconds(2);

    private const int FnPlusF7 = 16;
    private const int FnPlusF8 = 32;
    private const int FnPlusM3 = 124;
    private const int FnPlusM4 = 174;
    private const int FnPlusM5 = 56;

    private readonly ILogger<HardwareServiceWorker> _logger;
    private readonly AsusHidInput _hid;
    private readonly BatteryChargeLimiter _batteryChargeLimiter;
    private readonly BrightnessController _brightnessController;
    private readonly ColorProfileApplier _colorProfileApplier;
    private readonly MicController _micController;
    private readonly ModeGpuManager _modeGpuManager;
    private readonly HardwareOptions _options;
    private readonly SemaphoreSlim _modeLock = new(1, 1);

    private PerformanceMode _performanceMode;
    private GpuMode _gpuMode;
    private int? _lastSessionId;

    /// <summary>
    /// Initialises a new instance of the <see cref="HardwareServiceWorker"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and service lifecycle messages.</param>
    /// <param name="hid">The ASUS HID input listener used to receive hotkey events.</param>
    /// <param name="batteryChargeLimiter">The battery charge limiter controller.</param>
    /// <param name="brightnessController">The brightness controller.</param>
    /// <param name="colorProfileApplier">The colour profile launcher and applier.</param>
    /// <param name="micController">The microphone mute controller.</param>
    /// <param name="modeGpuManager">The combined performance and GPU mode manager.</param>
    /// <param name="options">The configured hardware service options.</param>
    public HardwareServiceWorker(
        ILogger<HardwareServiceWorker> logger,
        AsusHidInput hid,
        BatteryChargeLimiter batteryChargeLimiter,
        BrightnessController brightnessController,
        ColorProfileApplier colorProfileApplier,
        MicController micController,
        ModeGpuManager modeGpuManager,
        IOptions<HardwareOptions> options)
    {
        _logger = logger;
        _hid = hid;
        _batteryChargeLimiter = batteryChargeLimiter;
        _brightnessController = brightnessController;
        _colorProfileApplier = colorProfileApplier;
        _micController = micController;
        _modeGpuManager = modeGpuManager;
        _options = options.Value;
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
        await RestoreModesAsync(stoppingToken);

        var hidTask = Task.Run(() => _hid.ListenAsync(HandleAsusEventAsync, stoppingToken), stoppingToken);
        var sessionTask = MonitorUserSessionAsync(stoppingToken);

        await Task.WhenAll(hidTask, sessionTask);
    }

    /// <summary>
    /// Restores the configured startup performance and GPU mode combination.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the restore operation finishes.</returns>
    private async Task RestoreModesAsync(CancellationToken cancellationToken)
    {
        await ApplyCombinedModeAsync(_options.PerformanceMode, _options.GpuMode, cancellationToken);
        _performanceMode = _options.PerformanceMode;
        _gpuMode = _options.GpuMode;
    }

    /// <summary>
    /// Polls for the active interactive user session and reapplies the configured colour profile when
    /// a new session becomes active.
    /// </summary>
    /// <param name="stoppingToken">A token that signals when the service should stop.</param>
    /// <returns>A task that completes when session monitoring stops.</returns>
    private async Task MonitorUserSessionAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SessionMonitorInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var session = UserSessionHelper.TryGetActiveInteractiveSession();
                if (session is null)
                {
                    _logger.LogInformation("No active interactive session detected.");
                    _lastSessionId = null;
                }
                else
                {
                    _logger.LogInformation(
                        "Active interactive session detected. SessionId={SessionId}, User={Domain}\\{User}",
                        session.SessionId,
                        session.Domain,
                        session.UserName);

                    if (_lastSessionId != session.SessionId)
                    {
                        await ApplyColourProfileForSessionAsync(session.SessionId, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while monitoring interactive user session.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
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
                    await ToggleCombinedModeAsync();
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
    /// Cycles to the next combined performance and GPU mode pair.
    /// </summary>
    /// <returns>A task that completes when the mode switch has finished.</returns>
    private async Task ToggleCombinedModeAsync()
    {
        await _modeLock.WaitAsync();
        try
        {
            var current = GetCurrentCombinedMode();
            var next = GetNextCombinedMode(current.performanceMode, current.gpuMode);

            await ApplyCombinedModeAsync(next.performanceMode, next.gpuMode, CancellationToken.None);
            _performanceMode = next.performanceMode;
            _gpuMode = next.gpuMode;
        }
        finally
        {
            _modeLock.Release();
        }
    }

    /// <summary>
    /// Reads the currently active combined performance and GPU mode pair from hardware.
    /// </summary>
    /// <returns>The current performance mode and GPU mode.</returns>
    private (PerformanceMode performanceMode, GpuMode gpuMode) GetCurrentCombinedMode() => (_performanceMode, _gpuMode);

    /// <summary>
    /// Returns the next combined mode pair in the service rotation.
    /// </summary>
    /// <param name="performanceMode">The current performance mode.</param>
    /// <param name="gpuMode">The current GPU mode.</param>
    /// <returns>The next combined performance/GPU mode pair.</returns>
    private static (PerformanceMode performanceMode, GpuMode gpuMode) GetNextCombinedMode(PerformanceMode performanceMode, GpuMode gpuMode) =>
        performanceMode == PerformanceMode.Silent && gpuMode == GpuMode.Eco
            ? (PerformanceMode.Balanced, GpuMode.Standard)
            : (PerformanceMode.Silent, GpuMode.Eco);

    /// <summary>
    /// Applies a combined performance and GPU mode pair.
    /// </summary>
    /// <param name="performanceMode">The performance mode to apply.</param>
    /// <param name="gpuMode">The GPU mode to apply.</param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when both mode operations have finished.</returns>
    private async Task ApplyCombinedModeAsync(PerformanceMode performanceMode, GpuMode gpuMode, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying combined mode {PerformanceMode}/{GpuMode}.", performanceMode, gpuMode);

        var perfResult = await _modeGpuManager.SetPerformanceModeAsync(performanceMode, cancellationToken);
        if (perfResult != 1)
        {
            _logger.LogWarning("Setting performance mode to {PerformanceMode} returned {Result}.", performanceMode, perfResult);
        }

        var gpuResult = await _modeGpuManager.SetGpuModeAsync(gpuMode, cancellationToken: cancellationToken);
        _logger.LogInformation("GPU mode apply result for {GpuMode}: {Result}.", gpuMode, gpuResult);
    }

    /// <summary>
    /// Applies the configured colour profile to a newly detected interactive session.
    /// </summary>
    /// <param name="sessionId">The active session identifier.</param>
    /// <param name="stoppingToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the colour profile handling finishes.</returns>
    private async Task ApplyColourProfileForSessionAsync(int sessionId, CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "New session detected. Previous={PreviousSessionId}, Current={CurrentSessionId}",
            _lastSessionId,
            sessionId);

        await Task.Delay(_options.ColorProfileDelay, stoppingToken);
        var started = await _colorProfileApplier.ApplyAsync(sessionId);

        if (started)
        {
            _lastSessionId = sessionId;
            _logger.LogInformation("AsusSplendid launch request succeeded for session {SessionId}.", sessionId);
            return;
        }

        _logger.LogWarning("AsusSplendid launch request failed for session {SessionId}.", sessionId);
    }
}
