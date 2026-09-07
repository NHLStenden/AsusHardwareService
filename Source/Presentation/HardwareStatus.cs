using AsusHardwareService.Asus.Performance;

namespace AsusHardwareService.Presentation;

/// <summary>
/// Base type for hardware-state changes that can be presented to the interactive user.
/// </summary>
internal abstract record HardwareStatus;

/// <summary>
/// Reports the current built-in display brightness.
/// </summary>
/// <param name="Percentage">The brightness percentage from 0 through 100.</param>
internal sealed record DisplayBrightnessStatus(int Percentage) : HardwareStatus;

/// <summary>
/// Reports the current ASUS keyboard-backlight level.
/// </summary>
/// <param name="Level">The firmware brightness level from 0 through 3.</param>
internal sealed record KeyboardBacklightStatus(int Level) : HardwareStatus;

/// <summary>
/// Reports the current default microphone mute state.
/// </summary>
/// <param name="Muted"><see langword="true"/> when capture is muted.</param>
internal sealed record MicrophoneMuteStatus(bool Muted) : HardwareStatus;

/// <summary>
/// Reports the active or requested paired ASUS operating mode.
/// </summary>
/// <param name="Mode">The paired performance and GPU mode.</param>
internal sealed record OperatingModeStatus(HardwareOperatingMode Mode) : HardwareStatus;
