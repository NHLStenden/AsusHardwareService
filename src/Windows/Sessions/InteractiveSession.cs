namespace AsusHardwareService.Windows.Sessions;

/// <summary>
/// Describes a logged-on interactive Windows user session.
/// </summary>
/// <param name="SessionId">The Windows session identifier.</param>
/// <param name="UserName">The user name associated with the session.</param>
/// <param name="Domain">The user domain associated with the session.</param>
internal sealed record InteractiveSession(int SessionId, string UserName, string Domain);
