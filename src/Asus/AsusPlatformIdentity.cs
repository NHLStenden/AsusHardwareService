using System.Management;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus;

/// <summary>
/// Provides lightweight ASUS platform-family information for firmware variants that cannot be distinguished by probing alone.
/// </summary>
internal sealed class AsusPlatformIdentity
{
    private readonly ILogger<AsusPlatformIdentity> _logger;
    private readonly Lazy<string> _model;

    /// <summary>Initializes a new instance of the <see cref="AsusPlatformIdentity"/> class.</summary>
    public AsusPlatformIdentity(ILogger<AsusPlatformIdentity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _model = new Lazy<string>(LoadModel, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the Windows-reported ASUS computer model.</summary>
    public string Model => _model.Value;

    /// <summary>Gets a value indicating whether the model belongs to the TUF/TX gaming family.</summary>
    public bool IsTuf =>
        ContainsModel("TUF") ||
        ContainsModel("TX Gaming") ||
        ContainsModel("TX Air");

    /// <summary>Gets a value indicating whether the model uses the VivoBook/Zenbook/ProArt ASUS firmware family.</summary>
    public bool IsVivoZenPro =>
        ContainsModel("Vivobook") ||
        ContainsModel("Zenbook") ||
        ContainsModel("ProArt") ||
        ContainsModel("EXPERTBOOK") ||
        ContainsModel(" V16") ||
        ContainsModel("ASUSLaptop");

    /// <summary>Gets a value indicating whether MiniLED probing needs the known ROG write-path fallback.</summary>
    public bool RequiresMiniLedWriteFallback =>
        ContainsModel("G834JYR") ||
        ContainsModel("G834JZR") ||
        ContainsModel("G634JZR") ||
        ContainsModel("G835L") ||
        ContainsModel("G635L");

    private bool ContainsModel(string value) =>
        Model.Contains(value, StringComparison.OrdinalIgnoreCase);

    private string LoadModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (ManagementObject computer in searcher.Get())
            {
                using (computer)
                {
                    var model = computer["Model"]?.ToString()?.Trim() ?? string.Empty;
                    _logger.LogInformation("Detected ASUS platform model {Model}.", model);
                    return model;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not read the Windows computer model.");
        }

        return string.Empty;
    }
}
