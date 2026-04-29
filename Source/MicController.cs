using NAudio.CoreAudioApi;

namespace AsusHardwareService;

/// <summary>
/// Toggles the mute state of the built in microphone endpoints.
/// </summary>
public sealed class MicController
{
    private static readonly Role[] CaptureRoles = [Role.Communications, Role.Console, Role.Multimedia];

    private readonly ILogger<MicController> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="MicController"/> class.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    public MicController(ILogger<MicController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Toggles the built in microphone on or off.
    /// </summary>
    public void Toggle()
    {
        using var enumerator = new MMDeviceEnumerator();

        var devices = CaptureRoles
            .Select(role => TryGetDefaultAudioEndpoint(enumerator, role))
            .OfType<MMDevice>()
            .DistinctBy(device => device.ID)
            .ToList();

        if (devices.Count == 0)
        {
            _logger.LogWarning("No default capture devices were found to toggle microphone mute.");
            return;
        }

        var newMuteState = !devices[0].AudioEndpointVolume.Mute;
        foreach (var device in devices)
        {
            if (device.AudioEndpointVolume.Mute != newMuteState)
            {
                device.AudioEndpointVolume.Mute = newMuteState;
            }
        }

        _logger.LogInformation(
            "Microphone mute toggled. New state: {MuteState}. Devices affected: {DeviceCount}.",
            newMuteState ? "Muted" : "Unmuted",
            devices.Count);
    }

    /// <summary>
    /// Tries to resolve the default capture endpoint for the specified role.
    /// </summary>
    /// <param name="enumerator">The MMDevice enumerator to query.</param>
    /// <param name="role">The Windows audio role to resolve.</param>
    /// <returns>The matching device when available; otherwise, <see langword="null"/>.</returns>
    private MMDevice? TryGetDefaultAudioEndpoint(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No default capture endpoint available for role {Role}.", role);
            return null;
        }
    }
}
