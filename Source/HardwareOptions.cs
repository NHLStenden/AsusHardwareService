namespace AsusHardwareService;

/// <summary>
/// ASUS performance modes supported by this service.
/// </summary>
public enum PerformanceMode
{
    /// <summary>
    /// Uses the balanced ASUS performance profile.
    /// </summary>
    Balanced = 0,

    /// <summary>
    /// Uses the silent ASUS performance profile.
    /// </summary>
    Silent = 2,
}
/// <summary>
/// ASUS GPU modes supported by this service.
/// </summary>
public enum GpuMode
{
    /// <summary>
    /// Uses the integrated GPU focused Eco mode.
    /// </summary>
    Eco = 0,

    /// <summary>
    /// Uses the standard hybrid GPU mode.
    /// </summary>
    Standard = 1,
}
/// <summary>
/// Represents configurable settings for the ASUS hardware service.
/// </summary>
/// <remarks>
/// These options control the hardware state applied at startup and on relevant user-session changes.
/// </remarks>
public sealed class HardwareOptions
{
    /// <summary>
    /// Gets or sets the battery charge limit percentage to apply through the ASUS ACPI interface.
    /// </summary>
    public int ChargeLimit { get; set; } = 60;
    /// <summary>
    /// Gets or sets the brightness step size, in percentage points.
    /// </summary>
    public int BrightnessStep { get; set; } = 10;

    /// <summary>
    /// Gets or sets the fallback keyboard backlight level used when the current hardware level cannot be read.
    /// </summary>
    /// <remarks>
    /// ASUS keyboard backlight levels range from <c>0</c> (off) to <c>3</c> (maximum).
    /// </remarks>
    public int KeyboardBacklightFallbackLevel { get; set; } = 1;

    /// <summary>
    /// Gets or sets the delay, in milliseconds, before retrying ASUS HID device discovery after a failure.
    /// </summary>
    public int RetryDelay { get; set; } = 1500;
    /// <summary>
    /// Gets or sets the laptop panel refresh-rate and overdrive preset to apply.
    /// </summary>
    public LaptopScreenMode LaptopScreenMode { get; set; } = LaptopScreenMode.Auto;

    /// <summary>
    /// Gets or sets the MiniLED backlight zone mode to apply.
    /// </summary>
    public MiniLedMode MiniLedMode { get; set; } = MiniLedMode.MultiZone;
    /// <summary>
    /// Gets or sets a value indicating whether brightness changes should be broadcast to a per-user helper.
    /// </summary>
    public bool BroadcastBrightnessChanges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the colour profile should be reset to default.
    /// </summary>
    public bool ColorProfileToDefault { get; set; }
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
    /// Valid values typically follow the scale: <c>0</c>, <c>15</c>, <c>30</c>, <c>50</c>,
    /// <c>70</c>, <c>85</c>, and <c>100</c>. A value of <c>50</c> is neutral.
    /// </remarks>
    public int ColorTemperature { get; set; } = 50;
    /// <summary>
    /// Gets or sets the delay, in milliseconds, before applying the colour profile after a session change.
    /// </summary>
    public int ColorProfileDelay { get; set; } = 8000;

    /// <summary>
    /// Gets or sets the delay, in milliseconds, before calling the Splendid command.
    /// </summary>
    public int ColorProfileCommandDelay { get; set; } = 1000;
    /// <summary>
    /// Gets or sets the configured performance mode.
    /// </summary>
    public PerformanceMode PerformanceMode { get; set; } = PerformanceMode.Silent;

    /// <summary>
    /// Gets or sets the configured GPU mode.
    /// </summary>
    public GpuMode GpuMode { get; set; } = GpuMode.Eco;
}
