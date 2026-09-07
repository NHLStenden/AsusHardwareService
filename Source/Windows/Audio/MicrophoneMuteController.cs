using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace AsusHardwareService.Windows.Audio;

/// <summary>Toggles mute on the distinct default Windows capture endpoints used by common audio roles.</summary>
internal sealed class MicrophoneMuteController
{
    private static readonly Role[] CaptureRoles = [Role.Communications, Role.Console, Role.Multimedia];
    private readonly ILogger<MicrophoneMuteController> _logger;

    /// <summary>Initializes the Windows microphone adapter.</summary>
    public MicrophoneMuteController(ILogger<MicrophoneMuteController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Toggles mute on the default Windows capture endpoints.</summary>
    /// <returns>The new mute state, or <see langword="null"/> when no usable capture endpoint exists.</returns>
    public bool? Toggle()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = CaptureRoles
            .Select(role => TryGetDefaultCaptureEndpoint(enumerator, role))
            .OfType<MMDevice>()
            .DistinctBy(device => device.ID)
            .ToList();

        if (devices.Count == 0)
        {
            _logger.LogWarning("No default capture devices were found to toggle microphone mute.");
            return null;
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
            "Microphone mute toggled. New state={MuteState}; devices affected={DeviceCount}.",
            newMuteState ? "Muted" : "Unmuted",
            devices.Count);
        return newMuteState;
    }

    private MMDevice? TryGetDefaultCaptureEndpoint(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "No default capture endpoint is available for role {Role}.", role);
            return null;
        }
    }
}
