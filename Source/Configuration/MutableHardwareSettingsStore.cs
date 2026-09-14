using System.Text.Json;
using System.Text.Json.Nodes;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Configuration;

/// <summary>Persists user-adjustable hardware settings in the application's existing appsettings.json file.</summary>
/// <remarks>
/// Only properties represented by <see cref="HardwareSettingsPatch"/> are changed. Other configuration
/// sections and hardware properties are preserved so appsettings.json remains the single durable
/// configuration source for both startup state and interactive changes.
/// </remarks>
internal sealed class MutableHardwareSettingsStore
{
    private const string SettingsFileName = "appsettings.json";

    private readonly ILogger<MutableHardwareSettingsStore> _logger;
    private readonly string _settingsPath;

    /// <summary>Initializes the durable hardware-settings store.</summary>
    public MutableHardwareSettingsStore(
        ILogger<MutableHardwareSettingsStore> logger,
        IHostEnvironment environment)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(environment);
        _settingsPath = Path.Combine(environment.ContentRootPath, SettingsFileName);
    }

    /// <summary>Atomically applies a partial hardware-settings update to appsettings.json.</summary>
    /// <param name="patch">The settings properties to update.</param>
    /// <param name="cancellationToken">Cancels the file operation.</param>
    /// <returns>A task that completes after appsettings.json has been replaced.</returns>
    public async Task UpdateAsync(HardwareSettingsPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.IsEmpty)
        {
            return;
        }

        var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
        var hardware = root[HardwareOptions.SectionName] as JsonObject;
        if (hardware is null)
        {
            hardware = new JsonObject();
            root[HardwareOptions.SectionName] = hardware;
        }

        if (patch.BatteryChargeLimitPercent is { } chargeLimit)
        {
            hardware["ChargeLimit"] = chargeLimit;
        }

        if (patch.OperatingMode is { } operatingMode)
        {
            var hardwareMode = HardwareOperatingMode.FromPreset(operatingMode);
            hardware["PerformanceMode"] = hardwareMode.Performance.ToString();
            hardware["GpuMode"] = hardwareMode.Gpu.ToString();
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        var temporaryPath = _settingsPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            _logger.LogInformation("Persisted hardware settings to {Path}.", _settingsPath);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best effort only; a stale temp file does not affect the active configuration.
            }
        }
    }

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
            var root = JsonNode.Parse(
                json,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            return root as JsonObject
                ?? throw new InvalidDataException($"Configuration file '{_settingsPath}' must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Configuration file {Path} contains invalid JSON.", _settingsPath);
            throw new InvalidDataException($"Configuration file '{_settingsPath}' contains invalid JSON.", exception);
        }
    }
}
