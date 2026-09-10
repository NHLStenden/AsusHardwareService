using System.Runtime.InteropServices;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Osd;

internal static class OsdWindow
{
    /// <summary>
    /// Runs UI mode, forwarding to an existing instance or becoming the resident UI instance.
    /// </summary>
    /// <param name="initialNotification">An optional hardware status to show immediately.</param>
    /// <returns>A process exit code.</returns>
    public static int Run(OnScreenDisplayNotification? initialNotification)
    {
        var mutexHandle = CreateMutex(IntPtr.Zero, false, InstanceMutexName);
        var mutexLastError = Marshal.GetLastWin32Error();
        if (mutexHandle == IntPtr.Zero)
        {
            return mutexLastError;
        }

        try
        {
            if (mutexLastError == ErrorAlreadyExists)
            {
                var existingWindow = OsdHost.WaitForExistingWindow();
                if (existingWindow == IntPtr.Zero)
                {
                    return 3;
                }

                if (initialNotification.HasValue)
                {
                    return OsdHost.SendNotification(existingWindow, initialNotification.Value) ? 0 : 4;
                }

                return 0;
            }

            // The command is already running inside the interactive user session. A logged-on user
            // can exist briefly before Explorer has created the shell window; in that case do not
            // leave a resident UI process behind.
            if (GetShellWindow() == IntPtr.Zero)
            {
                return 0;
            }

            return OsdHost.RunMessageLoop(initialNotification);
        }
        finally
        {
            CloseHandle(mutexHandle);
        }
    }

    /// <summary>
    /// Requests that the resident UI instance in the current user session exits.
    /// </summary>
    /// <returns>A process exit code.</returns>
    public static int Shutdown()
    {
        var existingWindow = FindWindow(WindowClassName, null);
        if (existingWindow == IntPtr.Zero)
        {
            return 0;
        }

        return SendMessageTimeout(
            existingWindow,
            WmClose,
            UIntPtr.Zero,
            IntPtr.Zero,
            0x0002,
            500,
            out _) != IntPtr.Zero ? 0 : 4;
    }
}
