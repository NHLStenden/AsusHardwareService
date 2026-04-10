using System.Collections.Frozen;
using System.Text;
using HidSharp;
using HidSharp.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Discovers a supported ASUS HID device, initialises its input collection,
/// and listens for ASUS-specific hotkey events.
/// </summary>
/// <remarks>
/// This class is intended for long-running background use inside the Windows service.
/// It listens for vendor HID input reports and forwards recognised event IDs to the
/// supplied callback. When service shutdown is requested, the active HID stream is
/// disposed to unblock the blocking read operation cleanly.
/// </remarks>
public sealed class AsusHidInput
{
    /// <summary>
    /// ASUS USB vendor identifier.
    /// </summary>
    public const int AsusVendorId = 0x0b05;

    /// <summary>
    /// ASUS HID input report identifier used by supported devices.
    /// </summary>
    public const byte InputReportId = 0x5a;

    private const byte IgnoredEventId = 236;
    private static readonly int[] SupportedProductIds =
    {
        0x1a30,
        0x1854,
        0x1869,
        0x1866,
        0x19b6,
        0x1822,
        0x1837,
        0x1854,
        0x184a,
        0x183d,
        0x8502,
        0x1807,
        0x17e0,
        0x18c6,
        0x1abe,
        0x1b4c,
        0x1b6e,
        0x1b2c,
        0x8854
    };

    private static readonly byte[] InitialisationText = Encoding.ASCII.GetBytes("ZASUS Tech.Inc.");

    private readonly ILogger<AsusHidInput> _logger;
    private readonly HardwareOptions _options;

    /// <summary>
    /// Initialises a new instance of the <see cref="AsusHidInput"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and error reporting.</param>
    /// <param name="options">The configured hardware service options.</param>
    public AsusHidInput(
        ILogger<AsusHidInput> logger,
        IOptions<HardwareOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Starts listening for ASUS HID events and invokes the supplied callback for each recognised event.
    /// </summary>
    /// <param name="onEvent">
    /// Callback invoked with the ASUS event ID when a supported input report is received.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to stop listening. When cancellation is requested, the active HID stream is closed
    /// to unblock the blocking read operation.
    /// </param>
    /// <returns>A task that completes when listening stops.</returns>
    public async Task ListenAsync(
        Func<int, Task> onEvent,
        CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            HidStream? stream = null;
            CancellationTokenRegistration stopRegistration = default;

            try
            {
                stream = FindInputStream();
                if (stream is null)
                {
                    _logger.LogWarning(
                        "No ASUS HID input stream found. Retrying in {DelayMs} ms.",
                        _options.RetryDelay);

                    await Task.Delay(_options.RetryDelay, cancellationToken);
                    continue;
                }

                InitialiseInputCollection();

                _logger.LogInformation(
                    "Listening on HID path: {Path}",
                    stream.Device.DevicePath);

                stream.ReadTimeout = Timeout.Infinite;

                // Service stop only signals cancellation. Because HidStream.Read() blocks,
                // the stream must be disposed to force the read to exit.
                stopRegistration = cancellationToken.Register(
                    static state =>
                    {
                        try
                        {
                            ((HidStream)state!).Dispose();
                        }
                        catch
                        {
                        }
                    },
                    stream);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var data = stream.Read();
                    if (!TryGetEventId(data, out var eventId))
                    {
                        continue;
                    }

                    _logger.LogInformation("ASUS HID event: {EventId}", eventId);
                    await onEvent(eventId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("HID stream closed because service stop was requested.");
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogError(ex, "HID listener loop failed. Retrying.");
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
            finally
            {
                stopRegistration.Dispose();
                stream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Finds and opens the first supported ASUS HID input device that exposes the expected feature report.
    /// </summary>
    /// <returns>
    /// An open <see cref="HidStream"/> for a supported device, or <see langword="null"/> if none was found.
    /// </returns>
    private HidStream? FindInputStream()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(AsusVendorId))
        {
            try
            {
                if (!IsSupportedInputDevice(device))
                {
                    continue;
                }

                _logger.LogInformation(
                    "Candidate ASUS HID device: PID={Pid:X} Path={Path}",
                    device.ProductID,
                    device.DevicePath);

                return device.Open();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping HID device PID={Pid:X}", device.ProductID);
            }
        }

        return null;
    }

    /// <summary>
    /// Sends the ASUS initialisation payload to supported HID devices so they begin producing the expected events.
    /// </summary>
    private void InitialiseInputCollection()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(AsusVendorId))
        {
            try
            {
                if (!IsSupportedInputDevice(device))
                {
                    continue;
                }

                using var stream = device.Open();
                var payload = new byte[device.GetMaxFeatureReportLength()];

                Array.Copy(InitialisationText, payload, InitialisationText.Length);
                stream.SetFeature(payload);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Input initialisation failed for PID={Pid:X}", device.ProductID);
            }
        }
    }

    /// <summary>
    /// Determines whether a HID device matches the supported ASUS criteria for this service.
    /// </summary>
    /// <param name="device">The HID device to validate.</param>
    /// <returns><see langword="true"/> when the device is supported; otherwise, <see langword="false"/>.</returns>
    private static bool IsSupportedInputDevice(HidDevice device) =>
        SupportedProductIds.Contains(device.ProductID) &&
        device.CanOpen &&
        device.GetMaxFeatureReportLength() > 0 &&
        device.GetReportDescriptor().TryGetReport(ReportType.Feature, InputReportId, out _);

    /// <summary>
    /// Extracts a supported ASUS event identifier from a HID input report.
    /// </summary>
    /// <param name="data">The raw HID report bytes.</param>
    /// <param name="eventId">Receives the resolved event identifier when successful.</param>
    /// <returns><see langword="true"/> when the report contains a recognised event; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetEventId(byte[] data, out int eventId)
    {
        eventId = default;

        if (data.Length <= 1 || data[0] != InputReportId)
        {
            return false;
        }

        var candidate = data[1];
        if (candidate is 0 or IgnoredEventId)
        {
            return false;
        }

        eventId = candidate;
        return true;
    }
}
