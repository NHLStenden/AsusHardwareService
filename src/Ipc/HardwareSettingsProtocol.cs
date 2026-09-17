using System.Text.Json;
using System.Text.Json.Serialization;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;

namespace AsusHardwareService.Ipc;

/// <summary>Identifies an operation in the hardware-settings protocol.</summary>
internal enum HardwareSettingsOperation
{
    /// <summary>Reads the current hardware settings.</summary>
    Read = 1,

    /// <summary>Applies a partial update to the hardware settings.</summary>
    Update = 2,
}

/// <summary>Describes an integer setting together with the range the UI may expose.</summary>
/// <param name="Value">The current value.</param>
/// <param name="Minimum">The smallest accepted value.</param>
/// <param name="Maximum">The largest accepted value.</param>
/// <param name="Step">The smallest supported increment.</param>
internal sealed record IntegerSettingState(int Value, int Minimum, int Maximum, int Step);

/// <summary>Represents hardware settings.</summary>
/// <param name="BatteryChargeLimit">The current battery charge-limit state.</param>
/// <param name="OperatingMode">The current operating-mode preset, when representable.</param>
/// <param name="LaptopDisplayMode">The laptop-panel refresh-rate and overdrive preset.</param>
/// <param name="MiniLedMode">The MiniLED local-dimming mode.</param>
/// <param name="SplendidVisualMode">The ASUS Splendid visual preset.</param>
/// <param name="SplendidGamutMode">The ASUS Splendid color gamut.</param>
/// <param name="SplendidColorTemperature">The ASUS Splendid color temperature.</param>
internal sealed record HardwareSettingsSnapshot(
    IntegerSettingState BatteryChargeLimit,
    OperatingModePreset? OperatingMode,
    LaptopDisplayMode LaptopDisplayMode,
    MiniLedMode MiniLedMode,
    SplendidVisualMode SplendidVisualMode,
    SplendidGamutMode SplendidGamutMode,
    SplendidColorTemperature SplendidColorTemperature);

/// <summary>Represents a partial hardware settings update. Null properties are unchanged.</summary>
/// <param name="BatteryChargeLimitPercent">A replacement battery charge ceiling, or <see langword="null"/>.</param>
/// <param name="OperatingMode">A replacement operating-mode preset, or <see langword="null"/>.</param>
/// <param name="LaptopDisplayMode">A replacement laptop-panel preset, or <see langword="null"/>.</param>
/// <param name="MiniLedMode">A replacement MiniLED local-dimming mode, or <see langword="null"/>.</param>
/// <param name="SplendidVisualMode">A replacement ASUS Splendid visual preset, or <see langword="null"/>.</param>
/// <param name="SplendidGamutMode">A replacement ASUS Splendid color gamut, or <see langword="null"/>.</param>
/// <param name="SplendidColorTemperature">A replacement ASUS Splendid color temperature, or <see langword="null"/>.</param>
internal sealed record HardwareSettingsPatch(
    int? BatteryChargeLimitPercent = null,
    OperatingModePreset? OperatingMode = null,
    LaptopDisplayMode? LaptopDisplayMode = null,
    MiniLedMode? MiniLedMode = null,
    SplendidVisualMode? SplendidVisualMode = null,
    SplendidGamutMode? SplendidGamutMode = null,
    SplendidColorTemperature? SplendidColorTemperature = null)
{
    /// <summary>Gets a value indicating whether the patch contains no setting changes.</summary>
    internal bool IsEmpty =>
        !BatteryChargeLimitPercent.HasValue &&
        !OperatingMode.HasValue &&
        !LaptopDisplayMode.HasValue &&
        !MiniLedMode.HasValue &&
        !SplendidVisualMode.HasValue &&
        !SplendidGamutMode.HasValue &&
        !SplendidColorTemperature.HasValue;
}

/// <summary>Represents a request sent from the settings flyout to the Windows service.</summary>
/// <param name="ProtocolVersion">The protocol version used by the sender.</param>
/// <param name="Operation">The requested settings operation.</param>
/// <param name="Changes">The partial update for an update operation.</param>
internal sealed record HardwareSettingsRequest(
    int ProtocolVersion,
    HardwareSettingsOperation Operation,
    HardwareSettingsPatch? Changes = null);

/// <summary>Represents a response returned by the Windows service to the settings flyout.</summary>
/// <param name="ProtocolVersion">The protocol version used by the service.</param>
/// <param name="Success">Whether the requested operation completed successfully.</param>
/// <param name="Settings">The settings after the operation.</param>
/// <param name="Error">A user-presentable failure summary, when available.</param>
internal sealed record HardwareSettingsResponse(
    int ProtocolVersion,
    bool Success,
    HardwareSettingsSnapshot Settings,
    string? Error = null);

/// <summary>Shared protocol settings for the hardware settings pipe.</summary>
internal static class HardwareSettingsProtocol
{
    /// <summary>Current protocol version.</summary>
    internal const int Version = 6;

    /// <summary>Local named-pipe endpoint used by user interface processes.</summary>
    internal const string PipeName = "AsusHardwareService.HardwareSettings.v6";

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
