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

    /// <summary>Uses the turbo ASUS performance profile.</summary>
    Turbo = 1,

    /// <summary>Uses the silent ASUS performance profile.</summary>
    Silent = 2,
}

/// <summary>Named operating-mode presets exposed by the interactive settings UI.</summary>
internal enum OperatingModePreset
{
    /// <summary>Uses the silent performance profile with the discrete GPU disabled.</summary>
    Eco = 0,

    /// <summary>Uses balanced performance with the standard hybrid GPU mode.</summary>
    Normal = 1,

    /// <summary>Uses the ASUS turbo performance profile with the standard hybrid GPU mode.</summary>
    Turbo = 2,
}

/// <summary>Represents the paired performance/GPU state used by the service hotkey.</summary>
/// <remarks>The interactive settings UI also uses these pairs for its named operating-mode presets.</remarks>
/// <param name="Performance">The ASUS platform performance profile.</param>
/// <param name="Gpu">The ASUS GPU operating mode.</param>
internal readonly record struct HardwareOperatingMode(PerformanceMode Performance, GpuMode Gpu)
{
    /// <summary>The low-power pair used by the two-state toggle.</summary>
    public static HardwareOperatingMode LowPower { get; } = new(PerformanceMode.Silent, GpuMode.Eco);

    /// <summary>The normal-performance pair used by the two-state toggle.</summary>
    public static HardwareOperatingMode Normal { get; } = new(PerformanceMode.Balanced, GpuMode.Standard);

    /// <summary>The Turbo settings preset. Turbo is intentionally not part of the hardware-hotkey cycle.</summary>
    public static HardwareOperatingMode Turbo { get; } = new(PerformanceMode.Turbo, GpuMode.Standard);

    /// <summary>Returns the other supported pair in the two-state toggle cycle.</summary>
    public HardwareOperatingMode Toggle() => this == LowPower ? Normal : LowPower;

    /// <summary>Returns the hardware pair represented by an interactive settings preset.</summary>
    public static HardwareOperatingMode FromPreset(OperatingModePreset preset) => preset switch
    {
        OperatingModePreset.Eco => LowPower,
        OperatingModePreset.Normal => Normal,
        OperatingModePreset.Turbo => Turbo,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported operating-mode preset."),
    };

    /// <summary>Returns the interactive settings preset for this exact hardware pair, when one exists.</summary>
    public OperatingModePreset? ToPreset()
    {
        if (this == LowPower)
        {
            return OperatingModePreset.Eco;
        }

        if (this == Normal)
        {
            return OperatingModePreset.Normal;
        }

        return this == Turbo ? OperatingModePreset.Turbo : null;
    }
}
