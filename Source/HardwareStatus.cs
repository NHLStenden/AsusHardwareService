namespace AsusHardwareService;

/// <summary>
/// Represents a hardware state update that can be published to an interested presentation layer.
/// </summary>
public abstract record HardwareStatus;

/// <summary>
/// Represents the current built-in display brightness.
/// </summary>
/// <param name="Percentage">The display brightness percentage from <c>0</c> through <c>100</c>.</param>
public sealed record DisplayBrightnessStatus(int Percentage) : HardwareStatus;

/// <summary>
/// Represents the current ASUS keyboard-backlight level.
/// </summary>
/// <param name="Level">The keyboard-backlight level from <c>0</c> through <c>3</c>.</param>
public sealed record KeyboardBacklightStatus(int Level) : HardwareStatus;

/// <summary>
/// Represents the current microphone mute state.
/// </summary>
/// <param name="Muted"><see langword="true"/> when the microphone is muted.</param>
public sealed record MicrophoneStatus(bool Muted) : HardwareStatus;

/// <summary>
/// Represents the current combined ASUS performance and GPU mode.
/// </summary>
/// <param name="PerformanceMode">The active or requested ASUS performance mode.</param>
/// <param name="GpuMode">The active or requested ASUS GPU mode.</param>
public sealed record PerformanceGpuStatus(
    PerformanceMode PerformanceMode,
    GpuMode GpuMode) : HardwareStatus;
