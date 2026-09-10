using AsusHardwareService.Asus.Hid;
using AsusHardwareService.Asus.Keyboard;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Presentation;
using AsusHardwareService.Windows.Audio;
using AsusHardwareService.Windows.Display;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Service;

/// <summary>
/// Maps logical hardware hotkeys to application actions and publishes the resulting user-visible state.
/// </summary>
internal sealed class HotkeyHandler
{
    private readonly ILogger<HotkeyHandler> _logger;
    private readonly DisplayBrightnessController _displayBrightness;
    private readonly DisplayTopologyController _displayTopology;
    private readonly KeyboardBacklightController _keyboardBacklight;
    private readonly MicrophoneMuteController _microphoneMute;
    private readonly IHardwareStatusPublisher _statusPublisher;
    private readonly OperatingModeController _performanceMode;
    private HardwareOperatingMode? _expectedOperatingMode;

    /// <summary>Initializes the hotkey dispatcher.</summary>
    public HotkeyHandler(
        ILogger<HotkeyHandler> logger,
        DisplayBrightnessController displayBrightness,
        DisplayTopologyController displayTopology,
        KeyboardBacklightController keyboardBacklight,
        MicrophoneMuteController microphoneMute,
        IHardwareStatusPublisher statusPublisher,
        OperatingModeController performanceMode)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _displayBrightness = displayBrightness ?? throw new ArgumentNullException(nameof(displayBrightness));
        _displayTopology = displayTopology ?? throw new ArgumentNullException(nameof(displayTopology));
        _keyboardBacklight = keyboardBacklight ?? throw new ArgumentNullException(nameof(keyboardBacklight));
        _microphoneMute = microphoneMute ?? throw new ArgumentNullException(nameof(microphoneMute));
        _statusPublisher = statusPublisher ?? throw new ArgumentNullException(nameof(statusPublisher));
        _performanceMode = performanceMode ?? throw new ArgumentNullException(nameof(performanceMode));
    }

    /// <summary>Dispatches one logical ASUS hotkey without blocking subsequent HID input.</summary>
    /// <param name="hotkeyEvent">The logical action and original ASUS event identifier.</param>
    /// <param name="cancellationToken">Stops any asynchronous hardware transition with the service.</param>
    /// <returns>A completed task after synchronous dispatch; long-running mode changes continue asynchronously.</returns>
    public Task DispatchAsync(AsusHotkeyEvent hotkeyEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (hotkeyEvent.Hotkey)
            {
                case AsusHotkey.KeyboardBacklightDecrease:
                    PublishKeyboardBacklight(_keyboardBacklight.Decrease());
                    break;

                case AsusHotkey.KeyboardBacklightIncrease:
                    PublishKeyboardBacklight(_keyboardBacklight.Increase());
                    break;

                case AsusHotkey.DisplayBrightnessDecrease:
                    _statusPublisher.Publish(new DisplayBrightnessStatus(_displayBrightness.Decrease()));
                    break;

                case AsusHotkey.DisplayBrightnessIncrease:
                    _statusPublisher.Publish(new DisplayBrightnessStatus(_displayBrightness.Increase()));
                    break;

                case AsusHotkey.DisplayTopologyToggle:
                    _displayTopology.Toggle();
                    break;

                case AsusHotkey.MicrophoneMuteToggle:
                    var muted = _microphoneMute.Toggle();
                    if (muted.HasValue)
                    {
                        _statusPublisher.Publish(new MicrophoneMuteStatus(muted.Value));
                    }
                    break;

                case AsusHotkey.OperatingModeToggle:
                    QueueOperatingModeToggle(cancellationToken);
                    break;

                case AsusHotkey.VendorApplicationKey:
                    _logger.LogDebug(
                        "Ignoring ASUS HID event {EventId}: the model-dependent vendor application key has no service-owned state.",
                        hotkeyEvent.RawEventId);
                    break;

                case AsusHotkey.Unknown:
                default:
                    _logger.LogDebug("Ignoring unmapped ASUS HID event {EventId}.", hotkeyEvent.RawEventId);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle ASUS HID event {EventId}.", hotkeyEvent.RawEventId);
        }

        return Task.CompletedTask;
    }

    private void PublishKeyboardBacklight(int? level)
    {
        if (level.HasValue)
        {
            _statusPublisher.Publish(new KeyboardBacklightStatus(level.Value));
        }
    }

    private void QueueOperatingModeToggle(CancellationToken cancellationToken)
    {
        var current = _expectedOperatingMode ?? _performanceMode.CurrentMode;
        var requested = current.Toggle();
        _expectedOperatingMode = requested;
        _statusPublisher.Publish(new OperatingModeStatus(requested));
        _ = ApplyRequestedOperatingModeAsync(requested, cancellationToken);
    }

    private async Task ApplyRequestedOperatingModeAsync(
        HardwareOperatingMode requestedMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var changed = await _performanceMode.ApplyAsync(requestedMode, cancellationToken).ConfigureAwait(false);
            if (!changed)
            {
                _logger.LogWarning(
                    "ASUS performance/GPU mode request did not complete: {PerformanceMode}/{GpuMode}.",
                    requestedMode.Performance,
                    requestedMode.Gpu);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Cancelled ASUS performance/GPU transition during service shutdown: {PerformanceMode}/{GpuMode}.",
                requestedMode.Performance,
                requestedMode.Gpu);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to apply ASUS performance/GPU mode {PerformanceMode}/{GpuMode}.",
                requestedMode.Performance,
                requestedMode.Gpu);
        }
    }
}
