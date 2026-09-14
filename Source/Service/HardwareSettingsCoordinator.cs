using AsusHardwareService.Asus.Battery;
using AsusHardwareService.Configuration;
using AsusHardwareService.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Service;

/// <summary>Application-layer result of applying a hardware-settings patch.</summary>
/// <param name="Success">Whether all requested changes were persisted and applied.</param>
/// <param name="Settings">The authoritative service-owned state after the operation.</param>
/// <param name="Error">A user-presentable failure summary, when available.</param>
internal sealed record HardwareSettingsUpdateResult(
    bool Success,
    HardwareSettingsSnapshot Settings,
    string? Error = null);

/// <summary>Owns reads and updates of user-adjustable hardware settings.</summary>
/// <remarks>
/// Presentation code never writes ACPI or configuration directly. New fly-out properties should
/// be added as patch fields and handled here so validation, persistence, and hardware application
/// remain behind one service-owned coordination boundary.
/// </remarks>
internal sealed class HardwareSettingsCoordinator : IDisposable
{
    private readonly ILogger<HardwareSettingsCoordinator> _logger;
    private readonly BatteryChargeLimiter _batteryChargeLimiter;
    private readonly MutableHardwareSettingsStore _settingsStore;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly IDisposable? _optionsSubscription;
    private int _batteryChargeLimitPercent;

    /// <summary>Initializes the service-owned mutable settings coordinator.</summary>
    public HardwareSettingsCoordinator(
        ILogger<HardwareSettingsCoordinator> logger,
        BatteryChargeLimiter batteryChargeLimiter,
        MutableHardwareSettingsStore settingsStore,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batteryChargeLimiter = batteryChargeLimiter ?? throw new ArgumentNullException(nameof(batteryChargeLimiter));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        ArgumentNullException.ThrowIfNull(options);

        _batteryChargeLimitPercent = BatteryChargeLimitPolicy.Normalize(
            options.CurrentValue.BatteryChargeLimitPercent);
        _optionsSubscription = options.OnChange((value, _) =>
        {
            Interlocked.Exchange(
                ref _batteryChargeLimitPercent,
                BatteryChargeLimitPolicy.Normalize(value.BatteryChargeLimitPercent));
        });
    }

    /// <summary>Returns the current service-owned settings and UI constraints.</summary>
    /// <returns>A snapshot suitable for presentation.</returns>
    public HardwareSettingsSnapshot GetSnapshot()
    {
        var chargeLimit = Volatile.Read(ref _batteryChargeLimitPercent);
        return new HardwareSettingsSnapshot(
            new IntegerSettingState(
                chargeLimit,
                BatteryChargeLimitPolicy.MinimumPercent,
                BatteryChargeLimitPolicy.MaximumPercent,
                BatteryChargeLimitPolicy.StepPercent));
    }

    /// <summary>Validates, persists, and applies a partial user settings update.</summary>
    /// <param name="patch">The settings properties to update.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The result and authoritative post-update settings.</returns>
    public async Task<HardwareSettingsUpdateResult> UpdateAsync(
        HardwareSettingsPatch patch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.IsEmpty)
        {
            return Failure("No hardware settings were supplied.");
        }

        if (patch.BatteryChargeLimitPercent is { } chargeLimit &&
            !BatteryChargeLimitPolicy.IsValid(chargeLimit))
        {
            return Failure(
                $"Battery charge limit must be {BatteryChargeLimitPolicy.MinimumPercent}% to " +
                $"{BatteryChargeLimitPolicy.MaximumPercent}% in {BatteryChargeLimitPolicy.StepPercent}% steps.");
        }

        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Persist desired state first. A transient ACPI failure remains recoverable because
            // service startup will retry the durable user selection.
            await _settingsStore.UpdateAsync(patch, cancellationToken).ConfigureAwait(false);

            if (patch.BatteryChargeLimitPercent is { } requestedChargeLimit)
            {
                Interlocked.Exchange(ref _batteryChargeLimitPercent, requestedChargeLimit);
                if (!_batteryChargeLimiter.ApplyLimit(requestedChargeLimit))
                {
                    _logger.LogWarning(
                        "Battery charge limit {Limit}% was persisted but could not be applied immediately.",
                        requestedChargeLimit);
                    return Failure("Saved, but the charge limit could not be applied.");
                }
            }

            return Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update user-adjustable hardware settings.");
            return Failure("The charge limit could not be saved.");
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>Releases the configuration subscription and update lock.</summary>
    public void Dispose()
    {
        _optionsSubscription?.Dispose();
        _updateLock.Dispose();
    }

    private HardwareSettingsUpdateResult Success() =>
        new(true, GetSnapshot());

    private HardwareSettingsUpdateResult Failure(string error) =>
        new(false, GetSnapshot(), error);
}
