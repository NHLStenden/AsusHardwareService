namespace AsusHardwareService;
/// <summary>
/// Handles display-related command-line invocations that must run inside the interactive user session.
/// </summary>
/// <remarks>
/// The Windows service starts the current executable with this command mode when it needs to change
/// refresh rate. Keeping the logic inside the same executable avoids a separate helper binary while
/// still respecting the Windows session boundary.
/// </remarks>
internal static class DisplayCommand
{
    /// <summary>
    /// Main command name used to enter display command mode.
    /// </summary>
    public const string CommandName = "display";
    /// <summary>
    /// Subcommand for laptop panel refresh-rate control.
    /// </summary>
    public const string ScreenCommandName = "screen";

    /// <summary>
    /// Screen mode that chooses the refresh rate from the current power source.
    /// </summary>
    public const string ScreenModeAuto = "auto";

    /// <summary>
    /// Screen mode that requests 60 Hz.
    /// </summary>
    public const string ScreenMode60Hz = "60";
    /// <summary>
    /// Screen mode that requests 240 Hz and asks the service to use overdrive.
    /// </summary>
    public const string ScreenMode240HzOverdrive = "240-od";
    /// <summary>
    /// Tries to handle the supplied process arguments as a display command.
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

        exitCode = Run(args[1..]);
        return true;
    }
    /// <summary>
    /// Runs a display command.
    /// </summary>
    /// <param name="args">Arguments after the leading <c>display</c> command name.</param>
    /// <returns>A process exit code.</returns>
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                WriteUsage();
                return 2;
            }
            return args[0].ToLowerInvariant() switch
            {
                ScreenCommandName => ApplyScreen(args),
                "dump" => DumpDisplays(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
    private static int ApplyScreen(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Missing screen mode.");
            WriteUsage();
            return 2;
        }

        var mode = args[1].ToLowerInvariant();
        var targetHz = mode switch
        {
            ScreenModeAuto => PowerNative.IsOnAcPower() ? 240 : 60,
            ScreenMode60Hz => 60,
            ScreenMode240HzOverdrive => 240,
            _ => -1,
        };
        if (targetHz < 0)
        {
            Console.Error.WriteLine($"Unknown screen mode: {args[1]}");
            WriteUsage();
            return 2;
        }
        var display = ScreenNative.FindLaptopScreen(requireActive: true, preferredRefreshRate: targetHz);
        if (display is null)
        {
            Console.Error.WriteLine("No display candidates found in interactive session.");
            foreach (var line in ScreenNative.DumpDisplays())
            {
                Console.Error.WriteLine(line);
            }

            return 3;
        }
        var currentHz = ScreenNative.GetRefreshRate(display);
        if (currentHz == targetHz)
        {
            Console.WriteLine($"Refresh already {targetHz} Hz on {display}.");
            return 0;
        }

        var changed = ScreenNative.SetRefreshRate(display, targetHz);
        Console.WriteLine($"Refresh {currentHz} -> {targetHz} on {display}: {(changed ? "OK" : "Failed")}");

        return changed ? 0 : 4;
    }
    private static int DumpDisplays()
    {
        foreach (var line in ScreenNative.DumpDisplays())
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown display command: {command}");
        WriteUsage();
        return 2;
    }
    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  AsusHardwareService.exe display screen auto");
        Console.Error.WriteLine("  AsusHardwareService.exe display screen 60");
        Console.Error.WriteLine("  AsusHardwareService.exe display screen 240-od");
        Console.Error.WriteLine("  AsusHardwareService.exe display dump");
    }
}
