using System.ComponentModel;
using System.ServiceProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Applies the configured ASUS battery charge limit through the ASUS ACPI interface.
/// </summary>
/// <remarks>
/// This controller ensures that the required ASUS driver service is running, opens the
/// ASUS ACPI device through <see cref="AsusAcpi"/>, and applies the configured battery
/// charge limit. It is intended to run inside the Windows service at startup.
/// </remarks>
public sealed class ChargeController
{
    private readonly ILogger<ChargeController> _logger;
    private readonly IServiceProvider _services;
    private readonly HardwareOptions _options;

    /// <summary>
    /// Initialises a new instance of the <see cref="ChargeController"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and status messages.</param>
    /// <param name="services">The application service provider, used to resolve a transient <see cref="AsusAcpi"/> instance.</param>
    /// <param name="options">The configured hardware service options.</param>
    public ChargeController(
        ILogger<ChargeController> logger,
        IServiceProvider services,
        IOptions<HardwareOptions> options)
    {
        _logger = logger;
        _services = services;
        _options = options.Value;
    }

    /// <summary>
    /// Applies the configured battery charge limit.
    /// </summary>
    /// <remarks>
    /// If the configured charge limit is outside the supported range, the method logs an
    /// error and returns. If the ASUS ACPI service or device path is unavailable, the
    /// method also logs the problem and returns without throwing.
    /// </remarks>
    public void SetLimit()
    {
        try
        {
            int limit = _options.ChargeLimit;

            if (limit <= 0 || limit >= 100)
            {
                _logger.LogError(
                    "No valid charge limit configured. Value: {Limit}",
                    limit);

                return;
            }

            EnsureServiceRunning("ATKWMIACPIIO");

            using var acpi = _services.GetRequiredService<AsusAcpi>();

            if (!acpi.IsConnected)
            {
                _logger.LogError(@"Could not connect to \\.\ATKACPI.");
                return;
            }

            _logger.LogInformation(
                "Setting battery charge limit to {Limit}.",
                limit);

            int result = acpi.DeviceSet(AsusAcpi.BatteryLimit, limit, "Limit");
            if (result != 1)
            {
                _logger.LogError(
                    "ACPI call returned {Result}.",
                    result);

                return;
            }

            _logger.LogInformation(
                "Battery charge limit set to {Limit}%.",
                limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fatal error while setting battery charge limit.");
        }
    }

    /// <summary>
    /// Ensures that the specified Windows service is running.
    /// </summary>
    /// <param name="serviceName">The name of the Windows service to verify and start or continue if needed.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the service is in an unexpected state or does not reach the running state.
    /// </exception>
    private void EnsureServiceRunning(string serviceName)
    {
        using var controller = new ServiceController(serviceName);

        ServiceControllerStatus GetStatus()
        {
            controller.Refresh();
            return controller.Status;
        }

        var status = GetStatus();

        if (status == ServiceControllerStatus.Running)
        {
            _logger.LogInformation(
                "Required service {ServiceName} is already running.",
                serviceName);

            return;
        }

        if (status == ServiceControllerStatus.StartPending)
        {
            _logger.LogInformation(
                "Required service {ServiceName} is already starting.",
                serviceName);

            controller.WaitForStatus(
                ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(30));

            status = GetStatus();

            if (status != ServiceControllerStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Service {serviceName} did not reach Running state.");
            }

            return;
        }

        if (status == ServiceControllerStatus.StopPending)
        {
            _logger.LogInformation(
                "Waiting for service {ServiceName} to stop before starting it.",
                serviceName);

            controller.WaitForStatus(
                ServiceControllerStatus.Stopped,
                TimeSpan.FromSeconds(30));

            status = GetStatus();
        }

        if (status == ServiceControllerStatus.PausePending)
        {
            _logger.LogInformation(
                "Waiting for service {ServiceName} to finish pausing.",
                serviceName);

            controller.WaitForStatus(
                ServiceControllerStatus.Paused,
                TimeSpan.FromSeconds(30));

            status = GetStatus();
        }

        if (status == ServiceControllerStatus.ContinuePending)
        {
            _logger.LogInformation(
                "Waiting for service {ServiceName} to resume.",
                serviceName);

            controller.WaitForStatus(
                ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(30));

            status = GetStatus();

            if (status != ServiceControllerStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Service {serviceName} did not reach Running state.");
            }

            return;
        }

        switch (status)
        {
            case ServiceControllerStatus.Stopped:
                _logger.LogInformation(
                    "Starting required service {ServiceName}.",
                    serviceName);

                try
                {
                    controller.Start();
                }
                catch (Win32Exception ex)
                {
                    _logger.LogError(ex, "{ServiceName} cannot be started.", serviceName);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "{ServiceName} cannot be started.", serviceName);
                }
                break;

            case ServiceControllerStatus.Paused:
                if (!controller.CanPauseAndContinue)
                {
                    throw new InvalidOperationException(
                        $"Service {serviceName} is paused and cannot be continued.");
                }

                _logger.LogInformation(
                    "Continuing required service {ServiceName}.",
                    serviceName);

                try
                {
                    controller.Continue();
                }
                catch (Win32Exception ex)
                {
                    _logger.LogError(ex, "{ServiceName} cannot be continued.", serviceName);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "{ServiceName} cannot be continued.", serviceName);
                }
                break;

            case ServiceControllerStatus.Running:
                return;

            default:
                throw new InvalidOperationException(
                    $"Service {serviceName} is in unexpected state {status}.");
        }

        controller.WaitForStatus(
            ServiceControllerStatus.Running,
            TimeSpan.FromSeconds(30));

        status = GetStatus();

        if (status != ServiceControllerStatus.Running)
        {
            throw new InvalidOperationException(
                $"Service {serviceName} did not reach Running state.");
        }
    }
}