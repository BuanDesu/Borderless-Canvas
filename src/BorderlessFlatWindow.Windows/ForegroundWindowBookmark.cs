using System.Security.Cryptography;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>Short-lived identity marker for returning focus after cancelling overview; never moves a window.</summary>
public sealed class ForegroundWindowBookmark : IDisposable
{
    private readonly NativeWindowIdentity identity;
    private readonly string markerName = "BFW.Focus." + Guid.NewGuid().ToString("N");
    private readonly nint marker = RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private bool disposed;
    private ForegroundWindowBookmark(NativeWindowIdentity identity) => this.identity = identity;
    public static bool IsApplicationForeground
    {
        get { NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var process); return process == (uint)Environment.ProcessId; }
    }
    public static ForegroundWindowBookmark? Capture(nint window)
    {
        var observation = new WindowsWindowObservationSource().ReadWindow(window);
        if (observation.Identity is not { } identity || !observation.IsVisible || observation.IsMinimized
            || observation.IsCloaked != false || observation.IsOnCurrentDesktop != true
            || observation.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        var bookmark = new ForegroundWindowBookmark(identity);
        if (!NativeMethods.SetPropW(window, bookmark.markerName, bookmark.marker)) return null;
        if (bookmark.CanRestore) return bookmark;
        bookmark.Dispose(); return null;
    }
    public bool CanRestore
    {
        get
        {
            if (disposed || NativeMethods.GetPropW(identity.Handle, markerName) != marker) return false;
            var current = new WindowsWindowObservationSource().ReadWindow(identity.Handle);
            return current.Identity == identity && current.IsVisible && !current.IsMinimized
                && current.IsCloaked == false && current.IsOnCurrentDesktop == true;
        }
    }
    public bool TryRestore()
    {
        if (!CanRestore) return false;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == identity.Handle) return true;
        NativeMethods.GetWindowThreadProcessId(foreground, out var process);
        if (process != (uint)Environment.ProcessId) return false; // Do not override a user's switch to another app.
        if (NativeMethods.GetPropW(identity.Handle, markerName) != marker) return false;
        return NativeMethods.SetForegroundWindow(identity.Handle) && NativeMethods.GetForegroundWindow() == identity.Handle;
    }
    public void Dispose()
    {
        if (disposed) return;
        if (NativeMethods.GetPropW(identity.Handle, markerName) == marker) NativeMethods.RemovePropW(identity.Handle, markerName);
        disposed = true;
    }
}
