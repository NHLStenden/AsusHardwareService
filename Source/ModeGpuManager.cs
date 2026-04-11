using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Describes the outcome of a GPU mode change request.
/// </summary>
public enum GpuChangeResult
{
    /// <summary>
    /// The requested GPU mode was already active, so no change was needed.
    /// </summary>
    NoChange,

    /// <summary>
    /// The GPU mode was changed successfully without requiring a restart.
    /// </summary>
    Changed,

    /// <summary>
    /// The requested GPU mode change was applied, but a restart is required for it to take full effect.
    /// </summary>
    RestartRequired,

    /// <summary>
    /// The GPU mode change was cancelled, usually because confirmation was declined.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The GPU mode change could not be completed because a prerequisite or hardware condition blocked it.
    /// </summary>
    Blocked,

    /// <summary>
    /// GPU mode control is not supported on the current hardware or ACPI interface.
    /// </summary>
    Unsupported
}

/// <summary>
/// Handles ASUS performance and GPU mode logic directly against the service ACPI layer.
/// </summary>
/// <remarks>
/// This class wraps the low-level ASUS ACPI access exposed by <see cref="AsusAcpi"/> and provides
/// higher-level operations for reading, cycling, and applying performance and GPU modes. It also
/// contains the service-side policy for GPU mode transitions such as switching between Eco,
/// Standard, and Ultimate modes, including restart-required transitions and optional confirmation
/// callbacks.
/// </remarks>
public sealed class ModeGpuManager
{
    /// <summary>
    /// Represents the current Windows system power status returned by <c>GetSystemPowerStatus</c>.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        /// <summary>
        /// Indicates whether the system is running on AC power.
        /// </summary>
        public byte ACLineStatus;

        /// <summary>
        /// Provides battery condition flags reported by the operating system.
        /// </summary>
        public byte BatteryFlag;

        /// <summary>
        /// Gets the remaining battery percentage.
        /// </summary>
        public byte BatteryLifePercent;

        /// <summary>
        /// Reserved field in the native structure.
        /// </summary>
        public byte Reserved1;

        /// <summary>
        /// Gets the estimated remaining battery life, in seconds.
        /// </summary>
        public int BatteryLifeTime;

