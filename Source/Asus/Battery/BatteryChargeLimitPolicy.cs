namespace AsusHardwareService.Asus.Battery;

/// <summary>Defines the user-adjustable battery charge-limit values exposed by the service.</summary>
internal static class BatteryChargeLimitPolicy
{
    /// <summary>Smallest charge ceiling exposed to the user.</summary>
    internal const int MinimumPercent = 60;

    /// <summary>Largest charge ceiling exposed to the user.</summary>
    internal const int MaximumPercent = 100;

    /// <summary>Increment used by the charge-limit control.</summary>
    internal const int StepPercent = 5;

    /// <summary>Returns whether a percentage is one of the supported user-selectable values.</summary>
    internal static bool IsValid(int percentage) =>
        percentage is >= MinimumPercent and <= MaximumPercent &&
        (percentage - MinimumPercent) % StepPercent == 0;

    /// <summary>Clamps and snaps a configured value to the nearest supported user-selectable value.</summary>
    internal static int Normalize(int percentage)
    {
        var clamped = Math.Clamp(percentage, MinimumPercent, MaximumPercent);
        var step = (int)Math.Round(
            (clamped - MinimumPercent) / (double)StepPercent,
            MidpointRounding.AwayFromZero);
        return MinimumPercent + (step * StepPercent);
    }
}
