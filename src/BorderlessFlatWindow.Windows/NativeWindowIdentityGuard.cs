using System.Runtime.InteropServices;
using System.Text;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace BorderlessFlatWindow.Windows;

public static class NativeWindowIdentityGuard
{
    public static bool IsCurrent(NativeWindowIdentity identity)
    {
        if (!identity.IsValid || !NativeMethods.IsWindow(identity.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(identity.Handle, out var process);
        if (process != identity.ProcessId || WindowsWindowObservationSource.ReadCreationTime(process) != identity.ProcessCreationFileTime) return false;
        NativeMethods.GetWindowThreadProcessId(identity.Handle, out var after);
        return process == after && NativeMethods.IsWindow(identity.Handle);
    }
    public static string? Executable(NativeWindowIdentity identity)
    {
        if (!IsCurrent(identity)) return null;
        using var process = NativeMethods.OpenProcess(0x1000, false, identity.ProcessId);
        var text = new StringBuilder(32768); uint size = (uint)text.Capacity;
        return !process.IsInvalid && QueryFullProcessImageName(process, 0, text, ref size) && IsCurrent(identity) ? text.ToString() : null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
}
