using System.Collections.Frozen;
using System.Text;
using HidSharp;
using HidSharp.Reports;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;
/// <summary>
/// Listens for ASUS specific HID hotkey events.
/// </summary>
/// <remarks>
/// The listener discovers a supported ASUS HID device, sends the vendor initialisation payload, and
/// forwards recognised event IDs to a callback supplied by the service worker.
/// </remarks>
public sealed class AsusHidInput
{
    /// <summary>
    /// ASUS USB vendor identifier.
    /// </summary>
    public const int AsusVendorId = 0x0b05;
    /// <summary>
    /// ASUS HID report identifier used by supported hotkey devices.
    /// </summary>
    public const byte InputReportId = 0x5a;
    private const byte IgnoredEventId = 236;
    private static readonly FrozenSet<int> SupportedProductIds = new[]
    {
        0x1a30,
        0x1854,
        0x1869,
        0x1866,
        0x19b6,
        0x1822,
        0x1837,
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
        0x8854,
    }.ToFrozenSet();
    private static readonly byte[] InitialisationPayload = Encoding.ASCII.GetBytes("ZASUS Tech.Inc.");

    private readonly ILogger<AsusHidInput> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    /// <summary>
    /// Initialises a new instance of the <see cref="AsusHidInput"/> class.
    /// </summary>
    public AsusHidInput(ILogger<AsusHidInput> logger, IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Starts listening for HID events and invokes <paramref name="onEvent"/> for each recognised ASUS event.
    /// </summary>
    public async Task ListenAsync(Func<int, Task> onEvent, CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            HidStream? inputStream = null;
            CancellationTokenRegistration cancellationRegistration = default;
            try
            {
                inputStream = OpenSupportedInputStream();
                if (inputStream is null)
                {
                    _logger.LogWarning("No ASUS HID input stream found. Retrying in {DelayMs} ms.", _options.CurrentValue.RetryDelay);
                    await Task.Delay(_options.CurrentValue.RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                InitialiseSupportedDevices();
                _logger.LogInformation("Listening on HID path: {Path}", inputStream.Device.DevicePath);
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
                        }
                    },
                    inputStream);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var report = inputStream.Read();
                    if (!TryGetEventId(report, out var eventId))
                    {
                        continue;
                    }
                    _logger.LogInformation("ASUS HID event: {EventId}", eventId);
                    await onEvent(eventId).ConfigureAwait(false);
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
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                _logger.LogError(exception, "HID listener loop failed. Retrying.");
                await Task.Delay(_options.CurrentValue.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cancellationRegistration.Dispose();
                inputStream?.Dispose();
            }
        }
    }
    /// <summary>
    /// Sets the ASUS keyboard backlight brightness through the vendor HID feature report.
    /// </summary>
    /// <param name="level">The keyboard backlight level from <c>0</c> (off) through <c>3</c> (maximum).</param>
    /// <returns><c>true</c> when the command was sent to at least one supported ASUS HID device.</returns>
    public bool TrySetKeyboardBacklight(int level)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        byte[] command = [InputReportId, 0xBA, 0xC5, 0xC4, (byte)level];
        var sent = false;

        foreach (var device in DeviceList.Local.GetHidDevices(AsusVendorId))
        {
            try
            {
                if (!IsSupportedInputDevice(device))
                {
                    continue;
                }

                var reportLength = device.GetMaxFeatureReportLength();
                if (reportLength < command.Length)
                {
                    _logger.LogDebug(
                        "Skipping ASUS HID device PID={Pid:X}: feature report length {Length} is too small for keyboard backlight command.",
                        device.ProductID,
                        reportLength);
                    continue;
                }

                using var stream = device.Open();
                var featureBuffer = new byte[reportLength];
                Array.Copy(command, featureBuffer, command.Length);
                stream.SetFeature(featureBuffer);

                _logger.LogInformation(
                    "Keyboard backlight set to level {Level} through ASUS HID device PID={Pid:X}.",
                    level,
                    device.ProductID);
                sent = true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "Keyboard backlight command failed for ASUS HID device PID={Pid:X}.",
                    device.ProductID);
            }
        }

        return sent;
    }
    /// <summary>
    /// Opens the first supported ASUS HID input stream.
    /// </summary>
    private HidStream? OpenSupportedInputStream()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(AsusVendorId))
        {
            try
            {
                if (!IsSupportedInputDevice(device))
                {
                    continue;
                }
                _logger.LogInformation("Candidate ASUS HID device: PID={Pid:X} Path={Path}", device.ProductID, device.DevicePath);
                return device.Open();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Skipping HID device PID={Pid:X}", device.ProductID);
            }
        }

        return null;
    }
    /// <summary>
    /// Sends the ASUS initialisation payload to all supported devices.
    /// </summary>
    private void InitialiseSupportedDevices()
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
                var featureBuffer = new byte[device.GetMaxFeatureReportLength()];
                Array.Copy(InitialisationPayload, featureBuffer, InitialisationPayload.Length);
                stream.SetFeature(featureBuffer);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Input initialisation failed for PID={Pid:X}", device.ProductID);
            }
        }
    }
    /// <summary>
    /// Returns whether the supplied HID device matches the requirements for this listener.
    /// </summary>
    private static bool IsSupportedInputDevice(HidDevice device) =>
        SupportedProductIds.Contains(device.ProductID) &&
        device.CanOpen &&
        device.GetMaxFeatureReportLength() > 0 &&
        device.GetReportDescriptor().TryGetReport(ReportType.Feature, InputReportId, out _);
    /// <summary>
    /// Tries to extract an ASUS hotkey event identifier from a raw report.
    /// </summary>
    private static bool TryGetEventId(byte[] report, out int eventId)
    {
        eventId = default;

        if (report.Length <= 1 || report[0] != InputReportId)
        {
            return false;
        }

        var candidateEventId = report[1];
        if (candidateEventId is 0 or IgnoredEventId)
        {
            return false;
        }
        eventId = candidateEventId;
        return true;
    }
}
