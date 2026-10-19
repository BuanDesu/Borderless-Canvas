using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>Out-of-context hooks. Install/dispose on one STA thread with a live message loop.</summary>
public sealed class WindowEventListener : IDisposable
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly List<nint> hooks = [];
    private readonly NativeMethods.WinEventCallback callback;
    private GCHandle callbackRoot;
    private bool disposed;
    public event Action<WindowChange>? EventReceived;
    public string? LastCallbackError { get; private set; }
    public bool IsRunning => hooks.Count == 4;
    public static nint ForegroundWindow => NativeMethods.GetForegroundWindow();

    public WindowEventListener(uint processFilter = 0, bool includeOwnProcess = false)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("WinEvent hooks require an STA message-loop thread.");
        callback = OnEvent;
        callbackRoot = GCHandle.Alloc(callback);
        try
        {
            var flags = includeOwnProcess ? 0u : 2u; // OUTOFCONTEXT | SKIPOWNPROCESS
            foreach (var (min, max) in new (uint, uint)[] { (3, 3), (0xA, 0x17), (0x20, 0x20), (0x8000, 0x8018) })
            {
                var hook = NativeMethods.SetWinEventHook(min, max, 0, callback, processFilter, 0, flags);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register WinEvent hook.");
                hooks.Add(hook);
            }
        }
        catch { Dispose(); throw; }
    }

    private void OnEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint eventThread, uint time)
    {
        if (disposed || (eventId >= 0x8000 && (objectId != 0 || childId != 0))) return;
        WindowChangeKind? kind = eventId switch
        {
            3 => WindowChangeKind.Foreground,
            0xB or 0x16 or 0x17 => WindowChangeKind.State,
            0x20 => WindowChangeKind.DesktopChanged,
            0x8000 => WindowChangeKind.Created,
            0x8001 => WindowChangeKind.Destroyed,
            0x8002 => WindowChangeKind.Shown,
            0x8003 => WindowChangeKind.Hidden,
            0x800A or 0x8017 or 0x8018 => WindowChangeKind.State,
            0x800B => WindowChangeKind.Location,
            0x800C => WindowChangeKind.Name,
            _ => null
        };
        if (kind is null) return;
        // Subscribers must only enqueue hints. Never let a managed exception cross the native callback.
        try { EventReceived?.Invoke(new(window, kind.Value)); }
        catch (Exception exception) { LastCallbackError = exception.Message; }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("UnhookWinEvent must run on its registration thread.");
        foreach (var hook in hooks.ToArray())
        {
            if (!NativeMethods.UnhookWinEvent(hook))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot release WinEvent hook.");
            hooks.Remove(hook);
        }
        disposed = true;
        if (callbackRoot.IsAllocated) callbackRoot.Free();
        GC.KeepAlive(callback);
    }
}
