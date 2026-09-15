using System.Collections.Frozen;
using HidSharp;
using HidSharp.Reports;

namespace AsusHardwareService.Asus.Hid;

/// <summary>Provides access to supported ASUS HID devices.</summary>
internal static class AsusHidDeviceCatalog
{
    internal const int AsusVendorId = 0x0B05;
    internal const byte InputReportId = 0x5A;

    private static readonly FrozenSet<int> SupportedProductIds = new[]
    {
        0x1A30, 0x1854, 0x1869, 0x1866, 0x19B6, 0x1822, 0x1837, 0x184A, 0x183D,
        0x8502, 0x1807, 0x17E0, 0x18C6, 0x1ABE, 0x1B4C, 0x1B6E, 0x1B2C, 0x8854,
    }.ToFrozenSet();

    /// <summary>Enumerates supported ASUS HID devices.</summary>
    internal static IEnumerable<HidDevice> GetSupportedDevices() =>
        DeviceList.Local.GetHidDevices(AsusVendorId).Where(IsSupportedDevice);

    private static bool IsSupportedDevice(HidDevice device) =>
        SupportedProductIds.Contains(device.ProductID) &&
        device.CanOpen &&
        device.GetMaxFeatureReportLength() > 0 &&
        device.GetReportDescriptor().TryGetReport(ReportType.Feature, InputReportId, out _);
}
