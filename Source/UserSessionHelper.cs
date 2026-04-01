using System.Runtime.InteropServices;

namespace AsusHardwareService;

/// <summary>
/// Represents an interactive Windows user session.
/// </summary>
public sealed class SessionInfo
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
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == InvalidSessionId)
        {
            return null;
        }

        int activeSessionId = (int)sessionId;

        WtsConnectStateClass state = QueryConnectState(activeSessionId);
        if (state != WtsConnectStateClass.WTSActive)
        {
            return null;
        }

        string userName = QueryString(activeSessionId, WtsInfoClass.WTSUserName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        string domain = QueryString(activeSessionId, WtsInfoClass.WTSDomainName);

        return new SessionInfo
        {
            SessionId = activeSessionId,
            UserName = userName,
            Domain = domain
        };
    }

    private static string QueryString(int sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                infoClass,
                out IntPtr buffer,
                out _))
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

    private static WtsConnectStateClass QueryConnectState(int sessionId)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                WtsInfoClass.WTSConnectState,
                out IntPtr buffer,
                out _))
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

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSConnectState = 8
    }

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
        WTSInit
    }
}