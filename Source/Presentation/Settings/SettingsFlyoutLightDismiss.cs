using System.Runtime.InteropServices;
using static AsusHardwareService.Presentation.Osd.OsdNativeMethods;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>
/// Provides light-dismiss behavior for the settings flyout.
/// </summary>
internal static class SettingsFlyoutLightDismiss
{
    private const int WhMouseLl = 14;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmMiddleButtonDown = 0x0207;
    private const uint WmXButtonDown = 0x020B;

    private static readonly LowLevelMouseProcedure MouseProcedure = OnLowLevelMouse;

    private static IntPtr _hook;
    private static IntPtr _flyoutWindow;
    private static uint _dismissMessage;
    private static bool _dismissPosted;

    /// <summary>
    /// Starts observing mouse button presses and requests dismissal when one occurs outside the
    /// supplied fly-out window. The observed click continues to its original target.
    /// </summary>
    /// <param name="flyoutWindow">The visible settings fly-out window.</param>
    /// <param name="dismissMessage">The private window message used to request dismissal.</param>
    /// <returns><see langword="true"/> when the light-dismiss hook was installed.</returns>
    internal static bool Start(IntPtr flyoutWindow, uint dismissMessage)
    {
        Stop();
        if (flyoutWindow == IntPtr.Zero)
        {
            return false;
        }

        _flyoutWindow = flyoutWindow;
        _dismissMessage = dismissMessage;
        _dismissPosted = false;
        _hook = SetWindowsHookEx(WhMouseLl, MouseProcedure, GetModuleHandle(null), 0);
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _flyoutWindow = IntPtr.Zero;
        _dismissMessage = 0;
        return false;
    }

    /// <summary>Stops observing mouse clicks for light-dismiss.</summary>
    internal static void Stop()
    {
        var hook = _hook;
        _hook = IntPtr.Zero;
        _flyoutWindow = IntPtr.Zero;
        _dismissMessage = 0;
        _dismissPosted = false;

        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private static IntPtr OnLowLevelMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        var hook = _hook;
        if (code >= 0 &&
            !_dismissPosted &&
            _flyoutWindow != IntPtr.Zero &&
            IsDismissButtonDown(unchecked((uint)wParam.ToInt64())) &&
            GetWindowRect(_flyoutWindow, out var windowRect))
        {
            var mouse = Marshal.PtrToStructure<LowLevelMouseHookData>(lParam);
            if (!Contains(windowRect, mouse.pt.X, mouse.pt.Y))
            {
                _dismissPosted = true;
                PostMessage(_flyoutWindow, _dismissMessage, UIntPtr.Zero, IntPtr.Zero);
            }
        }

        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private static bool IsDismissButtonDown(uint message) =>
        message is WmLButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown;

    private static bool Contains(Rect rect, int x, int y) =>
        x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelMouseProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        internal Point pt;
        internal uint mouseData;
        internal uint flags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProcedure hookProcedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
