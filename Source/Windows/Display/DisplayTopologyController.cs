using AsusHardwareService.Windows.Processes;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Windows.Display;

/// <summary>Starts display-topology changes inside the active interactive user session.</summary>
internal sealed class DisplayTopologyController
{
    private readonly ILogger<DisplayTopologyController> _logger;
    private readonly SessionProcessLauncher _processLauncher;
    private readonly UserSessionService _interactiveSessions;

    public DisplayTopologyController(
        ILogger<DisplayTopologyController> logger,
        SessionProcessLauncher processLauncher,
        UserSessionService interactiveSessions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _interactiveSessions = interactiveSessions ?? throw new ArgumentNullException(nameof(interactiveSessions));
    }

    /// <summary>Toggles between Windows' internal-only and external-only display topologies.</summary>
    public bool Toggle()
    {
        var session = _interactiveSessions.GetActiveSession();
        if (session is null)
        {
            _logger.LogDebug("Skipping display-topology toggle because no active interactive session is available.");
            return false;
        }

        var executablePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogWarning("Could not resolve the current executable path for the display-topology helper.");
            return false;
        }

        var arguments = $"{DisplayCommand.CommandName} {DisplayCommand.TopologyCommandName} {DisplayCommand.TopologyModeToggle}";
        var started = _processLauncher.TryStart(
            session.SessionId,
            executablePath,
            arguments,
            _logger,
            createConsoleWindow: false);

        if (!started)
        {
            _logger.LogWarning("Failed to start display-topology helper in session {SessionId}.", session.SessionId);
        }

        return started;
    }
}
