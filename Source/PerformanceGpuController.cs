using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Describes the outcome of a GPU mode change request.
/// </summary>
public enum GpuChangeResult
{
    /// <summary>
    /// The requested GPU mode was already active.
    /// </summary>
    NoChange,

    /// <summary>
    /// The GPU mode changed successfully.
    /// </summary>
    Changed,
    /// <summary>
    /// The GPU mode changed, but a restart is required before it takes full effect.
    /// </summary>
    RestartRequired,

    /// <summary>
    /// The change was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The change was blocked by a runtime condition.
    /// </summary>
    Blocked,

    /// <summary>
    /// The GPU mode command failed or could not be verified.
    /// </summary>
    Failed,

    /// <summary>
    /// GPU mode control is not supported on the current device.
    /// </summary>
    Unsupported,
}
/// <summary>
/// Applies and tracks the combined ASUS performance mode and GPU mode.
/// </summary>
/// <remarks>
/// This controller wraps the low level <see cref="AsusAcpi"/> access and exposes the service-side
/// policy used for switching between the supported Silent/Eco and Balanced/Standard combinations.
/// </remarks>
public sealed class PerformanceGpuController
{
    private readonly SemaphoreSlim _modeSwitchLock = new(1, 1);
    private readonly IServiceProvider _services;
    private readonly ILogger<PerformanceGpuController> _logger;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    /// <summary>
    /// Initialises a new instance of the <see cref="PerformanceGpuController"/> class.
    /// </summary>
    public PerformanceGpuController(
        IServiceProvider services,
        ILogger<PerformanceGpuController> logger,
        IOptionsMonitor<HardwareOptions> options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    /// <summary>
    /// Gets the last performance mode applied or observed by the service.
    /// </summary>
    public PerformanceMode CurrentPerformanceMode { get; private set; } = PerformanceMode.Balanced;

    /// <summary>
    /// Gets the last GPU mode applied or observed by the service.
    /// </summary>
    public GpuMode CurrentGpuMode { get; private set; } = GpuMode.Standard;
    /// <summary>
    /// Returns the currently tracked performance and GPU mode pair.
    /// </summary>
    public (PerformanceMode performanceMode, GpuMode gpuMode) GetCurrentCombinedMode() =>
        (CurrentPerformanceMode, CurrentGpuMode);
    /// <summary>
    /// Returns the next mode pair in the service toggle cycle.
    /// </summary>
    public static (PerformanceMode performanceMode, GpuMode gpuMode) GetNextCombinedMode(
        PerformanceMode performanceMode,
        GpuMode gpuMode) =>
        performanceMode == PerformanceMode.Silent && gpuMode == GpuMode.Eco
            ? (PerformanceMode.Balanced, GpuMode.Standard)
            : (PerformanceMode.Silent, GpuMode.Eco);
    /// <summary>
    /// Switches to the next combined performance and GPU mode pair.
    /// </summary>
    /// <returns>The resulting tracked mode pair, or <see langword="null"/> when the complete mode change failed.</returns>
    public async Task<(PerformanceMode performanceMode, GpuMode gpuMode)?> ToggleCombinedModeAsync(
        CancellationToken cancellationToken = default)
    {
        var currentMode = GetCurrentCombinedMode();
        var nextMode = GetNextCombinedMode(currentMode.performanceMode, currentMode.gpuMode);

        var changed = await ApplyCombinedModeAsync(
            nextMode.performanceMode,
            nextMode.gpuMode,
            cancellationToken).ConfigureAwait(false);
        return changed ? GetCurrentCombinedMode() : null;
    }
    /// <summary>
    /// Applies a combined performance and GPU mode pair.
    /// </summary>
    /// <returns><see langword="true"/> when both requested modes were applied or already active.</returns>
    public async Task<bool> ApplyCombinedModeAsync(
        PerformanceMode performanceMode,
        GpuMode gpuMode,
        CancellationToken cancellationToken = default)
    {
        await _modeSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation(
                "Applying combined mode {PerformanceMode}/{GpuMode}.",
                performanceMode,
                gpuMode);
            var performanceResult = SetPerformanceMode(performanceMode);
            var performanceSucceeded = performanceResult == 1;
            if (!performanceSucceeded)
            {
                _logger.LogWarning(
                    "Setting performance mode to {PerformanceMode} returned {Result}.",
                    performanceMode,
                    performanceResult);
            }
            else
            {
                CurrentPerformanceMode = performanceMode;
            }

            var gpuResult = await SetGpuModeAsync(gpuMode, cancellationToken).ConfigureAwait(false);
            var gpuSucceeded = gpuResult is GpuChangeResult.Changed or GpuChangeResult.NoChange;
            if (!gpuSucceeded)
            {
                _logger.LogWarning(
                    "Setting GPU mode to {GpuMode} returned {Result}.",
                    gpuMode,
                    gpuResult);
            }

            return performanceSucceeded && gpuSucceeded;
        }
        finally
        {
            _modeSwitchLock.Release();
        }
    }

