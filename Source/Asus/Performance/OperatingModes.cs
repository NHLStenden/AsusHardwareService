namespace AsusHardwareService.Asus.Performance;

/// <summary>ASUS GPU modes controlled by the service.</summary>
internal enum GpuMode
{
    /// <summary>Uses the integrated-GPU-focused Eco mode.</summary>
    Eco = 0,

    /// <summary>Uses the standard hybrid GPU mode.</summary>
    Standard = 1,
}

/// <summary>ASUS platform performance profiles controlled by the service.</summary>
internal enum PerformanceMode
{
    /// <summary>Uses the balanced ASUS performance profile.</summary>
    Balanced = 0,

    /// <summary>Uses the silent ASUS performance profile.</summary>
    Silent = 2,
}

/// <summary>Represents the paired performance/GPU state used by the service hotkey.</summary>
/// <param name="Performance">The ASUS platform performance profile.</param>
/// <param name="Gpu">The ASUS GPU operating mode.</param>
internal readonly record struct HardwareOperatingMode(PerformanceMode Performance, GpuMode Gpu)
{
    /// <summary>The low-power pair used by the two-state toggle.</summary>
    public static HardwareOperatingMode LowPower { get; } = new(PerformanceMode.Silent, GpuMode.Eco);

    /// <summary>The normal-performance pair used by the two-state toggle.</summary>
    public static HardwareOperatingMode Normal { get; } = new(PerformanceMode.Balanced, GpuMode.Standard);

    /// <summary>Returns the other supported pair in the two-state toggle cycle.</summary>
    public HardwareOperatingMode Toggle() => this == LowPower ? Normal : LowPower;
}
