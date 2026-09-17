using AsusHardwareService.Asus.Battery;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Configuration;
using AsusHardwareService.Settings;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Service;

/// <summary>Application-layer result of applying a hardware-settings patch.</summary>
/// <param name="Success">Whether all requested changes were persisted and applied.</param>
/// <param name="Settings">The authoritative state after the operation.</param>
/// <param name="Error">A user-presentable failure summary, when available.</param>
internal sealed record HardwareSettingsUpdateResult(
    bool Success,
    HardwareSettingsSnapshot Settings,
    string? Error = null);

/// <summary>Owns reads and updates of hardware settings.</summary>
internal sealed class HardwareSettingsCoordinator : IDisposable
{
    private readonly ILogger<HardwareSettingsCoordinator> _logger;
    private readonly BatteryChargeLimiter _batteryChargeLimiter;
    private readonly OperatingModeController _operatingModeController;
    private readonly LaptopDisplayController _laptopDisplayController;
    private readonly SplendidProfileApplier _splendidProfileApplier;
    private readonly UserSessionService _userSessionService;
    private readonly MutableHardwareSettingsStore _settingsStore;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly IDisposable? _optionsSubscription;
    private int _batteryChargeLimitPercent;
    private int _laptopDisplayMode;
    private int _miniLedMode;
    private int _splendidVisualMode;
    private int _splendidGamutMode;
    private int _splendidColorTemperature;

