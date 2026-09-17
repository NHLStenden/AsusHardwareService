namespace AsusHardwareService.Configuration;

/// <summary>
/// Resolves immutable application files separately from machine-level mutable data.
/// </summary>
internal sealed class ApplicationPaths
{
    private const string ProductDataDirectoryName = "AsusHardwareService";
    private const string SettingsFileName = "appsettings.json";

    private ApplicationPaths(string contentRootPath, string settingsPath)
    {
        ContentRootPath = contentRootPath;
        SettingsPath = settingsPath;
    }

    /// <summary>Gets the directory containing the installed or development binaries.</summary>
    public string ContentRootPath { get; }

    /// <summary>Gets the settings file used by both configuration binding and persistence.</summary>
    public string SettingsPath { get; }

    /// <summary>
    /// Creates the path policy for the current process.
    /// A colocated settings file wins for development/manual deployments; packaged installs omit
    /// that file from the application directory and therefore use ProgramData.
    /// </summary>
    public static ApplicationPaths Create(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var normalizedContentRoot = Path.GetFullPath(contentRootPath);
        var localSettingsPath = Path.Combine(normalizedContentRoot, SettingsFileName);
        if (File.Exists(localSettingsPath))
        {
            return new ApplicationPaths(normalizedContentRoot, localSettingsPath);
        }

        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonApplicationData))
        {
            return new ApplicationPaths(normalizedContentRoot, localSettingsPath);
        }

        var machineSettingsPath = Path.Combine(
            commonApplicationData,
            ProductDataDirectoryName,
            SettingsFileName);

        return new ApplicationPaths(normalizedContentRoot, machineSettingsPath);
    }
}
