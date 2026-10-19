using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows.Interop;

/// <summary>Synchronous, same-thread scope. Never retain across await or a message loop.</summary>
internal sealed class PhysicalPixelScope : IDisposable
{
    private readonly nint previous;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private bool disposed;

    internal PhysicalPixelScope()
    {
        previous = NativeMethods.SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        if (previous == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot enter physical pixel context.");
    }

    public void Dispose()
    {
        if (disposed) return;
        if (threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("DPI context must be restored on its original thread.");
        if (NativeMethods.SetThreadDpiAwarenessContext(previous) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot restore DPI context.");
        disposed = true;
    }
}
