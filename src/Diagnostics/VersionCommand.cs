using System.Reflection;

namespace AsusHardwareService.Diagnostics;

/// <summary>Handles the lightweight version diagnostic without starting the service host.</summary>
internal static class VersionCommand
{
    /// <summary>Writes the product version for a <c>--version</c> or <c>version</c> invocation.</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 1 ||
            (!args[0].Equals("--version", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("version", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var assembly = Assembly.GetEntryAssembly() ?? typeof(VersionCommand).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informationalVersion;

        Console.WriteLine(version);
        return true;
    }
}
