using System.Management;
using Microsoft.Extensions.Options;

namespace AsusHardwareService;

/// <summary>
/// Defines the color gamut modes supported by ASUS Splendid.
/// </summary>
public enum SplendidGamut
{
    /// <summary>
    /// Uses the panel's native gamut.
    /// </summary>
    Native = 50,

    /// <summary>
    /// Uses the standard sRGB gamut.
    /// </summary>
    sRgb = 51,

    /// <summary>
    /// Uses the DCI-P3 gamut.
    /// </summary>
    DciP3 = 53,

    /// <summary>
    /// Uses the Display P3 gamut.
    /// </summary>
    DisplayP3 = 54,
}

/// <summary>
/// Defines the visual modes supported by ASUS Splendid.
/// </summary>
public enum SplendidVisual
{
    /// <summary>
    /// Initialises ASUS Splendid.
    /// </summary>
    Init = 10,

    /// <summary>
    /// Switches the gamut mode.
    /// </summary>
    GamutMode = 200,

    /// <summary>
    /// Uses the default visual preset.
    /// </summary>
    Default = 11,

    /// <summary>
    /// Uses the racing visual preset.
    /// </summary>
    Racing = 21,

    /// <summary>
    /// Uses the scenery visual preset.
    /// </summary>
    Scenery = 22,

    /// <summary>
    /// Uses the RTS visual preset.
    /// </summary>
    Rts = 23,

    /// <summary>
    /// Uses the FPS visual preset.
    /// </summary>
    Fps = 24,

    /// <summary>
    /// Uses the cinema visual preset.
    /// </summary>
    Cinema = 25,

    /// <summary>
    /// Uses the vivid visual preset.
    /// </summary>
    Vivid = 13,

    /// <summary>
    /// Uses the eye care visual preset.
    /// </summary>
    EyeCare = 17,

    /// <summary>
    /// Uses the e-reading visual preset.
    /// </summary>
    EReading = 212,

    /// <summary>
    /// Disables the visual enhancement mode.
    /// </summary>
    Disabled = 18,
}

/// <summary>
/// Locates and launches ASUS Splendid display color commands inside a user session.
/// </summary>
public sealed class ColorProfileApplier
{
    private const string DriverName = "ATKWMIACPIIO";
    private const string SplendidExecutableName = "AsusSplendid.exe";
    private const int DefaultIntensity = 50;

    private readonly ILogger<ColorProfileApplier> _logger;
    private readonly HardwareOptions _options;

