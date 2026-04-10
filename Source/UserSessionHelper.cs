using System.Runtime.InteropServices;

namespace AsusHardwareService;

/// <summary>
/// Represents an interactive Windows user session.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>
    /// Gets the Windows session identifier.
    /// </summary>
    public required int SessionId { get; init; }

    /// <summary>
    /// Gets the user name associated with the session.
    /// </summary>
    public required string UserName { get; init; }

    /// <summary>
    /// Gets the domain associated with the session.
    /// </summary>
    public required string Domain { get; init; }
}

/// <summary>
/// Provides helpers for detecting the active interactive Windows user session.
/// </summary>
public static class UserSessionHelper
{
    private const uint InvalidSessionId = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pointer);

    /// <summary>
    /// Tries to retrieve the currently active interactive user session.
    /// </summary>
    /// <returns>
    /// A <see cref="SessionInfo"/> instance when an active interactive session with a logged-in
    /// user is available; otherwise, <see langword="null"/>.
    /// </returns>
    public static SessionInfo? TryGetActiveInteractiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == InvalidSessionId)
        {
            return null;
        }

        var activeSessionId = (int)sessionId;
        if (QueryConnectState(activeSessionId) != WtsConnectStateClass.WTSActive)
        {
            return null;
        }

        var userName = QueryString(activeSessionId, WtsInfoClass.WTSUserName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return new SessionInfo
        {
            SessionId = activeSessionId,
            UserName = userName,
            Domain = QueryString(activeSessionId, WtsInfoClass.WTSDomainName),
        };
    }

    /// <summary>
    /// Reads a Unicode string value from WTS session information.
    /// </summary>
    /// <param name="sessionId">The Windows session identifier.</param>
    /// <param name="infoClass">The WTS information class to read.</param>
    /// <returns>The resolved string value, or an empty string when unavailable.</returns>
    private static string QueryString(int sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// Reads the connection state for a Windows session.
    /// </summary>
    /// <param name="sessionId">The Windows session identifier.</param>
    /// <returns>The resolved connection state.</returns>
    private static WtsConnectStateClass QueryConnectState(int sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WtsInfoClass.WTSConnectState, out var buffer, out _))
        {
            return WtsConnectStateClass.WTSDown;
        }

        try
        {
            return (WtsConnectStateClass)Marshal.ReadInt32(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// Defines the WTS session information classes used by this helper.
    /// </summary>
    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSConnectState = 8,
    }

    /// <summary>
    /// Defines the connection states returned by the WTS API.
    /// </summary>
    private enum WtsConnectStateClass
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit,
    }
}
