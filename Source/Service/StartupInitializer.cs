using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Battery;
using AsusHardwareService.Asus.Keyboard;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Configuration;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Service;

/// <summary>
/// Applies service-owned hardware state that must be restored when the Windows service starts.
/// </summary>
internal sealed class StartupInitializer
{
    private readonly BatteryChargeLimiter _batteryChargeLimit;
    private readonly KeyboardBacklightController _keyboardBacklight;
    private readonly LaptopDisplayController _laptopDisplay;
    private readonly OperatingModeController _performanceMode;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes the startup-state application service.</summary>
    public StartupInitializer(
        BatteryChargeLimiter batteryChargeLimit,
        KeyboardBacklightController keyboardBacklight,
        LaptopDisplayController laptopDisplay,
        OperatingModeController performanceMode,
        IOptionsMonitor<HardwareOptions> options)
    {
        _batteryChargeLimit = batteryChargeLimit ?? throw new ArgumentNullException(nameof(batteryChargeLimit));
        _keyboardBacklight = keyboardBacklight ?? throw new ArgumentNullException(nameof(keyboardBacklight));
        _laptopDisplay = laptopDisplay ?? throw new ArgumentNullException(nameof(laptopDisplay));
        _performanceMode = performanceMode ?? throw new ArgumentNullException(nameof(performanceMode));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Applies the configured battery, keyboard, display, performance, and GPU startup state.</summary>
    /// <param name="cancellationToken">Signals service shutdown.</param>
    /// <returns>A task that completes after the performance/GPU pair has been attempted.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        _batteryChargeLimit.ApplyConfiguredLimit();
        _keyboardBacklight.SetLevel(options.StartupKeyboardBacklightLevel);
        _laptopDisplay.ApplyConfiguredServiceSettings();
        return _performanceMode.ApplyAsync(
            new HardwareOperatingMode(options.StartupPerformanceMode, options.StartupGpuMode),
            cancellationToken);
    }
}
