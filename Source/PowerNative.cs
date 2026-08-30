using System.Runtime.InteropServices;

namespace AsusHardwareService;
/// <summary>
/// Provides access to native Windows power status information.
/// </summary>
internal static class PowerNative
{
    /// <summary>
    /// Returns whether Windows currently reports AC power.
    /// </summary>
    /// <returns><see langword="true"/> when AC power is online; otherwise, <see langword="false"/>.</returns>
    public static bool IsOnAcPower()
    {
        return GetSystemPowerStatus(out var status) && status.ACLineStatus == 1;
    }
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