    /// <summary>
    /// Initialises a new instance of the <see cref="ColorProfileApplier"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and errors.</param>
    /// <param name="options">The configured hardware options.</param>
    public ColorProfileApplier(ILogger<ColorProfileApplier> logger, IOptions<HardwareOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Applies the configured ASUS Splendid initialisation sequence in the specified user session.
    /// </summary>
    /// <param name="sessionId">The interactive Windows session identifier.</param>
    /// <returns>
    /// <see langword="true"/> if the full sequence completed successfully; otherwise, <see langword="false"/>.
    /// </returns>
    public async Task<bool> ApplyAsync(int sessionId)
    {
        var executablePath = TryGetSplendidExePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var commands = BuildCommandSequence();
        foreach (var command in commands)
        {
            if (!InvokeSplendidInSession(sessionId, executablePath, command.Command, command.Param1, command.Param2, command.Param3))
            {
                return false;
            }

            if (command.DelayAfterCommand)
            {
                await Task.Delay(_options.ColorProfileCommandDelay);
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to resolve the full path of <c>AsusSplendid.exe</c> based on the installed ASUS driver path.
    /// </summary>
    /// <returns>
    /// The full path to <c>AsusSplendid.exe</c> when found; otherwise, <see langword="null"/>.
    /// </returns>
    private string? TryGetSplendidExePath()
    {
        using ManagementObjectSearcher searcher =
            new($"SELECT Name, PathName FROM Win32_SystemDriver WHERE Name = '{DriverName}'");

        using var results = searcher.Get();
        var driver = results.Cast<ManagementObject>().FirstOrDefault();
        if (driver is null)
        {
            _logger.LogError("{DriverName} driver not found.", DriverName);
            return null;
        }

        var pathName = driver["PathName"]?.ToString();
        if (string.IsNullOrWhiteSpace(pathName))
        {
            _logger.LogError("{DriverName} driver path is empty.", DriverName);
            return null;
        }

        var normalisedPath = pathName.Trim().Trim('"');
        var basePath = Path.GetDirectoryName(normalisedPath);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            _logger.LogError("Could not determine the ASUS driver directory.");
            return null;
        }

        var executablePath = Path.Combine(basePath, SplendidExecutableName);
        if (!File.Exists(executablePath))
        {
            _logger.LogError("{ExecutableName} not found at: {Path}", SplendidExecutableName, executablePath);
            return null;
        }

        _logger.LogInformation("Resolved {ExecutableName} path: {Path}", SplendidExecutableName, executablePath);
        return executablePath;
    }

    /// <summary>
    /// Launches a single ASUS Splendid command in the specified user session.
    /// </summary>
    /// <param name="sessionId">The interactive Windows session identifier.</param>
    /// <param name="executablePath">The full path to <c>AsusSplendid.exe</c>.</param>
    /// <param name="command">The ASUS Splendid command identifier.</param>
    /// <param name="param1">The optional first command parameter.</param>
    /// <param name="param2">The optional second command parameter.</param>
    /// <param name="param3">The optional third command parameter.</param>
    /// <returns>
    /// <see langword="true"/> if the process launch succeeded; otherwise, <see langword="false"/>.
    /// </returns>
    private bool InvokeSplendidInSession(
        int sessionId,
        string executablePath,
        int command,
        int? param1 = null,
        int? param2 = null,
        int? param3 = null)
    {
        var arguments = BuildArguments(command, param1, param2, param3);

        _logger.LogInformation(
            "Launching ASUS Splendid in session {SessionId}: \"{ExecutablePath}\" {Arguments}",
            sessionId,
            executablePath,
            arguments);

        return SessionProcessLauncher.TryStartInSession(sessionId, executablePath, arguments, _logger);
    }

    /// <summary>
    /// Builds the command-line argument string for an ASUS Splendid command.
    /// </summary>
    /// <param name="command">The ASUS Splendid command identifier.</param>
    /// <param name="param1">The optional first command parameter.</param>
    /// <param name="param2">The optional second command parameter.</param>
    /// <param name="param3">The optional third command parameter.</param>
    /// <returns>A space-separated command-line argument string.</returns>
    private static string BuildArguments(
        int command,
        int? param1,
        int? param2,
        int? param3)
    {
        List<string> parts = [command.ToString()];

        if (param1.HasValue)
        {
            parts.Add(param1.Value.ToString());
        }

        if (param2.HasValue)
        {
            parts.Add(param2.Value.ToString());
        }

        if (param3.HasValue)
        {
            parts.Add(param3.Value.ToString());
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Builds the ordered ASUS Splendid command sequence for the configured colour profile.
    /// </summary>
    /// <returns>The ordered commands to execute.</returns>
    private IReadOnlyList<SplendidCommand> BuildCommandSequence()
    {
        List<SplendidCommand> commands = [
            new((int)SplendidVisual.Init),
        ];

        if (_options.ColorProfileToDefault)
        {
            commands.Add(new((int)SplendidVisual.GamutMode, 0, (int)SplendidGamut.Native));
            commands.Add(new((int)SplendidVisual.Default, 0, DefaultIntensity));
        }

        commands.Add(new((int)SplendidVisual.GamutMode, 0, (int)_options.GamutMode));
        commands.Add(new((int)_options.VisualMode, 0, _options.ColorTemperature));

        return commands;
    }

    /// <summary>
    /// Represents a single ASUS Splendid command invocation.
    /// </summary>
    /// <param name="Command">The command identifier.</param>
    /// <param name="Param1">The optional first parameter.</param>
    /// <param name="Param2">The optional second parameter.</param>
    /// <param name="Param3">The optional third parameter.</param>
    /// <param name="DelayAfterCommand"><see langword="true"/> when the standard inter-command delay should be applied.</param>
    private sealed record SplendidCommand(
        int Command,
        int? Param1 = null,
        int? Param2 = null,
        int? Param3 = null,
        bool DelayAfterCommand = true);
}
