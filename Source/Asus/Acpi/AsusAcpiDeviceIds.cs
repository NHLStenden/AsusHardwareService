namespace AsusHardwareService.Asus.Acpi;

/// <summary>
/// ASUS ACPI device identifiers used by this service.
/// </summary>
internal static class AsusAcpiDeviceIds
{
    internal const uint BatteryChargeLimit = 0x00120057;
    internal const uint KeyboardBacklight = 0x00050021;
    internal const uint ScreenOverdrive = 0x00050019;
    internal const uint MiniLedTwoState = 0x0005001E;
    internal const uint MiniLedThreeState = 0x0005002E;
    internal const uint PerformanceMode = 0x00120075;
    internal const uint GpuEco = 0x00090020;
    internal const uint GpuMux = 0x00090016;
}
