using Microsoft.Extensions.Options;

namespace AsusHardwareService;
/// <summary>
/// Reads and adjusts the ASUS keyboard backlight brightness.
/// </summary>
/// <remarks>
/// The current level is read from the standard ASUS keyboard-backlight ACPI state when available.
/// Brightness changes are written through the ASUS vendor HID feature report used by supported ROG keyboards.
/// </remarks>
public sealed class KeyboardBacklightController
{
    private const int MinimumLevel = 0;
    private const int MaximumLevel = 3;
    private const int AcpiPresenceBit = 0x00010000;
    private const int AcpiBrightnessMask = 0x000000FF;
    private readonly ILogger<KeyboardBacklightController> _logger;
    private readonly IServiceProvider _services;
    private readonly AsusHidInput _hid;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    private int? _lastKnownLevel;
    /// <summary>
    /// Initialises a new instance of the <see cref="KeyboardBacklightController"/> class.
    /// </summary>
    /// <param name="logger">The logger used for keyboard backlight diagnostics.</param>
    /// <param name="services">The application service provider, used to resolve a transient <see cref="AsusAcpi"/> instance.</param>
    /// <param name="hid">The ASUS HID interface used to write keyboard backlight commands.</param>
    /// <param name="options">The configured hardware service options.</param>
    public KeyboardBacklightController(
        ILogger<KeyboardBacklightController> logger,
        IServiceProvider services,
        AsusHidInput hid,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _hid = hid ?? throw new ArgumentNullException(nameof(hid));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Decreases the keyboard backlight by one ASUS firmware level.
    /// </summary>
    /// <returns>The resulting level, or <see langword="null"/> if the hardware command failed.</returns>
    public int? Decrease()
    {
        return Adjust(-1);
    }
    /// <summary>
    /// Increases the keyboard backlight by one ASUS firmware level.
    /// </summary>
    /// <returns>The resulting level, or <see langword="null"/> if the hardware command failed.</returns>
    public int? Increase()
    {
        return Adjust(1);
    }
    /// <summary>
    /// Sets an explicit keyboard backlight level.
    /// </summary>
    /// <param name="level">The keyboard backlight level from <c>0</c> (off) through <c>3</c> (maximum).</param>
    /// <returns><c>true</c> when the ASUS HID command was sent successfully.</returns>
    public bool SetLevel(int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Keyboard backlight level must be between 0 and 3.");
        }

        if (!_hid.TrySetKeyboardBacklight(level))
        {
            _logger.LogWarning("Could not set ASUS keyboard backlight to level {Level}.", level);
            return false;
        }

        _lastKnownLevel = level;
        return true;
    }
    /// <summary>
    /// Adjusts the current hardware level by the supplied delta.
    /// </summary>
    /// <param name="delta">The amount to add to the current level.</param>
    /// <returns>The resulting level, or <see langword="null"/> if the hardware command failed.</returns>
    private int? Adjust(int delta)
    {
        var current = GetCurrentLevel();
        var next = Math.Clamp(current + delta, MinimumLevel, MaximumLevel);
        if (next == current)
        {
            _logger.LogDebug("Keyboard backlight already at boundary level {Level}.", current);
            return current;
        }

        return SetLevel(next) ? next : null;
    }
    /// <summary>
    /// Reads the current keyboard backlight level from ASUS ACPI, falling back to the last known or configured level.
    /// </summary>
    /// <returns>The best available keyboard backlight level from <c>0</c> through <c>3</c>.</returns>
    private int GetCurrentLevel()
    {
        try
        {
            using var acpi = _services.GetRequiredService<AsusAcpi>();
            if (acpi.IsConnected)
            {
                var rawState = acpi.GetRawDeviceValue(AsusAcpi.KeyboardBacklight);
                var hardwareLevel = rawState & AcpiBrightnessMask;
                if (rawState >= 0 &&
                    (rawState & AcpiPresenceBit) != 0 &&
                    hardwareLevel is >= MinimumLevel and <= MaximumLevel)
                {
                    _lastKnownLevel = hardwareLevel;
                    return hardwareLevel;
                }

                _logger.LogDebug(
                    "ASUS ACPI keyboard backlight state was not usable. Raw state: 0x{RawState:X8}.",
                    rawState);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not read the current ASUS keyboard backlight level.");
        }

        if (_lastKnownLevel is int lastKnownLevel)
        {
            return lastKnownLevel;
        }

        return Math.Clamp(
            _options.CurrentValue.KeyboardBacklightLevel,
            MinimumLevel,
            MaximumLevel);
    }
}
