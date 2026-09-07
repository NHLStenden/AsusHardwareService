using HidSharp;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Hid;

/// <summary>Writes ASUS keyboard-backlight levels through the vendor HID feature report.</summary>
internal sealed class AsusKeyboardBacklightWriter
{
    private readonly ILogger<AsusKeyboardBacklightWriter> _logger;

    /// <summary>Initializes the ASUS keyboard-backlight HID writer.</summary>
    public AsusKeyboardBacklightWriter(ILogger<AsusKeyboardBacklightWriter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Sends the vendor feature report for a keyboard-backlight level from 0 through 3.</summary>
    /// <returns><see langword="true"/> when at least one supported ASUS HID device accepted the report.</returns>
    public bool TrySetLevel(int level)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        byte[] command = [AsusHidDeviceCatalog.InputReportId, 0xBA, 0xC5, 0xC4, (byte)level];
        var sent = false;
        foreach (var device in AsusHidDeviceCatalog.GetSupportedDevices())
        {
            try
            {
                var reportLength = device.GetMaxFeatureReportLength();
                if (reportLength < command.Length)
                {
                    _logger.LogDebug(
                        "Skipping ASUS HID PID={ProductId:X}: feature report length {Length} is too small.",
                        device.ProductID,
                        reportLength);
                    continue;
                }

                using var stream = device.Open();
                var featureBuffer = new byte[reportLength];
                Array.Copy(command, featureBuffer, command.Length);
                stream.SetFeature(featureBuffer);
                _logger.LogInformation(
                    "Keyboard backlight set to level {Level} through ASUS HID PID={ProductId:X}.",
                    level,
                    device.ProductID);
                sent = true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "Keyboard-backlight HID command failed for ASUS PID={ProductId:X}.",
                    device.ProductID);
            }
        }

        return sent;
    }
}
