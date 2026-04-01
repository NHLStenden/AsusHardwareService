using AsusHardwareService;
using Microsoft.Extensions.Logging.EventLog;

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

builder.Services.Configure<HardwareOptions>(
    builder.Configuration.GetSection("Hardware"));

builder.Services.AddTransient<AsusAcpi>();
builder.Services.AddSingleton<AsusHidInput>();
builder.Services.AddSingleton<BrightnessController>();
builder.Services.AddSingleton<BatteryChargeLimiter>();
builder.Services.AddSingleton<ColorProfileApplier>();
builder.Services.AddHostedService<HardwareServiceWorker>();

await builder.Build().RunAsync();