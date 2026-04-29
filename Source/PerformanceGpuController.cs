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
    private readonly HardwareOptions _options;

    /// <summary>
    /// Initialises a new instance of the <see cref="PerformanceGpuController"/> class.
    /// </summary>
    public PerformanceGpuController(
        IServiceProvider services,
        ILogger<PerformanceGpuController> logger,
        IOptions<HardwareOptions> options)
    {
        _services = services;
        _logger = logger;
        _options = options.Value;
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
    public async Task ToggleCombinedModeAsync(CancellationToken cancellationToken = default)
    {
        var currentMode = GetCurrentCombinedMode();
        var nextMode = GetNextCombinedMode(currentMode.performanceMode, currentMode.gpuMode);

        await ApplyCombinedModeAsync(nextMode.performanceMode, nextMode.gpuMode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a combined performance and GPU mode pair.
    /// </summary>
    public async Task ApplyCombinedModeAsync(
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
            if (performanceResult != 1)
            {
                _logger.LogWarning(
                    "Setting performance mode to {PerformanceMode} returned {Result}.",
                    performanceMode,
                    performanceResult);
            }

            var gpuResult = await SetGpuModeAsync(gpuMode, cancellationToken).ConfigureAwait(false);
            if (gpuResult is not (GpuChangeResult.Changed or GpuChangeResult.NoChange))
            {
                _logger.LogWarning(
                    "Setting GPU mode to {GpuMode} returned {Result}.",
                    gpuMode,
                    gpuResult);
            }

            CurrentPerformanceMode = performanceMode;
            CurrentGpuMode = gpuMode;
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

            await ApplyEcoModeTransitionAsync(enableEcoMode: true, cancellationToken).ConfigureAwait(false);
            CurrentGpuMode = GpuMode.Eco;
            return GpuChangeResult.Changed;
        }

        await ApplyEcoModeTransitionAsync(enableEcoMode: false, cancellationToken).ConfigureAwait(false);
        CurrentGpuMode = GpuMode.Standard;
        return GpuChangeResult.Changed;
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
    private async Task ApplyEcoModeTransitionAsync(bool enableEcoMode, CancellationToken cancellationToken)
    {
        if (enableEcoMode)
        {
            _logger.LogInformation("Preparing to enable Eco GPU mode.");
        }

        _ = SetGpuEcoFlag(enableEcoMode ? 1 : 0);
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        if (!enableEcoMode)
        {
            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("GPU Eco mode disabled. AC power connected: {IsPluggedIn}.", PowerNative.IsOnAcPower());
        }

        _logger.LogDebug(
            "Performance/GPU configuration remains {PerformanceMode}/{GpuMode} after GPU transition.",
            _options.PerformanceMode,
            _options.GpuMode);
    }

}
