using AsusHardwareService.Asus.Hid;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Service;

/// <summary>
/// Runs the hardware service.
/// </summary>
internal sealed class HardwareService : BackgroundService
{
    private readonly ILogger<HardwareService> _logger;
    private readonly AsusHotkeyListener _hotkeyListener;
    private readonly StartupInitializer _startup;
    private readonly HotkeyHandler _hotkeys;
    private readonly SessionMonitor _sessions;

    public HardwareService(
        ILogger<HardwareService> logger,
        AsusHotkeyListener hotkeyListener,
        StartupInitializer startup,
        HotkeyHandler hotkeys,
        SessionMonitor sessions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hotkeyListener = hotkeyListener ?? throw new ArgumentNullException(nameof(hotkeyListener));
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ASUS Hardware Service started in Windows Session 0.");
        await _startup.InitializeAsync(stoppingToken).ConfigureAwait(false);

        // HidSharp performs a blocking report read.
        var hotkeyTask = Task.Run(
            () => _hotkeyListener.ListenAsync(
                hotkey => _hotkeys.DispatchAsync(hotkey, stoppingToken),
                stoppingToken),
            stoppingToken);
        var sessionTask = _sessions.RunAsync(stoppingToken);

        await Task.WhenAll(hotkeyTask, sessionTask).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.StopPresentations();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
