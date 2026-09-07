namespace AsusHardwareService.Asus.Hid;

/// <summary>
/// Logical hardware actions emitted by the ASUS hotkey input adapter.
/// </summary>
internal enum AsusHotkey
{
    /// <summary>The raw ASUS event is not mapped to a service action.</summary>
    Unknown = 0,

    /// <summary>Decrease keyboard-backlight brightness.</summary>
    KeyboardBacklightDecrease,

    /// <summary>Increase keyboard-backlight brightness.</summary>
    KeyboardBacklightIncrease,

    /// <summary>Decrease built-in display brightness.</summary>
    DisplayBrightnessDecrease,

    /// <summary>Increase built-in display brightness.</summary>
    DisplayBrightnessIncrease,

    /// <summary>Toggle the default capture-device mute state.</summary>
    MicrophoneMuteToggle,

    /// <summary>Toggle the paired ASUS performance/GPU mode.</summary>
    OperatingModeToggle,

    /// <summary>The model-dependent ASUS application/ROG key.</summary>
    VendorApplicationKey,
}

/// <summary>
/// A logical hotkey action together with its original ASUS HID event identifier.
/// </summary>
/// <param name="Hotkey">The logical service action.</param>
/// <param name="RawEventId">The raw event identifier reported by ASUS HID firmware.</param>
internal readonly record struct AsusHotkeyEvent(AsusHotkey Hotkey, int RawEventId);

/// <summary>Maps ASUS firmware event identifiers to stable application-level hotkey actions.</summary>
internal static class AsusHotkeyMapper
{
    internal static AsusHotkey Map(int eventId) => eventId switch
    {
        197 => AsusHotkey.KeyboardBacklightDecrease,
        196 => AsusHotkey.KeyboardBacklightIncrease,
        16 => AsusHotkey.DisplayBrightnessDecrease,
        32 => AsusHotkey.DisplayBrightnessIncrease,
        124 => AsusHotkey.MicrophoneMuteToggle,
        174 => AsusHotkey.OperatingModeToggle,
        56 => AsusHotkey.VendorApplicationKey,
        _ => AsusHotkey.Unknown,
    };
}
