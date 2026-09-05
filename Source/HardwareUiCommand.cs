namespace AsusHardwareService;

/// <summary>
/// Identifies the hardware status that the resident UI should display.
/// </summary>
internal enum HardwareUiNotificationKind
{
    /// <summary>
    /// The default microphone mute state.
    /// </summary>
    Microphone = 1,

    /// <summary>
    /// The ASUS keyboard-backlight level.
    /// </summary>
    KeyboardBacklight = 2,

    /// <summary>
    /// The built-in display brightness percentage.
    /// </summary>
    DisplayBrightness = 3,

    /// <summary>
    /// The combined ASUS performance and GPU mode.
    /// </summary>
    PerformanceGpuMode = 4,
}

/// <summary>
/// Represents one hardware status update sent to the lightweight UI.
/// </summary>
/// <param name="Kind">The hardware function represented by the update.</param>
/// <param name="Value">The integer state value for the hardware function.</param>
internal readonly record struct HardwareUiNotification(HardwareUiNotificationKind Kind, int Value);

/// <summary>
/// Handles command-line invocations for the lightweight Win32 hardware status UI.
/// </summary>
/// <remarks>
/// The Windows service starts the current executable in this mode inside the active interactive
/// user session. A short-lived invocation forwards its status to the resident UI instance through
/// a window message. If no UI instance exists yet, the invocation becomes that resident instance.
/// </remarks>
internal static class HardwareUiCommand
{
    /// <summary>
    /// Main command name used to enter UI mode.
    /// </summary>
    public const string CommandName = "ui";

    /// <summary>
    /// Subcommand used to stop a resident UI instance in the current user session.
    /// </summary>
    public const string ShutdownCommandName = "shutdown";

    /// <summary>
    /// Subcommand used for microphone mute status updates.
    /// </summary>
    public const string MicrophoneCommandName = "mic";

    /// <summary>
    /// Subcommand used for keyboard-backlight status updates.
    /// </summary>
    public const string KeyboardBacklightCommandName = "keyboard-backlight";

    /// <summary>
    /// Subcommand used for display-brightness status updates.
    /// </summary>
    public const string DisplayBrightnessCommandName = "brightness";

    /// <summary>
    /// Subcommand used for combined performance/GPU status updates.
    /// </summary>
    public const string PerformanceGpuCommandName = "performance-gpu";

    /// <summary>
    /// Tries to handle the supplied process arguments as a UI command.
    /// </summary>
    /// <param name="args">The process command-line arguments.</param>
    /// <param name="exitCode">The command exit code when handled; otherwise <c>0</c>.</param>
    /// <returns><see langword="true"/> when the process should exit after command handling.</returns>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !args[0].Equals(CommandName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        HardwareUiNotification? notification = null;
        if (args.Length > 1)
        {
            if (args[1].Equals(ShutdownCommandName, StringComparison.OrdinalIgnoreCase))
            {
                exitCode = HardwareUiWindow.Shutdown();
                return true;
            }

            if (args.Length < 3)
            {
                exitCode = 2;
                return true;
            }

            if (args[1].Equals(MicrophoneCommandName, StringComparison.OrdinalIgnoreCase))
            {
                var muted = args[2].ToLowerInvariant() switch
                {
                    "muted" => true,
                    "unmuted" => false,
                    _ => (bool?)null,
                };

                if (!muted.HasValue)
                {
                    exitCode = 2;
                    return true;
                }

                notification = new HardwareUiNotification(
                    HardwareUiNotificationKind.Microphone,
                    muted.Value ? 1 : 0);
            }
            else if (args[1].Equals(KeyboardBacklightCommandName, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(args[2], out var level) || level is < 0 or > 3)
                {
                    exitCode = 2;
                    return true;
                }

                notification = new HardwareUiNotification(HardwareUiNotificationKind.KeyboardBacklight, level);
            }
            else if (args[1].Equals(DisplayBrightnessCommandName, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(args[2], out var brightness) || brightness is < 0 or > 100)
                {
                    exitCode = 2;
                    return true;
                }

                notification = new HardwareUiNotification(HardwareUiNotificationKind.DisplayBrightness, brightness);
            }
            else if (args[1].Equals(PerformanceGpuCommandName, StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 4 ||
                    !Enum.TryParse<PerformanceMode>(args[2], true, out var performanceMode) ||
                    !Enum.TryParse<GpuMode>(args[3], true, out var gpuMode) ||
                    performanceMode is not (PerformanceMode.Balanced or PerformanceMode.Silent) ||
                    gpuMode is not (GpuMode.Eco or GpuMode.Standard))
                {
                    exitCode = 2;
                    return true;
                }

                var modeValue = (performanceMode == PerformanceMode.Silent ? 1 : 0) |
                                (gpuMode == GpuMode.Eco ? 2 : 0);
                notification = new HardwareUiNotification(HardwareUiNotificationKind.PerformanceGpuMode, modeValue);
            }
            else
            {
                exitCode = 2;
                return true;
            }
        }

        exitCode = HardwareUiWindow.Run(notification);
        return true;
    }
}
