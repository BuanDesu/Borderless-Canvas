using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>The intent belongs to the live window, not a PID/title persisted on disk. HWND destruction removes its property.</summary>
public static class WindowPinMarker
{
    private const string Name = "BorderlessFlatWindow.Fixed.v1";
    private static readonly nint Value = 0x42465750;
    public static bool IsPinned(NativeWindowIdentity identity) => NativeWindowIdentityGuard.IsCurrent(identity)
        && NativeMethods.GetPropW(identity.Handle, Name) == Value && NativeWindowIdentityGuard.IsCurrent(identity);
    public static void Set(NativeWindowIdentity identity, bool pinned)
    {
        if (!NativeWindowIdentityGuard.IsCurrent(identity)) throw new InvalidOperationException("窗口身份已变化，未切换便签。");
        var current = NativeMethods.GetPropW(identity.Handle, Name);
        if (current != 0 && current != Value) throw new InvalidOperationException("便签标记已被其他数据占用，未覆盖。");
        if (pinned && current == 0 && !NativeMethods.SetPropW(identity.Handle, Name, Value)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法登记窗口便签状态。");
        if (!pinned && current == Value && NativeMethods.RemovePropW(identity.Handle, Name) != Value) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法取消窗口便签状态。");
        if (!NativeWindowIdentityGuard.IsCurrent(identity)) throw new InvalidOperationException("窗口在便签切换期间已关闭或改变身份。");
    }
}
