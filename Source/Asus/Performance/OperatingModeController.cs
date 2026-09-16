using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Windows.Power;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Performance;

/// <summary>
/// Controls ASUS performance and GPU modes.
/// </summary>
internal sealed class OperatingModeController
{
    private readonly SemaphoreSlim _modeSwitchLock = new(1, 1);
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly AsusAcpiCapabilities _capabilities;
    private readonly ILogger<OperatingModeController> _logger;

    /// <summary>Initializes a new instance of the <see cref="OperatingModeController"/> class.</summary>
    public OperatingModeController(
        AsusAcpiClientFactory acpiFactory,
        AsusAcpiCapabilities capabilities,
        ILogger<OperatingModeController> logger)
    {
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the last operating-mode pair successfully applied or observed from firmware.</summary>
    public HardwareOperatingMode CurrentMode { get; private set; } = HardwareOperatingMode.Normal;

    /// <summary>Applies the requested performance/GPU pair serially using ASUS ACPI.</summary>
    /// <returns><see langword="true"/> when both parts are applied or already active.</returns>
    public async Task<bool> ApplyAsync(
        HardwareOperatingMode mode,
        CancellationToken cancellationToken = default)
    {
        await _modeSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation(
                "Applying ASUS operating mode {PerformanceMode}/{GpuMode}.",
                mode.Performance,
                mode.Gpu);

            var performanceResult = WritePerformanceMode(mode.Performance);
            var performanceSucceeded = performanceResult == 1;
            if (!performanceSucceeded)
            {
                _logger.LogWarning(
                    "Setting performance mode to {PerformanceMode} returned {Result}.",
                    mode.Performance,
                    performanceResult);
            }
            else
            {
                CurrentMode = CurrentMode with { Performance = mode.Performance };
            }

            var gpuResult = await SetGpuModeAsync(mode.Gpu, cancellationToken).ConfigureAwait(false);
            var gpuSucceeded = gpuResult is GpuModeChangeResult.Changed or GpuModeChangeResult.NoChange;
            if (!gpuSucceeded)
            {
                _logger.LogWarning("Setting GPU mode to {GpuMode} returned {Result}.", mode.Gpu, gpuResult);
            }

            return performanceSucceeded && gpuSucceeded;
        }
        finally
        {
            _modeSwitchLock.Release();
        }
    }

    private int ReadGpuEcoFlag()
    {
        var endpoint = _capabilities.GetGpuEcoEndpoint();
        if (!endpoint.HasValue)
        {
            return -1;
        }

        using var acpi = _acpiFactory.Open();
        return acpi.IsConnected ? acpi.ReadDeviceValue(endpoint.Value, "GpuEco") : -1;
    }

    private int ReadGpuMuxFlag()
    {
        var endpoint = _capabilities.GetGpuMuxEndpoint();
        if (!endpoint.HasValue)
        {
            return -1;
        }

        using var acpi = _acpiFactory.Open();
        return acpi.IsConnected ? acpi.ReadDeviceValue(endpoint.Value, "GpuMux") : -1;
    }

    private int WriteGpuEcoFlag(int ecoFlag)
    {
        var endpoint = _capabilities.GetGpuEcoEndpoint();
        if (!endpoint.HasValue)
        {
            return -1;
        }

        using var acpi = _acpiFactory.Open();
        return acpi.IsConnected ? acpi.WriteDeviceValue(endpoint.Value, ecoFlag, "GpuEco") : -1;
    }

    private int WritePerformanceMode(PerformanceMode mode)
    {
        using var acpi = _acpiFactory.Open();
        if (!acpi.IsConnected)
        {
            return -1;
        }

        var endpoint = _capabilities.GetPerformanceModeEndpoint();
        if (endpoint.HasValue)
        {
            return acpi.WriteDeviceValue(
                endpoint.Value.DeviceId,
                ToFirmwarePerformanceMode(mode, endpoint.Value.SwapsTurboAndSilent),
                endpoint.Value.DiagnosticName);
        }

        // Preserve the original ROG write path for firmware that does not expose readable capability state.
        var result = acpi.WriteDeviceValue(
            AsusAcpiDeviceIds.PerformanceMode,
            (int)mode,
            nameof(PerformanceMode));
        if (result == 1)
        {
            return result;
        }

        return acpi.WriteDeviceValue(
            AsusAcpiDeviceIds.PerformanceModeVivoBook,
            ToFirmwarePerformanceMode(mode, swapsTurboAndSilent: true),
            "VivoBookPerformanceMode");
    }

    private static int ToFirmwarePerformanceMode(PerformanceMode mode, bool swapsTurboAndSilent)
    {
        if (!swapsTurboAndSilent)
        {
            return (int)mode;
        }

        return mode switch
        {
            PerformanceMode.Turbo => (int)PerformanceMode.Silent,
            PerformanceMode.Silent => (int)PerformanceMode.Turbo,
            _ => (int)mode,
        };
    }

    private async Task<GpuModeChangeResult> SetGpuModeAsync(GpuMode targetMode, CancellationToken cancellationToken)
    {
        if (!HasGpuModeSupport())
        {
            return GpuModeChangeResult.Unsupported;
        }

        var currentMode = RefreshGpuMode();
        if (currentMode == targetMode)
        {
            return GpuModeChangeResult.NoChange;
        }

        if (targetMode == GpuMode.Eco)
        {
            if (IsExternalGpuConnected())
            {
                return GpuModeChangeResult.Blocked;
            }

            if (!await ApplyEcoModeTransitionAsync(enableEcoMode: true, cancellationToken).ConfigureAwait(false))
            {
                return GpuModeChangeResult.Failed;
            }
        }
        else if (!await ApplyEcoModeTransitionAsync(enableEcoMode: false, cancellationToken).ConfigureAwait(false))
        {
            return GpuModeChangeResult.Failed;
        }

        var observedMode = RefreshGpuMode();
        return observedMode == targetMode ? GpuModeChangeResult.Changed : GpuModeChangeResult.Failed;
    }

    private GpuMode RefreshGpuMode()
    {
        if (!HasGpuModeSupport())
        {
            CurrentMode = CurrentMode with { Gpu = GpuMode.Standard };
            return CurrentMode.Gpu;
        }

        var ecoFlag = ReadGpuEcoFlag();
        _ = ReadGpuMuxFlag(); // Preserve original probe/logging behavior; MUX is not changed by this service.
        CurrentMode = CurrentMode with { Gpu = ecoFlag == 1 ? GpuMode.Eco : GpuMode.Standard };
        return CurrentMode.Gpu;
    }

    private bool HasGpuModeSupport() => _capabilities.GetGpuEcoEndpoint().HasValue;

    // eGPU detection is not currently implemented.
    private static bool IsExternalGpuConnected() => false;

    private async Task<bool> ApplyEcoModeTransitionAsync(bool enableEcoMode, CancellationToken cancellationToken)
    {
        if (enableEcoMode)
        {
            _logger.LogInformation("Preparing to enable Eco GPU mode.");
        }

        var result = WriteGpuEcoFlag(enableEcoMode ? 1 : 0);
        if (result != 1)
        {
            _logger.LogWarning(
                "Setting GPU Eco flag to {EcoFlag} returned {Result}.",
                enableEcoMode ? 1 : 0,
                result);
            return false;
        }

        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        if (!enableEcoMode)
        {
            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GPU Eco mode disabled. AC power connected: {IsPluggedIn}.",
                PowerStatus.IsOnAcPower());
        }

        return true;
    }

    private enum GpuModeChangeResult
    {
        NoChange,
        Changed,
        Blocked,
        Failed,
        Unsupported,
    }
}
