using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Acpi;

/// <summary>Default factory for ASUS ACPI clients.</summary>
internal sealed class AsusAcpiClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes a new factory.</summary>
    public AsusAcpiClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>Opens a new short-lived ASUS ACPI device client.</summary>
    public AsusAcpiClient Open() => new(_loggerFactory.CreateLogger<AsusAcpiClient>());
}
