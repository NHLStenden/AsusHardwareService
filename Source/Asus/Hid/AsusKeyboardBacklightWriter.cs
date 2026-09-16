using AsusHardwareService.Asus.Acpi;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Hid;

/// <summary>Writes ASUS keyboard-backlight levels using the supported ASUS hardware backend.</summary>
internal sealed class AsusKeyboardBacklightWriter
{
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly AsusAcpiCapabilities _capabilities;
    private readonly ILogger<AsusKeyboardBacklightWriter> _logger;

    /// <summary>Initializes a new instance of the <see cref="AsusKeyboardBacklightWriter"/> class.</summary>
    public AsusKeyboardBacklightWriter(
        AsusAcpiClientFactory acpiFactory,
        AsusAcpiCapabilities capabilities,
        ILogger<AsusKeyboardBacklightWriter> logger)
    {
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Applies an ASUS keyboard-backlight level from 0 through 3 using the supported hardware backend.</summary>
    /// <returns><see langword="true"/> when an ASUS keyboard-backlight backend accepted the level.</returns>
    public bool TrySetLevel(int level)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        if (_capabilities.IsTufKeyboardBacklightSupported() && TrySetTufAcpiLevel(level))
        {
            return true;
        }

        return TrySetHidLevel(level);
    }

    private bool TrySetHidLevel(int level)
    {
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

    private bool TrySetTufAcpiLevel(int level)
    {
        try
        {
            using var acpi = _acpiFactory.Open();
            if (!acpi.IsConnected)
            {
                return false;
            }

            var result = acpi.WriteDeviceValue(
                AsusAcpiDeviceIds.KeyboardBacklight,
                0x80 | level,
                "TufKeyboardBacklight");
            if (result == 1)
            {
                _logger.LogInformation("Keyboard backlight set to level {Level} through ASUS TUF ACPI.", level);
                return true;
            }

            _logger.LogDebug("ASUS TUF keyboard-backlight write returned {Result}.", result);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "ASUS TUF keyboard-backlight command failed.");
        }

        return false;
    }
}
