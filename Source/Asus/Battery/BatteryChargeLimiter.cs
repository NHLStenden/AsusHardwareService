using System.ComponentModel;
using System.ServiceProcess;
using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Asus.Battery;

/// <summary>Applies the configured ASUS battery charge ceiling through ACPI.</summary>
internal sealed class BatteryChargeLimiter
{
    private static readonly TimeSpan ServiceStateTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<BatteryChargeLimiter> _logger;
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly IOptionsMonitor<HardwareOptions> _options;

    /// <summary>Initializes the battery charge-limit adapter.</summary>
    public BatteryChargeLimiter(
        ILogger<BatteryChargeLimiter> logger,
        AsusAcpiClientFactory acpiFactory,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Starts the ASUS ACPI driver if needed and writes the configured battery charge ceiling.</summary>
    public void ApplyConfiguredLimit()
    {
        try
        {
            var limit = _options.CurrentValue.BatteryChargeLimitPercent;
            if (limit is <= 0 or >= 100)
            {
                _logger.LogError("No valid battery charge limit is configured. Value={Limit}.", limit);
                return;
            }

            EnsureDriverServiceRunning(AsusDriverLocator.DriverServiceName);

            using var acpi = _acpiFactory.Open();
            if (!acpi.IsConnected)
            {
                _logger.LogError(@"Could not connect to \\.\ATKACPI.");
                return;
            }

            _logger.LogInformation("Setting battery charge limit to {Limit}%.", limit);
            var result = acpi.WriteDeviceValue(AsusAcpiDeviceIds.BatteryChargeLimit, limit, "BatteryChargeLimit");
            if (result != 1)
            {
                _logger.LogError("ASUS ACPI battery-limit write returned {Result}.", result);
                return;
            }

            _logger.LogInformation("Battery charge limit set to {Limit}%.", limit);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Fatal error while applying the ASUS battery charge limit.");
        }
    }

    private void EnsureDriverServiceRunning(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        var status = RefreshStatus(controller);
        if (status == ServiceControllerStatus.Running)
        {
            _logger.LogInformation("Required service {ServiceName} is already running.", serviceName);
            return;
        }

        if (status == ServiceControllerStatus.StartPending)
        {
            WaitForStatus(controller, serviceName, ServiceControllerStatus.Running);
            return;
        }

        if (status == ServiceControllerStatus.StopPending)
        {
            WaitForStatus(controller, serviceName, ServiceControllerStatus.Stopped);
            status = RefreshStatus(controller);
        }

        if (status == ServiceControllerStatus.PausePending)
        {
            WaitForStatus(controller, serviceName, ServiceControllerStatus.Paused);
            status = RefreshStatus(controller);
        }

        if (status == ServiceControllerStatus.ContinuePending)
        {
            WaitForStatus(controller, serviceName, ServiceControllerStatus.Running);
            return;
        }

        switch (status)
        {
            case ServiceControllerStatus.Stopped:
                _logger.LogInformation("Starting required service {ServiceName}.", serviceName);
                ControlService(serviceName, controller.Start);
                break;
            case ServiceControllerStatus.Paused:
                if (!controller.CanPauseAndContinue)
                {
                    throw new InvalidOperationException($"Service {serviceName} is paused and cannot be continued.");
                }

                _logger.LogInformation("Continuing required service {ServiceName}.", serviceName);
                ControlService(serviceName, controller.Continue);
                break;
            case ServiceControllerStatus.Running:
                return;
            default:
                throw new InvalidOperationException($"Service {serviceName} is in unexpected state {status}.");
        }

        WaitForStatus(controller, serviceName, ServiceControllerStatus.Running);
    }

    private static ServiceControllerStatus RefreshStatus(ServiceController controller)
    {
        controller.Refresh();
        return controller.Status;
    }

    private static void WaitForStatus(
        ServiceController controller,
        string serviceName,
        ServiceControllerStatus expectedStatus)
    {
        controller.WaitForStatus(expectedStatus, ServiceStateTimeout);
        if (RefreshStatus(controller) != expectedStatus)
        {
            throw new InvalidOperationException($"Service {serviceName} did not reach {expectedStatus} state.");
        }
    }

    private void ControlService(string serviceName, Action action)
    {
        try
        {
            action();
        }
        catch (Win32Exception exception)
        {
            _logger.LogError(exception, "{ServiceName} cannot be controlled.", serviceName);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "{ServiceName} cannot be controlled.", serviceName);
        }
    }
}
