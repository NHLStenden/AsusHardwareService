using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Configuration;
using AsusHardwareService.Presentation;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsusHardwareService.Service;

/// <summary>
/// Watches the active interactive Windows session and applies settings that cannot run from Session 0.
/// </summary>
internal sealed class SessionMonitor
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger<SessionMonitor> _logger;
    private readonly LaptopDisplayController _laptopDisplay;
    private readonly SplendidProfileApplier _colorProfile;
    private readonly IOnScreenDisplayLifecycle _onScreenDisplay;
    private readonly UserSessionService _interactiveSessions;
    private readonly IOptionsMonitor<HardwareOptions> _options;
    private int? _lastSessionId;

    /// <summary>Initializes the interactive-session monitor.</summary>
    public SessionMonitor(
        ILogger<SessionMonitor> logger,
        LaptopDisplayController laptopDisplay,
        SplendidProfileApplier colorProfile,
        IOnScreenDisplayLifecycle onScreenDisplay,
        UserSessionService interactiveSessions,
        IOptionsMonitor<HardwareOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _laptopDisplay = laptopDisplay ?? throw new ArgumentNullException(nameof(laptopDisplay));
        _colorProfile = colorProfile ?? throw new ArgumentNullException(nameof(colorProfile));
        _onScreenDisplay = onScreenDisplay ?? throw new ArgumentNullException(nameof(onScreenDisplay));
        _interactiveSessions = interactiveSessions ?? throw new ArgumentNullException(nameof(interactiveSessions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Runs until cancellation while tracking active interactive-session changes.</summary>
    /// <param name="stoppingToken">Signals service shutdown.</param>
    /// <returns>A task that completes after monitoring stops.</returns>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var session = await _interactiveSessions
                    .WaitForActiveSessionAsync(PollingInterval, stoppingToken)
                    .ConfigureAwait(false);
                if (session is null)
                {
                    return;
                }

                await ApplyForSessionAsync(session, stoppingToken).ConfigureAwait(false);
                await WaitForSessionChangeAsync(session.SessionId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error while monitoring the active interactive user session.");
                await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Closes resident OSD instances that may still belong to the service during an orderly stop.</summary>
    public void StopPresentations()
    {
        var activeSessionId = _interactiveSessions.GetActiveSession()?.SessionId;

        if (_lastSessionId.HasValue)
        {
            _onScreenDisplay.StopInSession(_lastSessionId.Value);
        }

        if (activeSessionId.HasValue && activeSessionId != _lastSessionId)
        {
            _onScreenDisplay.StopInSession(activeSessionId.Value);
        }
    }

    /// <summary>Applies configured user-session display and color settings to a newly active session.</summary>
    /// <param name="session">The interactive session receiving the settings.</param>
    /// <param name="stoppingToken">Signals service shutdown.</param>
    /// <returns>A task that completes after the color-profile launch sequence.</returns>
    public async Task ApplyForSessionAsync(InteractiveSession session, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_lastSessionId == session.SessionId)
        {
            return;
        }

        _logger.LogInformation(
            "Active interactive session detected. SessionId={SessionId}, User={Domain}\\{User}",
            session.SessionId,
            session.Domain,
            session.UserName);

        _lastSessionId = session.SessionId;
        _laptopDisplay.ApplyConfiguredUserSessionSettings(session);

        _logger.LogInformation("Applying user-session color profile for session {SessionId}.", session.SessionId);
        await Task.Delay(_options.CurrentValue.ColorProfileSessionDelayMilliseconds, stoppingToken).ConfigureAwait(false);
        var applied = await _colorProfile.ApplyConfiguredProfileAsync(session.SessionId, stoppingToken).ConfigureAwait(false);
        if (applied)
        {
            _logger.LogInformation("ASUS Splendid launch sequence succeeded for session {SessionId}.", session.SessionId);
        }
        else
        {
            _logger.LogWarning("ASUS Splendid launch sequence failed for session {SessionId}.", session.SessionId);
        }
    }

    private async Task WaitForSessionChangeAsync(int sessionId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
            var session = _interactiveSessions.GetActiveSession();
            if (session?.SessionId == sessionId)
            {
                continue;
            }

            _logger.LogInformation(
                "Interactive session changed. Previous={PreviousSessionId}, Current={CurrentSessionId}",
                sessionId,
                session?.SessionId);

            _onScreenDisplay.StopInSession(sessionId);
            if (session is null)
            {
                _lastSessionId = null;
            }

            return;
        }
    }
}
