namespace AsusHardwareService.Asus.Display;

/// <summary>Built-in laptop panel refresh-rate and overdrive presets.</summary>
internal enum LaptopDisplayMode
{
    /// <summary>Uses 240 Hz/overdrive on AC power and 60 Hz/no overdrive on battery power.</summary>
    Auto,

    /// <summary>Uses 60 Hz with panel overdrive disabled.</summary>
    Hz60,

    /// <summary>Uses 240 Hz with panel overdrive enabled.</summary>
    Hz240Overdrive,
}

/// <summary>MiniLED local-dimming modes exposed by ASUS firmware.</summary>
/// <remarks>
/// ASUS exposes two firmware endpoints with different numeric encodings. The mapping is kept in
/// <see cref="LaptopDisplayController"/> so these names remain independent of those protocol values.
/// </remarks>
internal enum MiniLedMode
{
    /// <summary>Uses one uniform backlight zone.</summary>
    OneZone,

    /// <summary>Uses normal multi-zone local dimming.</summary>
    MultiZone,

    /// <summary>Uses the stronger multi-zone mode where supported.</summary>
    MultiZoneStrong,
}
