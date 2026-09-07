using System.Management;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Acpi;

/// <summary>Resolves files installed alongside the ASUS ACPI driver.</summary>
internal static class AsusDriverLocator
{
    internal const string DriverServiceName = "ATKWMIACPIIO";

    /// <summary>Attempts to locate a named companion file next to the installed ASUS driver binary.</summary>
    internal static string? TryResolveCompanionFile(string fileName, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(logger);

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

        var normalizedPath = pathName.Trim().Trim('"');
        var driverDirectory = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(driverDirectory))
        {
            logger.LogError("Could not determine the ASUS driver directory from {PathName}.", pathName);
            return null;
        }

        return driverDirectory;
    }
}
