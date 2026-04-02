using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace AsusHardwareService;

/// <summary>
/// Toggles the mute state of the built in microphone endpoints.
/// </summary>
public sealed class MicController
{
    private readonly ILogger<MicController> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="MicController"/> class.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    public MicController(ILogger<MicController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Toggles the built in microphone on or off.
    /// </summary>
    public void Toggle()
    {
        using var enumerator = new MMDeviceEnumerator();

        var devices = new[]
        {
            TryGetDefaultAudioEndpoint(enumerator, Role.Communications),
            TryGetDefaultAudioEndpoint(enumerator, Role.Console),
            TryGetDefaultAudioEndpoint(enumerator, Role.Multimedia)
        }
        .Where(device => device is not null)
        .Cast<MMDevice>()
        .GroupBy(device => device.ID)
        .Select(group => group.First())
        .ToList();

        if (devices.Count == 0)
        {
            _logger.LogWarning("No default capture devices were found to toggle microphone mute.");
            return;
        }

        bool newMuteState = !devices[0].AudioEndpointVolume.Mute;

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