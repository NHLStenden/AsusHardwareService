using AsusHardwareService.Asus.Display;
using AsusHardwareService.Asus.Acpi;
using AsusHardwareService.Asus.Battery;
using AsusHardwareService.Asus.Hid;
using AsusHardwareService.Asus.Keyboard;
using AsusHardwareService.Asus.Performance;
using AsusHardwareService.Asus.Splendid;
using AsusHardwareService.Configuration;
using AsusHardwareService.Presentation;
using AsusHardwareService.Presentation.Osd;
using AsusHardwareService.Service;
using AsusHardwareService.Windows.Audio;
using AsusHardwareService.Windows.Display;
using AsusHardwareService.Windows.Processes;
using AsusHardwareService.Windows.Sessions;
using Microsoft.Extensions.Logging.EventLog;

if (OsdCommand.TryHandle(args, out var osdExitCode))
{
    return osdExitCode;
}

if (DisplayCommand.TryHandle(args, out var displayExitCode))
{
    return displayExitCode;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "ASUS Hardware Service");

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(settings =>
{
    settings.LogName = "Application";
    settings.SourceName = "ASUS Hardware Service";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);

builder.Services
    .AddOptions<HardwareOptions>()
    .Bind(builder.Configuration.GetSection(HardwareOptions.SectionName));

// Concrete components are the default. Interfaces are reserved for the two presentation seams where
// orchestration genuinely benefits from depending on behavior rather than the Win32 OSD implementation.
builder.Services.AddSingleton<AsusAcpiClientFactory>();
builder.Services.AddSingleton<AsusHotkeyListener>();
builder.Services.AddSingleton<AsusKeyboardBacklightWriter>();
builder.Services.AddSingleton<BatteryChargeLimiter>();
builder.Services.AddSingleton<KeyboardBacklightController>();
builder.Services.AddSingleton<OperatingModeController>();
builder.Services.AddSingleton<SplendidProfileApplier>();
builder.Services.AddSingleton<DisplayBrightnessController>();
builder.Services.AddSingleton<LaptopDisplayController>();
builder.Services.AddSingleton<MicrophoneMuteController>();
builder.Services.AddSingleton<UserSessionService>();
builder.Services.AddSingleton<SessionProcessLauncher>();

builder.Services.AddSingleton<OsdNotifier>();
builder.Services.AddSingleton<IHardwareStatusPublisher>(services => services.GetRequiredService<OsdNotifier>());
builder.Services.AddSingleton<IOnScreenDisplayLifecycle>(services => services.GetRequiredService<OsdNotifier>());

builder.Services.AddSingleton<StartupInitializer>();
builder.Services.AddSingleton<HotkeyHandler>();
builder.Services.AddSingleton<SessionMonitor>();
builder.Services.AddHostedService<HardwareService>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
