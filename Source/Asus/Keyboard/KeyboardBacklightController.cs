using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Asus.Hid;
using AsusHardwareService.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Asus.Keyboard;

/// <summary>Reads ASUS keyboard-backlight state from ACPI and writes levels through vendor HID.</summary>
internal sealed class KeyboardBacklightController
{
    private const int MinimumLevel = 0;
    private const int MaximumLevel = 3;
    private const int AcpiPresenceBit = 0x00010000;
    private const int AcpiBrightnessMask = 0x000000FF;

    private readonly ILogger<KeyboardBacklightController> _logger;
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly AsusKeyboardBacklightWriter _writer;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    private int? _lastKnownLevel;

    /// <summary>Initializes the ASUS keyboard-backlight adapter.</summary>
    public KeyboardBacklightController(
        ILogger<KeyboardBacklightController> logger,
        AsusAcpiClientFactory acpiFactory,
        AsusKeyboardBacklightWriter writer,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Decreases the keyboard-backlight level by one step.</summary>
    public int? Decrease() => Adjust(-1);

    /// <summary>Increases the keyboard-backlight level by one step.</summary>
    public int? Increase() => Adjust(1);

    /// <summary>Applies an explicit ASUS keyboard-backlight level from 0 through 3.</summary>
    public bool SetLevel(int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        if (!_writer.TrySetLevel(level))
        {
            _logger.LogWarning("Could not set ASUS keyboard backlight to level {Level}.", level);
            return false;
        }

        _lastKnownLevel = level;
        return true;
    }

    private int? Adjust(int delta)
    {
        var current = GetCurrentLevel();
        var next = Math.Clamp(current + delta, MinimumLevel, MaximumLevel);
        if (next == current)
        {
            _logger.LogDebug("Keyboard backlight is already at boundary level {Level}.", current);
            return current;
        }

        return SetLevel(next) ? next : null;
    }

    private int GetCurrentLevel()
    {
        try
        {
            using var acpi = _acpiFactory.Open();
            if (acpi.IsConnected)
            {
                var rawState = acpi.ReadRawDeviceValue(AsusAcpiDeviceIds.KeyboardBacklight, "KeyboardBacklight");
                var hardwareLevel = rawState & AcpiBrightnessMask;
                if (rawState >= 0 &&
                    (rawState & AcpiPresenceBit) != 0 &&
                    hardwareLevel is >= MinimumLevel and <= MaximumLevel)
                {
                    _lastKnownLevel = hardwareLevel;
                    return hardwareLevel;
                }

                _logger.LogDebug("ASUS keyboard-backlight ACPI state was unusable: 0x{RawState:X8}.", rawState);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not read the current ASUS keyboard-backlight level.");
        }

        return _lastKnownLevel ?? Math.Clamp(
            _options.CurrentValue.StartupKeyboardBacklightLevel,
            MinimumLevel,
            MaximumLevel);
    }
}
