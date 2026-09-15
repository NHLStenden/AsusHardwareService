using System.Management;
using AsusHardwareService.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Windows.Display;

/// <summary>Controls built-in panel brightness using WMI.</summary>
internal sealed class DisplayBrightnessController
{
    private const string WmiNamespace = @"\\.\root\wmi";
    private const string BrightnessClassName = "WmiMonitorBrightness";
    private const string BrightnessMethodsClassName = "WmiMonitorBrightnessMethods";
    private const string CurrentBrightnessPropertyName = "CurrentBrightness";
    private const string SetBrightnessMethodName = "WmiSetBrightness";
    private const int TransitionTimeout = 1;

    private readonly ILogger<DisplayBrightnessController> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="DisplayBrightnessController"/> class.</summary>
    public DisplayBrightnessController(
        ILogger<DisplayBrightnessController> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Increases built-in display brightness by the brightness step.</summary>
    public int Increase() => Adjust(Math.Abs(_options.CurrentValue.DisplayBrightnessStepPercent));

    /// <summary>Decreases built-in display brightness by the brightness step.</summary>
    public int Decrease() => Adjust(-Math.Abs(_options.CurrentValue.DisplayBrightnessStepPercent));

    private int Adjust(int delta)
    {
        var current = GetBrightness();
        var next = Math.Clamp(current + delta, 0, 100);
        if (next == current)
        {
            _logger.LogDebug("Display brightness is already at boundary {Brightness}%.", current);
            return current;
        }

        SetBrightness(next);
        return next;
    }

    private int GetBrightness()
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

        throw new InvalidOperationException("No WMI monitor-brightness instance was found for the built-in laptop panel.");
    }

    private void SetBrightness(int brightness)
    {
        using var methodsClass = CreateManagementClass(BrightnessMethodsClassName);
        using var instances = methodsClass.GetInstances();
        object[] arguments = [TransitionTimeout, brightness];
        var changed = false;

        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                instance.InvokeMethod(SetBrightnessMethodName, arguments);
                changed = true;
            }
        }

        if (!changed)
        {
            throw new InvalidOperationException("No WMI monitor-brightness method instance was found.");
        }

        _logger.LogInformation("Display brightness set to {Brightness}%.", brightness);
    }

    private static ManagementClass CreateManagementClass(string className)
    {
        var scope = new ManagementScope(WmiNamespace);
        scope.Connect();
        return new ManagementClass(scope, new ManagementPath(className), null);
    }
}
