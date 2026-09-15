using System.Text.Json;
using System.Text.Json.Serialization;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;

namespace AsusHardwareService.Settings;

/// <summary>Identifies an operation in the service-owned hardware-settings protocol.</summary>
internal enum HardwareSettingsOperation
{
    /// <summary>Reads the current user-adjustable hardware settings.</summary>
    Read = 1,

    /// <summary>Applies a partial update to the user-adjustable hardware settings.</summary>
    Update = 2,
}

/// <summary>Describes an integer setting together with the range the UI may expose.</summary>
/// <param name="Value">The service-owned current value.</param>
/// <param name="Minimum">The smallest accepted value.</param>
/// <param name="Maximum">The largest accepted value.</param>
/// <param name="Step">The smallest supported increment.</param>
internal sealed record IntegerSettingState(int Value, int Minimum, int Maximum, int Step);

/// <summary>Snapshot of user-adjustable hardware settings.</summary>
/// <param name="BatteryChargeLimit">The current battery charge-limit state.</param>
/// <param name="OperatingMode">The current service-observed operating-mode preset, when representable.</param>
/// <param name="LaptopDisplayMode">The configured laptop-panel refresh-rate and overdrive preset.</param>
/// <remarks>
/// Add future user-adjustable properties here rather than creating feature-specific presentation
/// channels. The service remains authoritative for validation and hardware application.
/// </remarks>
internal sealed record HardwareSettingsSnapshot(
    IntegerSettingState BatteryChargeLimit,
    OperatingModePreset? OperatingMode,
    LaptopDisplayMode LaptopDisplayMode);

/// <summary>Partial hardware-settings update. Null properties are left unchanged.</summary>
/// <param name="BatteryChargeLimitPercent">A replacement battery charge ceiling, or <see langword="null"/>.</param>
/// <param name="OperatingMode">A replacement operating-mode preset, or <see langword="null"/>.</param>
/// <param name="LaptopDisplayMode">A replacement laptop-panel preset, or <see langword="null"/>.</param>
/// <remarks>
/// This patch shape deliberately leaves room for additional independently-updatable properties.
/// </remarks>
internal sealed record HardwareSettingsPatch(
    int? BatteryChargeLimitPercent = null,
    OperatingModePreset? OperatingMode = null,
    LaptopDisplayMode? LaptopDisplayMode = null)
{
    /// <summary>Gets whether the patch contains no setting changes.</summary>
    internal bool IsEmpty =>
        !BatteryChargeLimitPercent.HasValue &&
        !OperatingMode.HasValue &&
        !LaptopDisplayMode.HasValue;
}

/// <summary>One request sent from the interactive fly-out to the Windows service.</summary>
/// <param name="ProtocolVersion">The wire-contract version used by the sender.</param>
/// <param name="Operation">The requested settings operation.</param>
/// <param name="Changes">The partial update for an update operation.</param>
internal sealed record HardwareSettingsRequest(
    int ProtocolVersion,
    HardwareSettingsOperation Operation,
    HardwareSettingsPatch? Changes = null);

/// <summary>One response returned by the Windows service to the interactive fly-out.</summary>
/// <param name="ProtocolVersion">The wire-contract version used by the service.</param>
/// <param name="Success">Whether the requested operation completed successfully.</param>
/// <param name="Settings">The authoritative service-owned settings after the operation.</param>
/// <param name="Error">A user-presentable failure summary, when available.</param>
internal sealed record HardwareSettingsResponse(
    int ProtocolVersion,
    bool Success,
    HardwareSettingsSnapshot Settings,
    string? Error = null);

/// <summary>Shared wire-level settings for the local hardware-settings channel.</summary>
internal static class HardwareSettingsProtocol
{
    /// <summary>Current protocol version.</summary>
    internal const int Version = 3;

    /// <summary>Local named-pipe endpoint used by interactive presentation processes.</summary>
    internal const string PipeName = "AsusHardwareService.HardwareSettings.v3";

    /// <summary>Maximum accepted request or response length in UTF-16 characters.</summary>
    internal const int MaximumMessageCharacters = 16 * 1024;

    /// <summary>Gets the serializer options shared by both endpoints.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
