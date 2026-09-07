using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Asus.Performance;
using Microsoft.Extensions.Configuration;

namespace AsusHardwareService.Configuration;

/// <summary>
/// Configurable startup state and timing values for the ASUS hardware service.
/// </summary>
internal sealed class HardwareOptions
{
    /// <summary>Configuration section name used by the host.</summary>
    public const string SectionName = "Hardware";

    /// <summary>Gets or sets the battery charge ceiling as a percentage.</summary>
    [ConfigurationKeyName("ChargeLimit")]
    public int BatteryChargeLimitPercent { get; set; } = 60;

    /// <summary>Gets or sets the display-brightness step in percentage points.</summary>
    [ConfigurationKeyName("BrightnessStep")]
    public int DisplayBrightnessStepPercent { get; set; } = 10;

    /// <summary>Gets or sets the keyboard-backlight level applied when the service starts.</summary>
    [ConfigurationKeyName("KeyboardBacklightLevel")]
    public int StartupKeyboardBacklightLevel { get; set; } = 2;

    /// <summary>Gets or sets the retry delay after ASUS HID discovery/read failures, in milliseconds.</summary>
    [ConfigurationKeyName("RetryDelay")]
    public int HidRetryDelayMilliseconds { get; set; } = 1500;

    /// <summary>Gets or sets the built-in panel refresh-rate/overdrive preset.</summary>
    [ConfigurationKeyName("LaptopScreenMode")]
    public LaptopDisplayMode LaptopDisplayMode { get; set; } = LaptopDisplayMode.Auto;

    /// <summary>
    /// Gets or sets the legacy brightness-broadcast option from the original configuration contract.
    /// </summary>
    /// <remarks>
    /// The original application defines this option but does not consume it. It is retained so existing
    /// configuration files and future behavior do not silently lose a setting during the refactor.
    /// </remarks>
    public bool BroadcastBrightnessChanges { get; set; } = true;

    /// <summary>Gets or sets the MiniLED local-dimming mode.</summary>
    public MiniLedMode MiniLedMode { get; set; } = MiniLedMode.MultiZone;

    /// <summary>Gets or sets whether ASUS Splendid should first be reset to its default state.</summary>
    [ConfigurationKeyName("ColorProfileToDefault")]
    public bool ResetColorProfileBeforeApply { get; set; }

    /// <summary>Gets or sets the ASUS Splendid visual preset.</summary>
    [ConfigurationKeyName("VisualMode")]
    public SplendidVisualMode SplendidVisualMode { get; set; } = SplendidVisualMode.Default;

    /// <summary>Gets or sets the ASUS Splendid gamut preset.</summary>
    [ConfigurationKeyName("GamutMode")]
    public SplendidGamutMode SplendidGamutMode { get; set; } = SplendidGamutMode.Native;

    /// <summary>Gets or sets the ASUS Splendid color-temperature value.</summary>
    public int ColorTemperature { get; set; } = 50;

    /// <summary>Gets or sets the delay before applying a color profile after a user session becomes active.</summary>
    [ConfigurationKeyName("ColorProfileDelay")]
    public int ColorProfileSessionDelayMilliseconds { get; set; } = 8000;

    /// <summary>Gets or sets the delay between ASUS Splendid commands.</summary>
    [ConfigurationKeyName("ColorProfileCommandDelay")]
    public int ColorProfileCommandDelayMilliseconds { get; set; } = 1000;

    /// <summary>Gets or sets the performance profile applied when the service starts.</summary>
    [ConfigurationKeyName("PerformanceMode")]
    public PerformanceMode StartupPerformanceMode { get; set; } = PerformanceMode.Silent;

    /// <summary>Gets or sets the GPU mode applied when the service starts.</summary>
    [ConfigurationKeyName("GpuMode")]
    public GpuMode StartupGpuMode { get; set; } = GpuMode.Eco;
}