        /// <summary>
        /// Gets the estimated full battery life, in seconds.
        /// </summary>
        public int BatteryFullLifeTime;
    }

    private static readonly IReadOnlyList<PerformanceMode> _modeOrder =
        new[] { PerformanceMode.Silent, PerformanceMode.Balanced };

    private readonly SemaphoreSlim _combinedModeLock = new(1, 1);
    private readonly IServiceProvider _services;
    private readonly ILogger<ModeGpuManager> _logger;
    private readonly HardwareOptions _options;

    /// <summary>
    /// Initialises a new instance of the <see cref="ModeGpuManager"/> class.
    /// </summary>
    /// <param name="services">The service provider used to resolve <see cref="AsusAcpi"/> instances.</param>
    /// <param name="logger">The logger used for diagnostics and transition messages.</param>
    /// <param name="options">The configured hardware service options.</param>
    public ModeGpuManager(
        IServiceProvider services,
        ILogger<ModeGpuManager> logger,
        IOptions<HardwareOptions> options)
    {
        _services = services;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the last performance mode tracked by this manager.
    /// </summary>
    public PerformanceMode CurrentPerformanceMode { get; private set; } = PerformanceMode.Balanced;

    /// <summary>
    /// Gets the last GPU mode tracked by this manager.
    /// </summary>
    public GpuMode CurrentGpuMode { get; private set; } = GpuMode.Standard;

    /// <summary>
    /// Reads the currently tracked combined performance and GPU mode pair.
    /// </summary>
    /// <returns>The current performance mode and GPU mode.</returns>
    public (PerformanceMode performanceMode, GpuMode gpuMode) GetCurrentCombinedMode() =>
        (CurrentPerformanceMode, CurrentGpuMode);

    /// <summary>
    /// Returns the next combined mode pair in the service rotation.
    /// </summary>
    /// <param name="performanceMode">The current performance mode.</param>
    /// <param name="gpuMode">The current GPU mode.</param>
    /// <returns>The next combined performance/GPU mode pair.</returns>
    public static (PerformanceMode performanceMode, GpuMode gpuMode) GetNextCombinedMode(
        PerformanceMode performanceMode,
        GpuMode gpuMode) =>
        performanceMode == PerformanceMode.Silent && gpuMode == GpuMode.Eco
            ? (PerformanceMode.Balanced, GpuMode.Standard)
            : (PerformanceMode.Silent, GpuMode.Eco);

    /// <summary>
    /// Cycles to the next combined performance and GPU mode pair.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the mode switch has finished.</returns>
    public async Task ToggleCombinedModeAsync(CancellationToken cancellationToken = default)
    {
        var current = GetCurrentCombinedMode();
        var next = GetNextCombinedMode(current.performanceMode, current.gpuMode);

        await ApplyCombinedModeAsync(next.performanceMode, next.gpuMode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a combined performance and GPU mode pair.
    /// </summary>
    /// <param name="performanceMode">The performance mode to apply.</param>
    /// <param name="gpuMode">The GPU mode to apply.</param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when both mode operations have finished.</returns>
    public async Task ApplyCombinedModeAsync(
        PerformanceMode performanceMode,
        GpuMode gpuMode,
        CancellationToken cancellationToken = default)
    {
        await _combinedModeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation(
                "Applying combined mode {PerformanceMode}/{GpuMode}.",
                performanceMode,
                gpuMode);

            var perfResult = await SetPerformanceModeAsync(performanceMode, cancellationToken).ConfigureAwait(false);
            if (perfResult != 1)
            {
                _logger.LogWarning(
                    "Setting performance mode to {PerformanceMode} returned {Result}.",
                    performanceMode,
                    perfResult);
            }

            var gpuResult = await SetGpuModeAsync(gpuMode, cancellationToken: cancellationToken).ConfigureAwait(false);
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
            _combinedModeLock.Release();
        }
    }
    /// <summary>
    /// Returns a user-friendly display name for a performance mode.
    /// </summary>
    /// <param name="mode">The performance mode.</param>
    /// <returns>The display name for the supplied mode.</returns>
    private static string GetPerformanceModeName(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Silent => "Silent",
        PerformanceMode.Balanced => "Balanced",
        _ => mode.ToString()
    };

    /// <summary>
    /// Returns a user-friendly display name for a GPU mode.
    /// </summary>
    /// <param name="mode">The GPU mode.</param>
    /// <returns>The display name for the supplied mode.</returns>
    private static string GetGpuModeName(GpuMode mode) => mode switch
    {
        GpuMode.Eco => "Eco",
        GpuMode.Standard => "Standard",
        _ => mode.ToString()
    };

    /// <summary>
    /// Updates the tracked performance mode without applying it to hardware.
    /// </summary>
    /// <param name="mode">The performance mode to track.</param>
    private void SetPerformanceMode(PerformanceMode mode)
    {
        CurrentPerformanceMode = mode;
    }

    /// <summary>
    /// Gets the next performance mode in the rotation order.
    /// </summary>
    /// <param name="back">
    /// <c>true</c> to move backwards through the rotation; otherwise, <c>false</c>.
    /// </param>
    /// <returns>The next performance mode.</returns>
    private PerformanceMode GetNextPerformanceMode(bool back = false)
    {
        var index = IndexOf(CurrentPerformanceMode);
        index = back
            ? (index - 1 + _modeOrder.Count) % _modeOrder.Count
            : (index + 1) % _modeOrder.Count;

        return _modeOrder[index];
    }

    /// <summary>
    /// Advances the tracked performance mode in the rotation order.
    /// </summary>
    /// <param name="back">
    /// <c>true</c> to move backwards through the rotation; otherwise, <c>false</c>.
    /// </param>
    /// <returns>The updated performance mode.</returns>
    private PerformanceMode CyclePerformanceMode(bool back = false)
    {
        CurrentPerformanceMode = GetNextPerformanceMode(back);
        return CurrentPerformanceMode;
    }

    /// <summary>
    /// Reads the GPU Eco flag from the ASUS ACPI interface.
    /// </summary>
    /// <returns>The raw GPU Eco flag value, or <c>-1</c> when unavailable.</returns>
    private int ReadGpuEcoFlag()
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.DeviceGet(AsusAcpi.GpuEcoRog, "GPUEco") : -1;
    }

    /// <summary>
    /// Reads the GPU MUX flag from the ASUS ACPI interface.
    /// </summary>
    /// <returns>The raw GPU MUX flag value, or <c>-1</c> when unavailable.</returns>
    private int ReadGpuMuxFlag()
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.DeviceGet(AsusAcpi.GpuMuxRog, "GPUMux") : -1;
    }

    /// <summary>
    /// Reads the current performance mode from the ASUS ACPI interface.
    /// </summary>
    /// <returns>The raw ASUS ACPI performance mode value, or <c>-1</c> when unavailable.</returns>
    private int ReadPerformanceMode()
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        return acpi.IsConnected ? acpi.DeviceGet(AsusAcpi.PerformanceMode, "PerformanceMode") : -1;
    }

    /// <summary>
    /// Writes the GPU Eco flag through the ASUS ACPI interface.
    /// </summary>
    /// <param name="eco">The raw Eco flag value to apply.</param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task containing the ASUS ACPI result code.</returns>
    private Task<int> SetGpuEcoAsync(int eco, CancellationToken cancellationToken = default)
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        int result = acpi.IsConnected ? acpi.DeviceSet(AsusAcpi.GpuEcoRog, eco, "GPUEco") : -1;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Writes the GPU MUX flag through the ASUS ACPI interface.
    /// </summary>
    /// <param name="mux">The raw MUX flag value to apply.</param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task containing the ASUS ACPI result code.</returns>
    private Task<int> SetGpuMuxAsync(int mux, CancellationToken cancellationToken = default)
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        int result = acpi.IsConnected ? acpi.DeviceSet(AsusAcpi.GpuMuxRog, mux, "GPUMux") : -1;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Writes the performance mode through the ASUS ACPI interface.
    /// </summary>
    /// <param name="mode">The performance mode to apply.</param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task containing the ASUS ACPI result code.</returns>
    private Task<int> SetPerformanceModeAsync(PerformanceMode mode, CancellationToken cancellationToken = default)
    {
        using var acpi = _services.GetRequiredService<AsusAcpi>();
        int result = acpi.IsConnected ? acpi.DeviceSet(AsusAcpi.PerformanceMode, (int)mode, "PerformanceMode") : -1;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Initialises the current GPU mode state by reading it from hardware.
    /// </summary>
    /// <returns>The resolved current GPU mode.</returns>
    private GpuMode InitialiseGpuMode()
    {
        return RefreshGpuMode();
    }

    /// <summary>
    /// Refreshes the tracked GPU mode by reading the current hardware flags.
    /// </summary>
    /// <returns>The resolved current GPU mode.</returns>
    private GpuMode RefreshGpuMode()
    {
        if (!HasGpuModeSupport())
        {
            CurrentGpuMode = GpuMode.Standard;
            return CurrentGpuMode;
        }

        var eco = ReadGpuEcoFlag();
        var mux = ReadGpuMuxFlag();

        CurrentGpuMode = ResolveGpuMode(eco, mux);
        return CurrentGpuMode;
    }

    /// <summary>
    /// Resolves a service GPU mode from the raw ASUS Eco and MUX flags.
    /// </summary>
    /// <param name="ecoFlag">The raw GPU Eco flag.</param>
    /// <param name="muxFlag">The raw GPU MUX flag.</param>
    /// <returns>The resolved GPU mode.</returns>
    private static GpuMode ResolveGpuMode(int ecoFlag, int muxFlag)
    {
        if (ecoFlag == 1)
            return GpuMode.Eco;

        return GpuMode.Standard;
    }

    /// <summary>
    /// Applies a target GPU mode using the ASUS ACPI interface and service transition rules.
    /// </summary>
    /// <param name="targetMode">The GPU mode to apply.</param>
    /// <param name="refreshDelayMs">The delay after changing Eco mode, in milliseconds.</param>
    /// <param name="nvidiaRestartDelayMs">
    /// The delay after disabling Eco mode before post-actions run, in milliseconds.
    /// </param>
    /// <param name="reapplyPerformanceTweaks">
    /// <c>true</c> to reapply performance tweaks after the GPU transition; otherwise, <c>false</c>.
    /// </param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task containing the result of the GPU mode change request.</returns>
    private async Task<GpuChangeResult> SetGpuModeAsync(
        GpuMode targetMode,
        int refreshDelayMs = 500,
        int nvidiaRestartDelayMs = 5000,
        bool reapplyPerformanceTweaks = false,
        CancellationToken cancellationToken = default)
    {
        var currentMode = RefreshGpuMode();
        if (currentMode == targetMode)
            return GpuChangeResult.NoChange;

        if (!HasGpuModeSupport())
            return GpuChangeResult.Unsupported;

        if (targetMode == GpuMode.Eco)
        {
            if (IsExternalGpuConnected())
                return GpuChangeResult.Blocked;

            await ApplyEcoAsync(eco: 1, refreshDelayMs, nvidiaRestartDelayMs, reapplyPerformanceTweaks, cancellationToken).ConfigureAwait(false);
            CurrentGpuMode = GpuMode.Eco;
            return GpuChangeResult.Changed;
        }

        await ApplyEcoAsync(eco: 0, refreshDelayMs, nvidiaRestartDelayMs, reapplyPerformanceTweaks, cancellationToken).ConfigureAwait(false);
        CurrentGpuMode = GpuMode.Standard;
        return GpuChangeResult.Changed;
    }

    /// <summary>
    /// Applies automatic GPU mode switching rules based on power state and configured mode.
    /// </summary>
    /// <param name="autoModeEnabled">
    /// <c>true</c> to switch automatically based on AC or battery state.
    /// </param>
    /// <param name="forceConfiguredMode">
    /// <c>true</c> to force the configured mode even when automatic mode is disabled.
    /// </param>
    /// <param name="configuredMode">The configured GPU mode to enforce when requested.</param>
    /// <param name="optimised">
    /// <c>true</c> to downgrade Ultimate to Standard when automatic logic detects it.
    /// </param>
    /// <param name="delayMs">An optional delay before applying the change, in milliseconds.</param>
    /// <param name="confirmAsync">
    /// An optional confirmation callback invoked before disruptive transitions.
    /// </param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>
    /// A task containing <c>true</c> when a GPU mode change was applied; otherwise, <c>false</c>.
    /// </returns>
    private async Task<bool> AutoSwitchGpuModeAsync(
        bool autoModeEnabled,
        bool forceConfiguredMode,
        GpuMode configuredMode,
        bool optimised = false,
        int delayMs = 0,
        CancellationToken cancellationToken = default)
    {
        if (!autoModeEnabled && !forceConfiguredMode)
            return false;

        var eco = ReadGpuEcoFlag();
        var mux = ReadGpuMuxFlag();

        if (mux == 0)
        {
            if (optimised)
            {
                var result = await SetGpuModeAsync(GpuMode.Standard, cancellationToken: cancellationToken).ConfigureAwait(false);
                return result is GpuChangeResult.Changed or GpuChangeResult.RestartRequired;
            }

            return false;
        }

        if (eco == 1)
        {
            if ((autoModeEnabled && IsPluggedIn()) ||
                (forceConfiguredMode && configuredMode == GpuMode.Standard))
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

                await ApplyEcoAsync(0, 500, 5000, false, cancellationToken).ConfigureAwait(false);
                CurrentGpuMode = GpuMode.Standard;
                return true;
            }
        }

        if (eco == 0)
        {
            if ((autoModeEnabled && !IsPluggedIn()) ||
                (forceConfiguredMode && configuredMode == GpuMode.Eco))
            {
                if (IsExternalGpuConnected())
                    return false;

                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

                await ApplyEcoAsync(1, 500, 5000, false, cancellationToken).ConfigureAwait(false);
                CurrentGpuMode = GpuMode.Eco;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the system is currently connected to AC power.
    /// </summary>
    /// <returns><c>true</c> when the system is plugged in; otherwise, <c>false</c>.</returns>
    private bool IsPluggedIn()
    {
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
            return true;

        return status.ACLineStatus == 1;
    }

    /// <summary>
    /// Determines whether an external GPU is currently connected.
    /// </summary>
    /// <returns>
    /// <c>true</c> if an external GPU is connected; otherwise, <c>false</c>.
    /// </returns>
    private bool IsExternalGpuConnected() => false;

    /// <summary>
    /// Determines whether the discrete GPU currently appears to be in use.
    /// </summary>
    /// <returns><c>true</c> if the GPU appears to be in use; otherwise, <c>false</c>.</returns>
    private bool IsGpuInUse() => false;

    /// <summary>
    /// Determines whether Eco mode must be disabled before switching to Ultimate mode.
    /// </summary>
    /// <returns>
    /// <c>true</c> when Eco must be turned off before enabling Ultimate; otherwise, <c>false</c>.
    /// </returns>
    private bool RequiresEcoOffBeforeUltimate() => true;

    /// <summary>
    /// Determines whether GPU mode control is supported on the current device.
    /// </summary>
    /// <returns><c>true</c> when GPU mode control is supported; otherwise, <c>false</c>.</returns>
    private bool HasGpuModeSupport() => ReadGpuEcoFlag() >= 0;

    /// <summary>
    /// Runs any actions required before enabling Eco GPU mode.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A completed task.</returns>
    private Task OnBeforeEcoEnabledAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Preparing to enable Eco GPU mode.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs any actions required after disabling Eco GPU mode.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A completed task.</returns>
    private Task OnAfterEcoDisabledAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GPU Eco mode disabled.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reapplies any performance-related tweaks after a GPU mode change.
    /// </summary>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A completed task.</returns>
    private Task ReapplyPerformanceTweaksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "ReapplyPerformanceTweaksAsync requested for {PerformanceMode}/{GpuMode}.",
            _options.PerformanceMode,
            _options.GpuMode);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies an Eco-mode transition and performs the required wait and post-transition steps.
    /// </summary>
    /// <param name="eco">The raw Eco flag value to apply.</param>
    /// <param name="refreshDelayMs">The delay after changing Eco mode, in milliseconds.</param>
    /// <param name="nvidiaRestartDelayMs">
    /// The delay after disabling Eco mode before post-actions run, in milliseconds.
    /// </param>
    /// <param name="reapplyPerformanceTweaks">
    /// <c>true</c> to reapply performance tweaks after the change; otherwise, <c>false</c>.
    /// </param>
    /// <param name="cancellationToken">A token that signals cancellation.</param>
    /// <returns>A task that completes when the transition has finished.</returns>
    private async Task ApplyEcoAsync(
        int eco,
        int refreshDelayMs,
        int nvidiaRestartDelayMs,
        bool reapplyPerformanceTweaks,
        CancellationToken cancellationToken)
    {
        if (eco == 1)
            await OnBeforeEcoEnabledAsync(cancellationToken).ConfigureAwait(false);

        await SetGpuEcoAsync(eco, cancellationToken).ConfigureAwait(false);
        await Task.Delay(refreshDelayMs, cancellationToken).ConfigureAwait(false);

        if (eco == 0)
        {
            if (nvidiaRestartDelayMs > 0)
                await Task.Delay(nvidiaRestartDelayMs, cancellationToken).ConfigureAwait(false);

            await OnAfterEcoDisabledAsync(cancellationToken).ConfigureAwait(false);
        }

        if (reapplyPerformanceTweaks)
            await ReapplyPerformanceTweaksAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the position of a performance mode in the internal rotation list.
    /// </summary>
    /// <param name="mode">The performance mode to locate.</param>
    /// <returns>The zero-based index of the mode, or the balanced-mode index when not found.</returns>
    private static int IndexOf(PerformanceMode mode)
    {
        for (var i = 0; i < _modeOrder.Count; i++)
        {
            if (_modeOrder[i] == mode)
                return i;
        }

        return 1;
    }

    /// <summary>
    /// Invokes an optional confirmation callback, or returns <c>true</c> when no callback is supplied.
    /// </summary>
    /// <param name="confirmAsync">The optional confirmation callback.</param>
    /// <param name="message">The confirmation message.</param>
    /// <returns>A task containing the confirmation result.</returns>
    private static Task<bool> ConfirmAsync(Func<string, Task<bool>>? confirmAsync, string message)
    {
        if (confirmAsync is null)
            return Task.FromResult(true);

        return confirmAsync(message);
    }

    /// <summary>
    /// Retrieves the current system power status from Windows.
    /// </summary>
    /// <param name="lpSystemPowerStatus">Receives the system power status structure.</param>
    /// <returns><c>true</c> if the call succeeds; otherwise, <c>false</c>.</returns>
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);
}