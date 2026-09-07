namespace AsusHardwareService.Asus.Splendid;

/// <summary>Color-gamut modes understood by ASUS Splendid.</summary>
internal enum SplendidGamutMode
{
    /// <summary>Uses the panel's native gamut.</summary>
    Native = 50,

    /// <summary>Uses the standard sRGB gamut.</summary>
    SRgb = 51,

    /// <summary>Uses the DCI-P3 gamut.</summary>
    DciP3 = 53,

    /// <summary>Uses the Display P3 gamut.</summary>
    DisplayP3 = 54,
}

/// <summary>Visual presets understood by ASUS Splendid.</summary>
internal enum SplendidVisualMode
{
    /// <summary>Uses the default visual preset.</summary>
    Default = 11,
    /// <summary>Uses the vivid visual preset.</summary>
    Vivid = 13,
    /// <summary>Uses the eye-care visual preset.</summary>
    EyeCare = 17,
    /// <summary>Disables the visual enhancement mode.</summary>
    Disabled = 18,
    /// <summary>Uses the racing visual preset.</summary>
    Racing = 21,
    /// <summary>Uses the scenery visual preset.</summary>
    Scenery = 22,
    /// <summary>Uses the RTS visual preset.</summary>
    Rts = 23,
    /// <summary>Uses the FPS visual preset.</summary>
    Fps = 24,
    /// <summary>Uses the cinema visual preset.</summary>
    Cinema = 25,
    /// <summary>Uses the e-reading visual preset.</summary>
    EReading = 212,
}
