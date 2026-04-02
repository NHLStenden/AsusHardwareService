using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;

namespace AsusHardwareService;

/// <summary>
/// Coordinates the ASUS hardware service lifecycle.
/// </summary>
/// <remarks>
/// At startup, this worker applies the configured battery charge limit, starts the
/// ASUS HID listener loop, and watches for an interactive user session. When a user
/// session becomes available, it launches AsusSplendid.exe inside that session.
/// </remarks>
public sealed class HardwareServiceWorker : BackgroundService
{
    private const int FnPlusF7 = 16;
    private const int FnPlusF8 = 32;
    private const int FnPlusM3 = 124;
    private const int FnPlusM4 = 174;
    private const int FnPlusM5 = 56;
    private readonly ILogger<HardwareServiceWorker> _logger;
    private readonly AsusHidInput _hid;
    private readonly BrightnessController _brightnessController;
    private readonly BatteryChargeLimiter _batteryChargeLimiter;
    private readonly ColorProfileApplier _colorProfileApplier;

    private readonly HardwareOptions _options;

    private int? _lastSessionId;

    /// <summary>
    /// Initialises a new instance of the <see cref="HardwareServiceWorker"/> class.
    /// </summary>
    /// <param name="logger">The logger used for lifecycle and error reporting.</param>
    /// <param name="hid">The ASUS HID input listener.</param>
    /// <param name="brightnessController">The brightness controller for the built-in panel.</param>
    /// <param name="batteryChargeLimiter">The battery charge limiter.</param>
    /// <param name="colorProfileApplier">The color profile applier for the color correction.</param>
    /// <param name="options">The configured hardware service options.</param>
    public HardwareServiceWorker(
        ILogger<HardwareServiceWorker> logger,
        AsusHidInput hid,
        BrightnessController brightnessController,
        BatteryChargeLimiter batteryChargeLimiter,
        ColorProfileApplier colorProfileApplier,
        IOptions<HardwareOptions> options)
    {
        _logger = logger;
        _hid = hid;
        _brightnessController = brightnessController;
        _batteryChargeLimiter = batteryChargeLimiter;
        _colorProfileApplier = colorProfileApplier;
        _options = options.Value;
    }

    /// <summary>
    /// Starts the service work loop.
    /// </summary>
    /// <param name="stoppingToken">A token that signals service shutdown.</param>
    /// <returns>A task that completes when the HID listener stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service started in Session 0.");

        _batteryChargeLimiter.ApplyChargeLimit();

        Task hidTask = Task.Run(() => _hid.ListenAsync(HandleAsusEventAsync, stoppingToken), stoppingToken);
        Task sessionTask = MonitorUserSessionAsync(stoppingToken);

        await Task.WhenAll(hidTask, sessionTask);
    }

    private async Task MonitorUserSessionAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SessionInfo? session = UserSessionHelper.TryGetActiveInteractiveSession();

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
                        _logger.LogInformation(
                            "New session detected. Previous={PreviousSessionId}, Current={CurrentSessionId}",
                            _lastSessionId,
                            session.SessionId);

                        await Task.Delay(_options.ColorProfileDelay);
                        bool started = await _colorProfileApplier.ApplyAsync(session.SessionId);

                        if (started)
                        {
                            _lastSessionId = session.SessionId;

                            _logger.LogInformation(
                                "AsusSplendid launch request succeeded for session {SessionId}.",
                                session.SessionId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "AsusSplendid launch request failed for session {SessionId}.",
                                session.SessionId);
                        }
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
    /// Handles a single ASUS HID event.
    /// </summary>
    /// <param name="eventId">The ASUS event identifier received from the HID listener.</param>
    /// <returns>A completed task.</returns>
    private Task HandleAsusEventAsync(int eventId)
    {
        try
        {
            switch (eventId)
            {
                case FnPlusF7:
                    // Fn+F7
                    _brightnessController.Decrease();
                    break;

                case FnPlusF8:
                    // Fn+F8
                    _brightnessController.Increase();
                    break;
                case FnPlusM3: // Mute/unmute mic (Fn+M3)
                    ToggleMic();
                    break;
                case FnPlusM4: // Turbo (Fn+M4)
                case FnPlusM5: // Armory Crate (Fn+M5)
                default:
                    _logger.LogDebug(
                        "Ignoring ASUS HID event {EventId}.",
                        eventId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle ASUS HID event {EventId}.",
                eventId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Toggles the built-in microphone on or off.
    /// </summary>
    private void ToggleMic()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new[]
        {
            enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications),
            enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console),
            enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
        }
        .GroupBy(d => d.ID)
        .Select(g => g.First())
        .ToList();

        if (devices.Count == 0)
            return;

        var newMuteState = !devices[0].AudioEndpointVolume.Mute;
        foreach (var device in devices)
        {
            if (device.AudioEndpointVolume.Mute != newMuteState)
                device.AudioEndpointVolume.Mute = newMuteState;
        }
    }
}