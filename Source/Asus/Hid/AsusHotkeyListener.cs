using System.Text;
using AsusHardwareService.Configuration;
using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Asus.Hid;

/// <summary>
/// Reads ASUS vendor HID reports and exposes them as logical hardware hotkey events.
/// </summary>
internal sealed class AsusHotkeyListener
{
    private const byte IgnoredEventId = 236;
    private static readonly byte[] InitializationPayload = Encoding.ASCII.GetBytes("ZASUS Tech.Inc.");

    private readonly ILogger<AsusHotkeyListener> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes the ASUS HID adapter.</summary>
    public AsusHotkeyListener(
        ILogger<AsusHotkeyListener> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Reads ASUS vendor HID reports until cancellation and emits mapped hotkey events.</summary>
    public async Task ListenAsync(Func<AsusHotkeyEvent, Task> onHotkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onHotkey);
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            HidStream? inputStream = null;
            CancellationTokenRegistration cancellationRegistration = default;
            try
            {
                inputStream = OpenInputStream();
                if (inputStream is null)
                {
                    var retryDelay = _options.CurrentValue.HidRetryDelayMilliseconds;
                    _logger.LogWarning("No supported ASUS HID input stream found. Retrying in {DelayMs} ms.", retryDelay);
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                InitializeSupportedDevices();
                _logger.LogInformation("Listening for ASUS hotkeys on HID path {Path}.", inputStream.Device.DevicePath);
                inputStream.ReadTimeout = Timeout.Infinite;
                cancellationRegistration = cancellationToken.Register(
                    static state =>
                    {
                        try
                        {
                            ((HidStream)state!).Dispose();
                        }
                        catch
                        {
                            // Disposing from cancellation is best effort; the listening loop handles shutdown.
                        }
                    },
                    inputStream);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var report = inputStream.Read();
                    if (!TryReadEventId(report, out var eventId))
                    {
                        continue;
                    }

                    var logicalHotkey = AsusHotkeyMapper.Map(eventId);
                    _logger.LogInformation(
                        "ASUS HID event {EventId} mapped to {Hotkey}.",
                        eventId,
                        logicalHotkey);
                    await onHotkey(new AsusHotkeyEvent(logicalHotkey, eventId)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("ASUS HID stream closed because service shutdown was requested.");
                break;
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogError(exception, "ASUS HID listener failed. The service will retry device discovery.");
                await Task.Delay(_options.CurrentValue.HidRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cancellationRegistration.Dispose();
                inputStream?.Dispose();
            }
        }
    }

    private HidStream? OpenInputStream()
    {
        foreach (var device in AsusHidDeviceCatalog.GetSupportedDevices())
        {
            try
            {
                _logger.LogInformation(
                    "Candidate ASUS HID device: PID={ProductId:X}, Path={Path}",
                    device.ProductID,
                    device.DevicePath);
                return device.Open();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Skipping ASUS HID PID={ProductId:X}.", device.ProductID);
            }
        }

        return null;
    }

    private void InitializeSupportedDevices()
    {
        foreach (var device in AsusHidDeviceCatalog.GetSupportedDevices())
        {
            try
            {
                using var stream = device.Open();
                var featureBuffer = new byte[device.GetMaxFeatureReportLength()];
                Array.Copy(InitializationPayload, featureBuffer, InitializationPayload.Length);
                stream.SetFeature(featureBuffer);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "ASUS HID initialization failed for PID={ProductId:X}.", device.ProductID);
            }
        }
    }

    private static bool TryReadEventId(byte[] report, out int eventId)
    {
        eventId = default;
        if (report.Length <= 1 || report[0] != AsusHidDeviceCatalog.InputReportId)
        {
            return false;
        }

        var candidate = report[1];
        if (candidate is 0 or IgnoredEventId)
        {
            return false;
        }

        eventId = candidate;
        return true;
    }
}
