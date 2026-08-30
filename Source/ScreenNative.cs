using System.Runtime.InteropServices;

namespace AsusHardwareService;
/// <summary>
/// Provides native Windows display enumeration and refresh-rate switching helpers.
/// </summary>
/// <remarks>
/// These APIs must be called from the interactive user session. Calling them from a Windows service
/// running in session 0 can return no displays or fail to change the display mode.
/// </remarks>
internal static class ScreenNative
{
    private const int EnumCurrentSettings = -1;
    private const int CdsUpdateRegistry = 0x00000001;
    private const int CdsTest = 0x00000002;
    private const int DispChangeSuccessful = 0;
    private const int DisplayDeviceActive = 0x00000001;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDeviceMirroringDriver = 0x00000008;
    /// <summary>
    /// Finds the most likely laptop display device.
    /// </summary>
    /// <param name="requireActive">Whether only active displays should be considered.</param>
    /// <param name="preferredRefreshRate">A refresh rate that the selected display should preferably support.</param>
    /// <returns>The Windows display device name, or <see langword="null"/> when no suitable display is found.</returns>
    public static string? FindLaptopScreen(bool requireActive = false, int? preferredRefreshRate = null)
    {
        var candidates = GetDisplayCandidates(requireActive).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        if (preferredRefreshRate is > 0)
        {
            var exact = candidates
                .Where(candidate => candidate.RefreshRates.Contains(preferredRefreshRate.Value))
                .OrderByDescending(candidate => candidate.IsInternalLike)
                .ThenByDescending(candidate => candidate.MaxRefreshRate)
                .FirstOrDefault();

            if (exact is not null)
            {
                return exact.DeviceName;
            }
        }
        var highRefreshInternal = candidates
            .Where(candidate => candidate.MaxRefreshRate >= 120)
            .OrderByDescending(candidate => candidate.IsInternalLike)
            .ThenByDescending(candidate => candidate.MaxRefreshRate)
            .FirstOrDefault();

        if (highRefreshInternal is not null)
        {
            return highRefreshInternal.DeviceName;
        }
        var internalLike = candidates
            .OrderByDescending(candidate => candidate.IsInternalLike)
            .ThenBy(candidate => candidate.DeviceIndex)
            .FirstOrDefault();

        return internalLike?.DeviceName;
    }
    /// <summary>
    /// Returns diagnostic descriptions for all detected display candidates.
    /// </summary>
    /// <returns>A list of diagnostic display descriptions.</returns>
    public static IReadOnlyList<string> DumpDisplays()
    {
        return GetDisplayCandidates(requireActive: false)
            .Select(candidate => candidate.ToLogLine())
            .ToArray();
    }
    /// <summary>
    /// Reads the current refresh rate for a Windows display device.
    /// </summary>
    /// <param name="deviceName">The Windows display device name.</param>
    /// <returns>The current refresh rate, or <c>-1</c> when unavailable.</returns>
    public static int GetRefreshRate(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return -1;
        }
        var mode = DevMode.Create();
        return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)
            ? (int)mode.dmDisplayFrequency
            : -1;
    }
    /// <summary>
    /// Gets the maximum advertised refresh rate for a Windows display device.
    /// </summary>
    /// <param name="deviceName">The Windows display device name.</param>
    /// <returns>The maximum refresh rate, or <c>-1</c> when unavailable.</returns>
    public static int GetMaxRefreshRate(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return -1;
        }

        var max = -1;
        var mode = DevMode.Create();
        for (var index = 0; EnumDisplaySettings(deviceName, index, ref mode); index++)
        {
            max = Math.Max(max, (int)mode.dmDisplayFrequency);
            mode = DevMode.Create();
        }

        return max;
    }
    /// <summary>
    /// Sets the refresh rate for a Windows display device.
    /// </summary>
    /// <param name="deviceName">The Windows display device name.</param>
    /// <param name="refreshRate">The desired refresh rate in Hz.</param>
    /// <returns><see langword="true"/> when the change was accepted by Windows; otherwise, <see langword="false"/>.</returns>
    public static bool SetRefreshRate(string? deviceName, int refreshRate)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }
        var mode = DevMode.Create();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
        {
            return false;
        }

        mode.dmDisplayFrequency = (uint)refreshRate;
        mode.dmFields |= DisplayModeField.DisplayFrequency;

        var test = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != DispChangeSuccessful)
        {
            return false;
        }
        var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        return result == DispChangeSuccessful;
    }

    private static IEnumerable<DisplayCandidate> GetDisplayCandidates(bool requireActive)
    {
        var adapter = DisplayDevice.Create();
        for (uint adapterIndex = 0; EnumDisplayDevices(null, adapterIndex, ref adapter, 0); adapterIndex++)
        {
            var active = (adapter.StateFlags & DisplayDeviceActive) != 0;
            var attached = (adapter.StateFlags & DisplayDeviceAttachedToDesktop) != 0;
            var mirror = (adapter.StateFlags & DisplayDeviceMirroringDriver) != 0;
            if ((!active && requireActive) || !attached || mirror)
            {
                adapter = DisplayDevice.Create();
                continue;
            }

            var deviceName = adapter.DeviceName;
            var refreshRates = GetRefreshRates(deviceName);
            var maxRefresh = refreshRates.Count == 0 ? -1 : refreshRates.Max();
            var monitor = DisplayDevice.Create();
            var monitorName = string.Empty;
            var monitorId = string.Empty;
            for (uint monitorIndex = 0; EnumDisplayDevices(deviceName, monitorIndex, ref monitor, 0); monitorIndex++)
            {
                if ((monitor.StateFlags & DisplayDeviceActive) != 0 || !requireActive)
                {
                    monitorName = monitor.DeviceString ?? string.Empty;
                    monitorId = monitor.DeviceID ?? string.Empty;
                    break;
                }

                monitor = DisplayDevice.Create();
            }
            var isInternalLike =
                monitorId.Contains(@"DISPLAY\", StringComparison.OrdinalIgnoreCase) &&
                !monitorName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !monitorName.Contains("Remote", StringComparison.OrdinalIgnoreCase) &&
                !monitorName.Contains("Mirage", StringComparison.OrdinalIgnoreCase);
            yield return new DisplayCandidate(
                adapterIndex,
                deviceName,
                adapter.DeviceString ?? string.Empty,
                monitorName,
                monitorId,
                active,
                attached,
                refreshRates,
                maxRefresh,
                isInternalLike);

            adapter = DisplayDevice.Create();
        }
    }
    private static HashSet<int> GetRefreshRates(string deviceName)
    {
        var rates = new HashSet<int>();
        var mode = DevMode.Create();

        for (var index = 0; EnumDisplaySettings(deviceName, index, ref mode); index++)
        {
            if (mode.dmDisplayFrequency > 0)
            {
                rates.Add((int)mode.dmDisplayFrequency);
            }

            mode = DevMode.Create();
        }

        return rates;
    }
    private sealed record DisplayCandidate(
        uint DeviceIndex,
        string DeviceName,
        string AdapterName,
        string MonitorName,
        string MonitorId,
        bool Active,
        bool Attached,
        HashSet<int> RefreshRates,
        int MaxRefreshRate,
        bool IsInternalLike)
    {
        public string ToLogLine()
        {
            var rates = RefreshRates.Count == 0
                ? "none"
                : string.Join(',', RefreshRates.OrderBy(rate => rate));
            return $"{DeviceName} adapter='{AdapterName}' monitor='{MonitorName}' id='{MonitorId}' active={Active} attached={Attached} internalLike={IsInternalLike} maxHz={MaxRefreshRate} rates=[{rates}]";
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DisplayDevice lpDisplayDevice,
        uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string? lpszDeviceName,
        int iModeNum,
        ref DevMode lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DevMode lpDevMode,
        IntPtr hwnd,
        int dwflags,
        IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DisplayDevice Create()
        {
            return new DisplayDevice
            {
                cb = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty,
            };
        }
    }
    [Flags]
    private enum DisplayModeField : uint
    {
        DisplayFrequency = 0x00400000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        private const int DeviceNameSize = 32;
        private const int FormNameSize = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameSize)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public DisplayModeField dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FormNameSize)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
        public static DevMode Create()
        {
            return new DevMode
            {
                dmSize = (ushort)Marshal.SizeOf<DevMode>(),
                dmDeviceName = string.Empty,
                dmFormName = string.Empty,
            };
        }
    }
}
