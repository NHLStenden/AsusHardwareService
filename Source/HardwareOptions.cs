namespace AsusHardwareService;

/// <summary>
/// Represents configurable settings for the ASUS hardware service.
/// </summary>
/// <remarks>
/// These options control the battery charge limit applied at startup, the brightness step size
/// used for supported hotkey events, retry timing for HID device discovery, and optional behaviour
/// for model-specific event handling and interactive-session brightness notifications.
/// </remarks>
public sealed class HardwareOptions
{
    /// <summary>
    /// Gets or sets the battery charge limit percentage to apply through the ASUS ACPI interface.
    /// </summary>
    /// <value>A percentage value typically between 1 and 99.</value>
    public int ChargeLimit { get; set; } = 60;

    /// <summary>
    /// Gets or sets the brightness step size, in percentage points, used when increasing or decreasing brightness.
    /// </summary>
    public int BrightnessStep { get; set; } = 10;

    /// <summary>
    /// Gets or sets the delay, in milliseconds, before retrying ASUS HID device discovery after a failure.
    /// </summary>
    public int RetryDelay { get; set; } = 1500;

    /// <summary>
    /// Gets or sets a value indicating whether brightness changes should be broadcast to a per-user helper.
    /// </summary>
    /// <remarks>
    /// When enabled, the service can publish brightness updates over an inter-process channel such as a named pipe,
    /// allowing a helper application in the interactive user session to show a toast or custom on-screen display.
    /// </remarks>
    public bool BroadcastBrightnessChanges { get; set; } = true;

    /// <summary>
    /// Gets or sets the visual mode to apply.
    /// </summary>
    public SplendidVisual VisualMode { get; set; } = SplendidVisual.Default;

    /// <summary>
    /// Gets or sets the gamut mode to apply.
    /// </summary>
    public SplendidGamut GamutMode { get; set; } = SplendidGamut.Native;

    /// <summary>
    /// Gets or sets the colour temperature to apply.
    /// </summary>
    /// <remarks>
    /// Valid values typically follow the GHelper scale:
    /// 0, 15, 30, 50, 70, 85, 100.
    /// A value of 50 is neutral.
    /// </remarks>
    public int ColorTemperature { get; set; } = 50;

    /// <summary>
    /// Gets or sets the delay, in milliseconds, before applying the color profile.
    /// </summary>
    public int ColorProfileDelay { get; set; } = 8000;
    /// <summary>
    /// Gets or sets the delay, in milliseconds, before calling the Splendid.exe command.
    /// </summary>
    public int ColorProfileCommandDelay { get; set; } = 1000;
}