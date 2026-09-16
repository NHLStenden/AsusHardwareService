namespace AsusHardwareService.Asus.Acpi;

/// <summary>
/// ASUS ACPI device identifiers used by this service.
/// </summary>
internal static class AsusAcpiDeviceIds
{
    internal const uint BatteryChargeLimit = 0x00120057;
    internal const uint KeyboardBacklight = 0x00050021;
    internal const uint ScreenOverdrive = 0x00050019;
    internal const uint ScreenOverdriveSupport = 0x00050020;
    internal const uint MiniLedTwoState = 0x0005001E;
    internal const uint MiniLedThreeState = 0x0005002E;
    internal const uint PerformanceMode = 0x00120075;
    internal const uint PerformanceModeVivoBook = 0x00110019;
    internal const uint GpuEco = 0x00090020;
    internal const uint GpuEcoVivoBook = 0x00090120;
    internal const uint GpuMux = 0x00090016;
    internal const uint GpuMuxVivoBook = 0x00090026;
}
