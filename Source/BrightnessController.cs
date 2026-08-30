using System.Management;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;
/// <summary>
/// Provides access to the built-in laptop panel brightness through Windows Management Instrumentation (WMI).
/// </summary>
/// <remarks>
/// This class reads the current brightness from <c>WmiMonitorBrightness</c> and applies changes through
/// <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c>. It is intended for the internal laptop display and
/// typically does not affect external monitors.
/// </remarks>
public sealed class BrightnessController
{
    private const string WmiNamespace = @"\\.\root\wmi";
    private const string BrightnessClassName = "WmiMonitorBrightness";
    private const string BrightnessMethodsClassName = "WmiMonitorBrightnessMethods";
    private const string CurrentBrightnessPropertyName = "CurrentBrightness";
    private const string SetBrightnessMethodName = "WmiSetBrightness";
    private const int TransitionTimeout = 1;
    private readonly ILogger<BrightnessController> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    /// <summary>
    /// Initialises a new instance of the <see cref="BrightnessController"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and status messages.</param>
    /// <param name="options">The configured hardware service options.</param>
    public BrightnessController(
        ILogger<BrightnessController> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Gets the current brightness of the built-in laptop display.
    /// </summary>
    /// <returns>The current brightness as a percentage from 0 to 100.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no WMI brightness instance is available.
    /// </exception>
    public int Get()
    {
        using var brightnessClass = CreateManagementClass(BrightnessClassName);
        using var instances = brightnessClass.GetInstances();
        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                return (byte)instance.GetPropertyValue(CurrentBrightnessPropertyName);
            }
        }

        throw new InvalidOperationException(
            "No WMI monitor brightness instance was found. This only works for the built in laptop panel.");
    }
    /// <summary>
    /// Sets the brightness of the built-in laptop display.
    /// </summary>
    /// <param name="brightness">The requested brightness percentage. Values are clamped to the range 0 to 100.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no WMI brightness method instance is available.
    /// </exception>
    public void Set(int brightness)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        using var methodsClass = CreateManagementClass(BrightnessMethodsClassName);
        using var instances = methodsClass.GetInstances();

        object[] args = [TransitionTimeout, brightness];
        var changed = false;

        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                instance.InvokeMethod(SetBrightnessMethodName, args);
                changed = true;
            }
        }
        if (!changed)
        {
            throw new InvalidOperationException("No WMI monitor brightness method instance was found.");
        }

        _logger.LogInformation("Brightness set to {Brightness}%.", brightness);
    }
    /// <summary>
    /// Adjusts the current brightness by the specified delta.
    /// </summary>
    /// <param name="delta">The amount to add to the current brightness. Negative values reduce brightness.</param>
    public void Adjust(int delta)
    {
        var current = Get();
        var next = Math.Clamp(current + delta, 0, 100);

        if (next == current)
        {
            _logger.LogDebug("Brightness already at boundary: {Brightness}%.", current);
            return;
        }
        Set(next);
    }

    /// <summary>
    /// Increases brightness by the configured step size.
    /// </summary>
    public void Increase()
    {
        Adjust(Math.Abs(_options.CurrentValue.BrightnessStep));
    }

    /// <summary>
    /// Decreases brightness by the configured step size.
    /// </summary>
    public void Decrease()
    {
        Adjust(-Math.Abs(_options.CurrentValue.BrightnessStep));
    }
    /// <summary>
    /// Creates a WMI management class connected to the ASUS brightness namespace.
    /// </summary>
    /// <param name="className">The WMI class name to open.</param>
    /// <returns>An initialised <see cref="ManagementClass"/> instance.</returns>
    private static ManagementClass CreateManagementClass(string className)
    {
        var scope = new ManagementScope(WmiNamespace);
        scope.Connect();
        return new ManagementClass(scope, new ManagementPath(className), null);
    }
}
