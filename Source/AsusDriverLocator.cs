using System.Management;

namespace AsusHardwareService;

/// <summary>
/// Resolves paths that belong to the installed ASUS ACPI driver package.
/// </summary>
internal static class AsusDriverLocator
{
    /// <summary>
    /// ASUS ACPI driver service name used by the ASUS hardware endpoints.
    /// </summary>
    public const string DriverServiceName = "ATKWMIACPIIO";
    /// <summary>
    /// Tries to resolve a file located next to the installed ASUS ACPI driver binary.
    /// </summary>
    /// <param name="fileName">The file name to locate in the ASUS driver directory.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <returns>The full file path when it exists; otherwise, <see langword="null"/>.</returns>
    public static string? TryResolveDriverSiblingFile(string fileName, ILogger logger)
    {
        var driverDirectory = TryGetDriverDirectory(logger);
        if (string.IsNullOrWhiteSpace(driverDirectory))
        {
            return null;
        }
        var filePath = Path.Combine(driverDirectory, fileName);
        if (File.Exists(filePath))
        {
            logger.LogInformation("Resolved ASUS driver companion file {FileName}: {Path}", fileName, filePath);
            return filePath;
        }

        logger.LogError("ASUS driver companion file {FileName} was not found at {Path}.", fileName, filePath);
        return null;
    }
    /// <summary>
    /// Tries to resolve the directory that contains the installed ASUS ACPI driver binary.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <returns>The driver directory when available; otherwise, <see langword="null"/>.</returns>
    private static string? TryGetDriverDirectory(ILogger logger)
    {
        using ManagementObjectSearcher searcher = new(
            $"SELECT Name, PathName FROM Win32_SystemDriver WHERE Name = '{DriverServiceName}'");
        using var results = searcher.Get();
        var driver = results.Cast<ManagementObject>().FirstOrDefault();
        if (driver is null)
        {
            logger.LogError("{DriverName} driver not found.", DriverServiceName);
            return null;
        }

        var pathName = driver["PathName"]?.ToString();
        if (string.IsNullOrWhiteSpace(pathName))
        {
            logger.LogError("{DriverName} driver path is empty.", DriverServiceName);
            return null;
        }
        var normalisedPath = pathName.Trim().Trim('"');
        var driverDirectory = Path.GetDirectoryName(normalisedPath);
        if (string.IsNullOrWhiteSpace(driverDirectory))
        {
            logger.LogError("Could not determine the ASUS driver directory from {PathName}.", pathName);
            return null;
        }

        return driverDirectory;
    }
}
