using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Windows.Processes;

/// <summary>Starts a process under the user token of an interactive Windows session from Session 0.</summary>
internal sealed class SessionProcessLauncher
{
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int CreateNewConsole = 0x00000010;
    private const int CreateNoWindow = 0x08000000;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustDefault = 0x0080;
    private const uint TokenAdjustSessionId = 0x0100;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    /// <summary>Starts an executable under the user token of the specified interactive session.</summary>
    /// <returns><see langword="true"/> when Windows creates the process successfully.</returns>
    public bool TryStart(
        int sessionId,
        string executablePath,
        string arguments,
        ILogger logger,
        bool createConsoleWindow = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(logger);

        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                throw CreateWin32Exception("WTSQueryUserToken failed.");
            }

            const uint tokenAccess =
                TokenAssignPrimary |
                TokenDuplicate |
                TokenQuery |
                TokenAdjustDefault |
                TokenAdjustSessionId;
            if (!DuplicateTokenEx(
                    userToken,
                    tokenAccess,
                    IntPtr.Zero,
                    SecurityImpersonation,
                    TokenPrimary,
                    out primaryToken))
            {
                throw CreateWin32Exception("DuplicateTokenEx failed.");
            }

            if (!CreateEnvironmentBlock(out environmentBlock, primaryToken, false))
            {
                throw CreateWin32Exception("CreateEnvironmentBlock failed.");
            }

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = @"winsta0\default",
                dwFlags = StartfUseShowWindow,
                wShowWindow = SwHide,
            };
            var commandLine = BuildCommandLine(executablePath, arguments);
            var workingDirectory = Path.GetDirectoryName(executablePath);
            var creationFlags = CreateUnicodeEnvironment | (createConsoleWindow ? CreateNewConsole : CreateNoWindow);
            var created = CreateProcessAsUser(
                primaryToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                environmentBlock,
                workingDirectory,
                ref startupInfo,
                out var processInfo);
            if (!created)
            {
                throw CreateWin32Exception("CreateProcessAsUser failed.");
            }

            try
            {
                logger.LogInformation(
                    "Started process in user session. SessionId={SessionId}, Pid={Pid}, CommandLine={CommandLine}",
                    sessionId,
                    processInfo.dwProcessId,
                    commandLine);
                return true;
            }
            finally
            {
                CloseHandle(processInfo.hProcess);
                CloseHandle(processInfo.hThread);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to start process in session {SessionId}.", sessionId);
            return false;
        }
        finally
        {
            CloseHandleIfNeeded(environmentBlock, static handle => DestroyEnvironmentBlock(handle));
            CloseHandleIfNeeded(primaryToken, CloseHandle);
            CloseHandleIfNeeded(userToken, CloseHandle);
        }
    }

    private static string BuildCommandLine(string executablePath, string arguments)
    {
        var quotedPath = $"\"{executablePath}\"";
        return string.IsNullOrWhiteSpace(arguments) ? quotedPath : $"{quotedPath} {arguments}";
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private static void CloseHandleIfNeeded(IntPtr handle, Func<IntPtr, bool> closeAction)
    {
        if (handle != IntPtr.Zero)
        {
            _ = closeAction(handle);
        }
    }

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
