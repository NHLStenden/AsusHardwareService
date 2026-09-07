using System.Runtime.InteropServices;

namespace AsusHardwareService.Windows.Sessions;

/// <summary>Discovers the active logged-on console session through Windows Terminal Services APIs.</summary>
internal sealed class UserSessionService
{
    private const uint InvalidSessionId = 0xFFFFFFFF;

    /// <summary>Returns the active console user session when it is logged on and connected.</summary>
    public InteractiveSession? GetActiveSession()
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

        return new InteractiveSession(
            activeSessionId,
            userName,
            QueryString(activeSessionId, WtsInfoClass.WTSDomainName));
    }

    /// <summary>Polls until an interactive session becomes active or cancellation is requested.</summary>
    public async Task<InteractiveSession?> WaitForActiveSessionAsync(
        TimeSpan pollingInterval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var session = GetActiveSession();
            if (session is not null)
            {
                return session;
            }

            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

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

    private static WtsConnectStateClass QueryConnectState(int sessionId)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                WtsInfoClass.WTSConnectState,
                out var buffer,
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

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSConnectState = 8,
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
        WTSInit,
    }
}
