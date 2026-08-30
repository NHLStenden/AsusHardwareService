using AsusHardwareService;
using Microsoft.Extensions.Logging.EventLog;

if (DisplayCommand.TryHandle(args, out var exitCode))
{
    return exitCode;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ASUS Hardware Service";
});

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(settings =>
{
    settings.LogName = "Application";
    settings.SourceName = "ASUS Hardware Service";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);

builder.Services.Configure<HardwareOptions>(builder.Configuration.GetSection("Hardware"));
builder.Services.AddTransient<AsusAcpi>();
builder.Services.AddSingleton<AsusHidInput>();
builder.Services.AddSingleton<BatteryChargeLimiter>();
builder.Services.AddSingleton<BrightnessController>();
builder.Services.AddSingleton<KeyboardBacklightController>();
builder.Services.AddSingleton<DisplayController>();
builder.Services.AddSingleton<SplendidProfileApplier>();
builder.Services.AddSingleton<MicController>();
builder.Services.AddSingleton<PerformanceGpuController>();
builder.Services.AddHostedService<HardwareServiceWorker>();
await builder.Build().RunAsync();
return 0;
