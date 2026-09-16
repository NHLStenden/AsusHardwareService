using AsusHardwareService.Windows.Power;

namespace AsusHardwareService.Windows.Display;
/// <summary>
/// Handles display-related command-line invocations that must run in the user session.
/// </summary>
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

    /// <summary>Subcommand for Windows display-topology control.</summary>
    public const string TopologyCommandName = "topology";

    /// <summary>Topology mode that toggles between internal-only and external-only.</summary>
    public const string TopologyModeToggle = "toggle";

    /// <summary>
    /// Screen mode that chooses the refresh rate from the current power source.
    /// </summary>
    public const string ScreenModeAuto = "auto";

    /// <summary>
    /// Screen mode that requests 60 Hz.
    /// </summary>
    public const string ScreenMode60Hz = "60";

    /// <summary>Screen mode that requests the maximum supported refresh rate with overdrive.</summary>
    public const string ScreenModeMaxRefreshOverdrive = "max-od";

    /// <summary>Legacy alias for the maximum-refresh-rate overdrive mode.</summary>
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
                TopologyCommandName => ApplyTopology(args),
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
        var validMode = mode == ScreenModeAuto ||
            mode == ScreenMode60Hz ||
            mode == ScreenModeMaxRefreshOverdrive ||
            mode == ScreenMode240HzOverdrive;
        if (!validMode)
        {
            Console.Error.WriteLine($"Unknown screen mode: {args[1]}");
            WriteUsage();
            return 2;
        }

        var prefers60Hz = mode == ScreenMode60Hz || (mode == ScreenModeAuto && !PowerStatus.IsOnAcPower());
        var display = DisplayNativeMethods.FindLaptopScreen(
            requireActive: true,
            preferredRefreshRate: prefers60Hz ? 60 : null);
        if (display is null)
        {
            Console.Error.WriteLine("No display candidates found in interactive session.");
            foreach (var line in DisplayNativeMethods.DumpDisplays())
            {
                Console.Error.WriteLine(line);
            }

            return 3;
        }

        var refreshRates = DisplayNativeMethods.GetRefreshRates(display);
        if (refreshRates.Count == 0)
        {
            Console.Error.WriteLine($"No refresh rates found for {display} at the current resolution.");
            return 3;
        }

        var targetHz = ResolveTargetRefreshRate(mode, refreshRates);
        if (targetHz < 0)
        {
            Console.Error.WriteLine($"Requested screen mode {args[1]} is not supported on {display}.");
            return 4;
        }

        var currentHz = DisplayNativeMethods.GetRefreshRate(display);
        if (currentHz == targetHz)
        {
            Console.WriteLine($"Refresh already {targetHz} Hz on {display}.");
            return 0;
        }

        var changed = DisplayNativeMethods.SetRefreshRate(display, targetHz);
        Console.WriteLine($"Refresh {currentHz} -> {targetHz} on {display}: {(changed ? "OK" : "Failed")}");

        return changed ? 0 : 4;
    }

    private static int ResolveTargetRefreshRate(string mode, IReadOnlyCollection<int> refreshRates)
    {
        if (mode == ScreenMode60Hz)
        {
            return refreshRates.Contains(60) ? 60 : -1;
        }

        if (mode == ScreenModeAuto && !PowerStatus.IsOnAcPower())
        {
            return refreshRates.Contains(60) ? 60 : refreshRates.Min();
        }

        return refreshRates.Max();
    }

    private static int ApplyTopology(string[] args)
    {
        if (args.Length < 2 || !args[1].Equals(TopologyModeToggle, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Missing or unknown topology mode.");
            WriteUsage();
            return 2;
        }

        var current = DisplayNativeMethods.GetCurrentTopology();
        if (!current.HasValue)
        {
            Console.Error.WriteLine("Could not determine the current Windows display topology.");
            return 3;
        }

        var requested = current.Value == DisplayTopology.External
            ? DisplayTopology.Internal
            : DisplayTopology.External;

        var changed = DisplayNativeMethods.SetDisplayTopology(requested);
        Console.WriteLine($"Display topology {current.Value} -> {requested}: {(changed ? "OK" : "Failed")}");
        return changed ? 0 : 4;
    }

    private static int DumpDisplays()
    {
        foreach (var line in DisplayNativeMethods.DumpDisplays())
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
        Console.Error.WriteLine("  AsusHardwareService.exe display screen max-od");
        Console.Error.WriteLine("  AsusHardwareService.exe display topology toggle");
        Console.Error.WriteLine("  AsusHardwareService.exe display dump");
    }
}
