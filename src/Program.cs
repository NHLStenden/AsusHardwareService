using AsusHardwareService.Asus;
using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Asus.Battery;
using AsusHardwareService.Asus.Hid;
using AsusHardwareService.Asus.Keyboard;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Configuration;
using AsusHardwareService.Diagnostics;
using AsusHardwareService.Presentation;
using AsusHardwareService.Presentation.Osd;
using AsusHardwareService.Service;
using AsusHardwareService.Windows.Audio;
using AsusHardwareService.Windows.Display;
using AsusHardwareService.Windows.Processes;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging.EventLog;

if (VersionCommand.TryHandle(args, out var versionExitCode))
{
    return versionExitCode;
}

if (OsdCommand.TryHandle(args, out var osdExitCode))
{
    return osdExitCode;
}

if (DisplayCommand.TryHandle(args, out var displayExitCode))
{
    return displayExitCode;
}

var builder = Host.CreateApplicationBuilder(args);

var applicationPaths = ApplicationPaths.Create(builder.Environment.ContentRootPath);
if (!Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "appsettings.json"))
        .Equals(Path.GetFullPath(applicationPaths.SettingsPath), StringComparison.OrdinalIgnoreCase))
{
    // Host defaults already load the repository-local appsettings.json. Installed builds keep
    // mutable machine settings in ProgramData so upgrades can replace Program Files safely.
    builder.Configuration.AddJsonFile(applicationPaths.SettingsPath, optional: true, reloadOnChange: true);
}

builder.Services.AddSingleton(applicationPaths);

builder.Services.AddWindowsService(options => options.ServiceName = AsusHardwareService.ApplicationIdentity.ServiceName);

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(settings =>
{
    settings.LogName = "Application";
    settings.SourceName = AsusHardwareService.ApplicationIdentity.EventLogSourceName;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);

builder.Services
    .AddOptions<HardwareOptions>()
    .Bind(builder.Configuration.GetSection(HardwareOptions.SectionName));

// Use interfaces at presentation boundaries.
builder.Services.AddSingleton<AsusPlatformIdentity>();
builder.Services.AddSingleton<AsusAcpiClientFactory>();
builder.Services.AddSingleton<AsusAcpiCapabilities>();
builder.Services.AddSingleton<AsusHotkeyListener>();
builder.Services.AddSingleton<AsusKeyboardBacklightWriter>();
builder.Services.AddSingleton<BatteryChargeLimiter>();
builder.Services.AddSingleton<MutableHardwareSettingsStore>();
builder.Services.AddSingleton<HardwareSettingsCoordinator>();
builder.Services.AddSingleton<KeyboardBacklightController>();
builder.Services.AddSingleton<OperatingModeController>();
builder.Services.AddSingleton<SplendidProfileApplier>();
builder.Services.AddSingleton<DisplayBrightnessController>();
builder.Services.AddSingleton<DisplayTopologyController>();
builder.Services.AddSingleton<LaptopDisplayController>();
builder.Services.AddSingleton<MicrophoneMuteController>();
builder.Services.AddSingleton<UserSessionService>();
builder.Services.AddSingleton<SessionProcessLauncher>();

builder.Services.AddSingleton<OsdNotifier>();
builder.Services.AddSingleton<IHardwareStatusPublisher>(services => services.GetRequiredService<OsdNotifier>());
builder.Services.AddSingleton<IOnScreenDisplayLifecycle>(services => services.GetRequiredService<OsdNotifier>());
builder.Services.AddSingleton<IHardwareSettingsPresenter>(services => services.GetRequiredService<OsdNotifier>());

builder.Services.AddSingleton<StartupInitializer>();
builder.Services.AddSingleton<HotkeyHandler>();
builder.Services.AddSingleton<SessionMonitor>();
builder.Services.AddHostedService<HardwareService>();
builder.Services.AddHostedService<HardwareSettingsPipeServer>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
