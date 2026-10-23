using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

public sealed class DesktopSessionNotifications : IDisposable
{
    private nint window;
    public bool IsRegistered => window != 0;
    public DesktopSessionNotifications(nint window)
    {
        var thread = NativeMethods.GetWindowThreadProcessId(window, out var process);
        if (window == 0 || thread != NativeMethods.GetCurrentThreadId() || process != (uint)Environment.ProcessId)
            throw new ArgumentException("Session notifications require this thread's own window.", nameof(window));
        if (!WTSRegisterSessionNotification(window, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Session notifications are unavailable.");
        this.window = window;
    }
    public void Dispose()
    {
        if (window == 0) return;
        WTSUnRegisterSessionNotification(window); window = 0;
    }
    public static bool TryRead(int message, nuint parameter, out DesktopLifecycleSignal signal)
    {
        DesktopLifecycleSignal? value = message switch
        {
            0x007E => DesktopLifecycleSignal.DisplayChanged,
            0x02E0 => DesktopLifecycleSignal.DpiChanged,
            0x0218 => parameter switch { 4 => DesktopLifecycleSignal.PowerSuspending, 6 or 7 or 0x12 => DesktopLifecycleSignal.PowerResumed, _ => null },
            0x02B1 => parameter switch
            {
                2 or 4 or 6 => DesktopLifecycleSignal.SessionDisconnected,
                1 or 3 or 5 or 15 => DesktopLifecycleSignal.SessionConnected,
                7 => DesktopLifecycleSignal.SessionLocked,
                8 => DesktopLifecycleSignal.SessionUnlocked,
                _ => null
            },
            _ => null
        };
        signal = value.GetValueOrDefault(); return value.HasValue;
    }
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);
}