    /// <summary>Initializes the mutable settings coordinator.</summary>
    public HardwareSettingsCoordinator(
        ILogger<HardwareSettingsCoordinator> logger,
        BatteryChargeLimiter batteryChargeLimiter,
        OperatingModeController operatingModeController,
        LaptopDisplayController laptopDisplayController,
        SplendidProfileApplier splendidProfileApplier,
        UserSessionService userSessionService,
        MutableHardwareSettingsStore settingsStore,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batteryChargeLimiter = batteryChargeLimiter ?? throw new ArgumentNullException(nameof(batteryChargeLimiter));
        _operatingModeController = operatingModeController ?? throw new ArgumentNullException(nameof(operatingModeController));
        _laptopDisplayController = laptopDisplayController ?? throw new ArgumentNullException(nameof(laptopDisplayController));
        _splendidProfileApplier = splendidProfileApplier ?? throw new ArgumentNullException(nameof(splendidProfileApplier));
        _userSessionService = userSessionService ?? throw new ArgumentNullException(nameof(userSessionService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        ArgumentNullException.ThrowIfNull(options);

        _batteryChargeLimitPercent = BatteryChargeLimitPolicy.Normalize(
            options.CurrentValue.BatteryChargeLimitPercent);
        _laptopDisplayMode = (int)options.CurrentValue.LaptopDisplayMode;
        _miniLedMode = (int)options.CurrentValue.MiniLedMode;
        _splendidVisualMode = (int)options.CurrentValue.SplendidVisualMode;
        _splendidGamutMode = (int)options.CurrentValue.SplendidGamutMode;
        _splendidColorTemperature = options.CurrentValue.ColorTemperature;
        _optionsSubscription = options.OnChange((value, _) =>
        {
            Interlocked.Exchange(
                ref _batteryChargeLimitPercent,
                BatteryChargeLimitPolicy.Normalize(value.BatteryChargeLimitPercent));
            Interlocked.Exchange(ref _laptopDisplayMode, (int)value.LaptopDisplayMode);
            Interlocked.Exchange(ref _miniLedMode, (int)value.MiniLedMode);
            Interlocked.Exchange(ref _splendidVisualMode, (int)value.SplendidVisualMode);
            Interlocked.Exchange(ref _splendidGamutMode, (int)value.SplendidGamutMode);
            Interlocked.Exchange(ref _splendidColorTemperature, value.ColorTemperature);
        });
    }

    /// <summary>Gets the current settings and UI constraints.</summary>
    /// <returns>A snapshot suitable for presentation.</returns>
    public HardwareSettingsSnapshot GetSnapshot()
    {
        var chargeLimit = Volatile.Read(ref _batteryChargeLimitPercent);
        return new HardwareSettingsSnapshot(
            new IntegerSettingState(
                chargeLimit,
                BatteryChargeLimitPolicy.MinimumPercent,
                BatteryChargeLimitPolicy.MaximumPercent,
                BatteryChargeLimitPolicy.StepPercent),
            _operatingModeController.CurrentMode.ToPreset(),
            (LaptopDisplayMode)Volatile.Read(ref _laptopDisplayMode),
            (MiniLedMode)Volatile.Read(ref _miniLedMode),
            (SplendidVisualMode)Volatile.Read(ref _splendidVisualMode),
            (SplendidGamutMode)Volatile.Read(ref _splendidGamutMode),
            (SplendidColorTemperature)Volatile.Read(ref _splendidColorTemperature));
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

        if (patch.OperatingMode is { } operatingMode && !Enum.IsDefined(typeof(OperatingModePreset), operatingMode))
        {
            return Failure("Unsupported operating mode.");
        }

        if (patch.LaptopDisplayMode is { } laptopDisplayMode &&
            !Enum.IsDefined(typeof(LaptopDisplayMode), laptopDisplayMode))
        {
            return Failure("Unsupported laptop screen mode.");
        }

        if (patch.MiniLedMode is { } miniLedMode &&
            !Enum.IsDefined(typeof(MiniLedMode), miniLedMode))
        {
            return Failure("Unsupported MiniLED mode.");
        }

        if (patch.SplendidVisualMode is { } splendidVisualMode &&
            !Enum.IsDefined(typeof(SplendidVisualMode), splendidVisualMode))
        {
            return Failure("Unsupported visual mode.");
        }

        if (patch.SplendidGamutMode is { } splendidGamutMode &&
            !Enum.IsDefined(typeof(SplendidGamutMode), splendidGamutMode))
        {
            return Failure("Unsupported color gamut.");
        }

        if (patch.SplendidColorTemperature is { } splendidColorTemperature &&
            !Enum.IsDefined(typeof(SplendidColorTemperature), splendidColorTemperature))
        {
            return Failure("Unsupported color temperature.");
        }

        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Persist the requested setting before applying it to hardware.
            await _settingsStore.UpdateAsync(patch, cancellationToken).ConfigureAwait(false);

            var chargeLimitApplied = true;
            if (patch.BatteryChargeLimitPercent is { } requestedChargeLimit)
            {
                Interlocked.Exchange(ref _batteryChargeLimitPercent, requestedChargeLimit);
                chargeLimitApplied = _batteryChargeLimiter.ApplyLimit(requestedChargeLimit);
                if (!chargeLimitApplied)
                {
                    _logger.LogWarning(
                        "Battery charge limit {Limit}% was persisted but could not be applied immediately.",
                        requestedChargeLimit);
                }
            }

            var operatingModeApplied = true;
            if (patch.OperatingMode is { } requestedOperatingMode)
            {
                var hardwareMode = HardwareOperatingMode.FromPreset(requestedOperatingMode);
                operatingModeApplied = await _operatingModeController
                    .ApplyAsync(hardwareMode, cancellationToken)
                    .ConfigureAwait(false);
                if (!operatingModeApplied)
                {
                    _logger.LogWarning(
                        "Operating mode {OperatingMode} was persisted but could not be applied immediately.",
                        requestedOperatingMode);
                }
            }

            var laptopDisplayModeApplied = true;
            if (patch.LaptopDisplayMode is { } requestedLaptopDisplayMode)
            {
                Interlocked.Exchange(ref _laptopDisplayMode, (int)requestedLaptopDisplayMode);
                var session = _userSessionService.GetActiveSession();
                laptopDisplayModeApplied = session is not null &&
                    _laptopDisplayController.ApplyLaptopDisplayMode(requestedLaptopDisplayMode, session);
                if (!laptopDisplayModeApplied)
                {
                    _logger.LogWarning(
                        "Laptop display mode {LaptopDisplayMode} was persisted but could not be applied immediately.",
                        requestedLaptopDisplayMode);
                }
            }

            var miniLedModeApplied = true;
            if (patch.MiniLedMode is { } requestedMiniLedMode)
            {
                Interlocked.Exchange(ref _miniLedMode, (int)requestedMiniLedMode);
                miniLedModeApplied = _laptopDisplayController.ApplyMiniLedMode(requestedMiniLedMode);
                if (!miniLedModeApplied)
                {
                    _logger.LogWarning(
                        "MiniLED mode {MiniLedMode} was persisted but could not be applied immediately.",
                        requestedMiniLedMode);
                }
            }

            var splendidProfileApplied = true;
            var splendidProfileRequested =
                patch.SplendidVisualMode.HasValue ||
                patch.SplendidGamutMode.HasValue ||
                patch.SplendidColorTemperature.HasValue;
            if (splendidProfileRequested)
            {
                if (patch.SplendidVisualMode is { } requestedSplendidVisualMode)
                {
                    Interlocked.Exchange(ref _splendidVisualMode, (int)requestedSplendidVisualMode);
                }

                if (patch.SplendidGamutMode is { } requestedSplendidGamutMode)
                {
                    Interlocked.Exchange(ref _splendidGamutMode, (int)requestedSplendidGamutMode);
                }

                if (patch.SplendidColorTemperature is { } requestedSplendidColorTemperature)
                {
                    Interlocked.Exchange(ref _splendidColorTemperature, (int)requestedSplendidColorTemperature);
                }

                var session = _userSessionService.GetActiveSession();
                splendidProfileApplied = session is not null &&
                    await _splendidProfileApplier
                        .ApplyProfileAsync(
                            session.SessionId,
                            (SplendidVisualMode)Volatile.Read(ref _splendidVisualMode),
                            (SplendidGamutMode)Volatile.Read(ref _splendidGamutMode),
                            (SplendidColorTemperature)Volatile.Read(ref _splendidColorTemperature),
                            cancellationToken)
                        .ConfigureAwait(false);
                if (!splendidProfileApplied)
                {
                    _logger.LogWarning(
                        "ASUS Splendid profile was persisted but could not be applied immediately.");
                }
            }

            if (chargeLimitApplied &&
                operatingModeApplied &&
                laptopDisplayModeApplied &&
                miniLedModeApplied &&
                splendidProfileApplied)
            {
                return Success();
            }

            var requestedSettingCount =
                (patch.BatteryChargeLimitPercent.HasValue ? 1 : 0) +
                (patch.OperatingMode.HasValue ? 1 : 0) +
                (patch.LaptopDisplayMode.HasValue ? 1 : 0) +
                (patch.MiniLedMode.HasValue ? 1 : 0) +
                (splendidProfileRequested ? 1 : 0);
            if (requestedSettingCount > 1)
            {
                return Failure("Saved; some settings were not applied.");
            }

            return Failure(!chargeLimitApplied
                ? "Saved; charge limit was not applied."
                : !operatingModeApplied
                    ? "Saved; operating mode was not applied."
                    : !laptopDisplayModeApplied
                        ? "Saved; laptop screen mode was not applied."
                        : !miniLedModeApplied
                            ? "Saved; MiniLED mode was not applied."
                            : "Saved; visual profile was not applied.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update user-adjustable hardware settings.");
            return Failure("The hardware settings could not be saved.");
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
