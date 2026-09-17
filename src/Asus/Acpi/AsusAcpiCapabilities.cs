using System.Collections.Concurrent;
using AsusHardwareService.Asus;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Asus.Acpi;

/// <summary>Describes the firmware encoding used by one ASUS performance-mode endpoint.</summary>
/// <param name="DeviceId">The ASUS ACPI device identifier.</param>
/// <param name="SwapsTurboAndSilent">Whether firmware values 1 and 2 are reversed.</param>
/// <param name="DiagnosticName">The diagnostic name used for ACPI logging.</param>
internal readonly record struct AsusPerformanceModeEndpoint(
    uint DeviceId,
    bool SwapsTurboAndSilent,
    string DiagnosticName);

/// <summary>Identifies the MiniLED value encoding exposed by an ASUS firmware endpoint.</summary>
internal enum AsusMiniLedEndpointKind
{
    /// <summary>The endpoint exposes one-zone and multi-zone states.</summary>
    TwoState,

    /// <summary>The endpoint additionally exposes the stronger multi-zone state.</summary>
    ThreeState,
}

/// <summary>Describes one supported ASUS MiniLED firmware endpoint.</summary>
/// <param name="DeviceId">The ASUS ACPI device identifier.</param>
/// <param name="Kind">The value encoding used by the endpoint.</param>
/// <param name="DiagnosticName">The diagnostic name used for ACPI logging.</param>
internal readonly record struct AsusMiniLedEndpoint(
    uint DeviceId,
    AsusMiniLedEndpointKind Kind,
    string DiagnosticName);

/// <summary>
/// Resolves ASUS firmware capabilities while preserving ROG endpoints as the default implementation.
/// </summary>
internal sealed class AsusAcpiCapabilities
{
    private readonly ConcurrentDictionary<uint, bool> _supportCache = new();
    private readonly AsusAcpiClientFactory _acpiFactory;
    private readonly AsusPlatformIdentity _platform;
    private readonly ILogger<AsusAcpiCapabilities> _logger;

    /// <summary>Initializes a new instance of the <see cref="AsusAcpiCapabilities"/> class.</summary>
    public AsusAcpiCapabilities(
        AsusAcpiClientFactory acpiFactory,
        AsusPlatformIdentity platform,
        ILogger<AsusAcpiCapabilities> logger)
    {
        _acpiFactory = acpiFactory ?? throw new ArgumentNullException(nameof(acpiFactory));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the supported performance-mode endpoint, preferring the ROG encoding.</summary>
    public AsusPerformanceModeEndpoint? GetPerformanceModeEndpoint()
    {
        if (IsSupported(AsusAcpiDeviceIds.PerformanceMode))
        {
            return new AsusPerformanceModeEndpoint(
                AsusAcpiDeviceIds.PerformanceMode,
                SwapsTurboAndSilent: false,
                "PerformanceMode");
        }

        if (IsSupported(AsusAcpiDeviceIds.PerformanceModeVivoBook))
        {
            return new AsusPerformanceModeEndpoint(
                AsusAcpiDeviceIds.PerformanceModeVivoBook,
                SwapsTurboAndSilent: true,
                "VivoBookPerformanceMode");
        }

        return null;
    }

    /// <summary>Gets the supported GPU Eco endpoint for the detected ASUS firmware family.</summary>
    public uint? GetGpuEcoEndpoint() => ResolveFamilyEndpoint(
        AsusAcpiDeviceIds.GpuEco,
        AsusAcpiDeviceIds.GpuEcoVivoBook);

    /// <summary>Gets the supported GPU MUX endpoint for the detected ASUS firmware family.</summary>
    public uint? GetGpuMuxEndpoint() => ResolveFamilyEndpoint(
        AsusAcpiDeviceIds.GpuMux,
        AsusAcpiDeviceIds.GpuMuxVivoBook);

    /// <summary>Gets the MiniLED endpoints in preferred protocol order, including known ROG firmware fallbacks.</summary>
    public IReadOnlyList<AsusMiniLedEndpoint> GetMiniLedEndpoints()
    {
        var endpoints = new List<AsusMiniLedEndpoint>(2);
        if (IsSupported(AsusAcpiDeviceIds.MiniLedThreeState) || _platform.RequiresMiniLedWriteFallback)
        {
            endpoints.Add(new AsusMiniLedEndpoint(
                AsusAcpiDeviceIds.MiniLedThreeState,
                AsusMiniLedEndpointKind.ThreeState,
                "MiniLED2"));
        }

        if (IsSupported(AsusAcpiDeviceIds.MiniLedTwoState))
        {
            endpoints.Add(new AsusMiniLedEndpoint(
                AsusAcpiDeviceIds.MiniLedTwoState,
                AsusMiniLedEndpointKind.TwoState,
                "MiniLED1"));
        }

        return endpoints;
    }

    /// <summary>Gets a value indicating whether panel overdrive is exposed by ASUS firmware.</summary>
    public bool IsScreenOverdriveSupported()
    {
        if (TryReadDeviceValue(AsusAcpiDeviceIds.ScreenOverdriveSupport, out var supportValue) && supportValue >= 0)
        {
            return supportValue == 1;
        }

        // Older firmware may expose the state endpoint without the dedicated support flag.
        return IsSupported(AsusAcpiDeviceIds.ScreenOverdrive);
    }

    /// <summary>Gets a value indicating whether TUF-style ACPI keyboard brightness is available.</summary>
    public bool IsTufKeyboardBacklightSupported() =>
        _platform.IsTuf && IsSupported(AsusAcpiDeviceIds.KeyboardBacklight);

    /// <summary>Determines whether an ASUS ACPI device identifier can be read successfully.</summary>
    public bool IsSupported(uint deviceId)
    {
        if (_supportCache.TryGetValue(deviceId, out var supported))
        {
            return supported;
        }

        if (!TryReadDeviceValue(deviceId, out var value))
        {
            return false;
        }

        supported = value >= 0;
        _supportCache.TryAdd(deviceId, supported);
        _logger.LogDebug(
            "ASUS ACPI endpoint 0x{DeviceId:X8} support={Supported} (value={Value}).",
            deviceId,
            supported,
            value);
        return supported;
    }

    private uint? ResolveFamilyEndpoint(uint rogEndpoint, uint vivoEndpoint)
    {
        if (_platform.IsVivoZenPro)
        {
            if (IsSupported(vivoEndpoint))
            {
                return vivoEndpoint;
            }

            return IsSupported(rogEndpoint) ? rogEndpoint : null;
        }

        if (IsSupported(rogEndpoint))
        {
            return rogEndpoint;
        }

        return IsSupported(vivoEndpoint) ? vivoEndpoint : null;
    }

    private bool TryReadDeviceValue(uint deviceId, out int value)
    {
        value = -1;
        try
        {
            using var acpi = _acpiFactory.Open();
            if (!acpi.IsConnected)
            {
                return false;
            }

            value = acpi.ReadDeviceValue(deviceId);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not probe ASUS ACPI endpoint 0x{DeviceId:X8}.",
                deviceId);
            return false;
        }
    }
}
