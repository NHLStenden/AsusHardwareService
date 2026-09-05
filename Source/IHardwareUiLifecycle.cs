namespace AsusHardwareService;

/// <summary>
/// Controls the lifetime of the resident hardware UI associated with interactive user sessions.
/// </summary>
public interface IHardwareUiLifecycle
{
    /// <summary>
    /// Requests that the resident hardware UI in a specific user session exits cleanly.
    /// </summary>
    /// <param name="sessionId">The Windows session containing the resident UI.</param>
    void StopUiInSession(int sessionId);
}