    /// <summary>
    /// Reads the raw GPU Eco flag from ASUS ACPI.
    /// </summary>
    private int ReadGpuEcoFlag()
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.GetDeviceValue(AsusAcpi.GpuEcoRog, "GpuEco") : -1;
    }
    /// <summary>
    /// Reads the raw GPU MUX flag from ASUS ACPI.
    /// </summary>
    private int ReadGpuMuxFlag()
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.GetDeviceValue(AsusAcpi.GpuMuxRog, "GpuMux") : -1;
    }
    /// <summary>
    /// Writes the raw Eco flag through ASUS ACPI.
    /// </summary>
    private int SetGpuEcoFlag(int ecoFlag)
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.SetDeviceValue(AsusAcpi.GpuEcoRog, ecoFlag, "GpuEco") : -1;
    }
    /// <summary>
    /// Writes the ASUS performance mode.
    /// </summary>
    private int SetPerformanceMode(PerformanceMode mode)
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.SetDeviceValue(AsusAcpi.PerformanceMode, (int)mode, nameof(PerformanceMode)) : -1;
    }
    /// <summary>
    /// Applies the requested GPU mode according to the service transition rules.
    /// </summary>
    private async Task<GpuChangeResult> SetGpuModeAsync(
        GpuMode targetMode,
        CancellationToken cancellationToken)
    {
        if (!HasGpuModeSupport())
        {
            return GpuChangeResult.Unsupported;
        }

        var currentMode = RefreshGpuMode();
        if (currentMode == targetMode)
        {
            return GpuChangeResult.NoChange;
        }
        if (targetMode == GpuMode.Eco)
        {
            if (IsExternalGpuConnected())
            {
                return GpuChangeResult.Blocked;
            }

            if (!await ApplyEcoModeTransitionAsync(enableEcoMode: true, cancellationToken).ConfigureAwait(false))
            {
                return GpuChangeResult.Failed;
            }
        }
        else if (!await ApplyEcoModeTransitionAsync(enableEcoMode: false, cancellationToken).ConfigureAwait(false))
        {
            return GpuChangeResult.Failed;
        }

        var observedMode = RefreshGpuMode();
        return observedMode == targetMode ? GpuChangeResult.Changed : GpuChangeResult.Failed;
    }

    /// <summary>
    /// Refreshes the tracked GPU mode from the current hardware flags.
    /// </summary>
    private GpuMode RefreshGpuMode()
    {
        if (!HasGpuModeSupport())
        {
            CurrentGpuMode = GpuMode.Standard;
            return CurrentGpuMode;
        }
        var ecoFlag = ReadGpuEcoFlag();
        _ = ReadGpuMuxFlag();

        CurrentGpuMode = ecoFlag == 1 ? GpuMode.Eco : GpuMode.Standard;
        return CurrentGpuMode;
    }

    /// <summary>
    /// Determines whether GPU mode control is available on the current device.
    /// </summary>
    private bool HasGpuModeSupport() => ReadGpuEcoFlag() >= 0;
    /// <summary>
    /// Returns whether an external GPU is connected.
    /// </summary>
    /// <remarks>
    /// This is currently a stub because the original implementation did not provide detection logic.
    /// </remarks>
    private static bool IsExternalGpuConnected() => false;
    /// <summary>
    /// Applies the Eco mode transition sequence.
    /// </summary>
    /// <returns><see langword="true"/> when the ASUS ACPI write was accepted.</returns>
    private async Task<bool> ApplyEcoModeTransitionAsync(bool enableEcoMode, CancellationToken cancellationToken)
    {
        if (enableEcoMode)
        {
            _logger.LogInformation("Preparing to enable Eco GPU mode.");
        }

        var result = SetGpuEcoFlag(enableEcoMode ? 1 : 0);
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
            _logger.LogInformation("GPU Eco mode disabled. AC power connected: {IsPluggedIn}.", PowerNative.IsOnAcPower());
        }

        _logger.LogDebug(
            "Performance/GPU configuration remains {PerformanceMode}/{GpuMode} after GPU transition.",
            _options.CurrentValue.PerformanceMode,
            _options.CurrentValue.GpuMode);
        return true;
    }

}
