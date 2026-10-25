using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>A thread-owned wheel target. Button input is made transparent before the physical event continues.</summary>
internal sealed class TouchWheelReceiver : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly WindowProcedure callback;
    private readonly Action<uint, nuint> wheel;
    private readonly Action unexpectedButton;
    private readonly Action<uint>? buttonObserved;
    private readonly nint original;
    private nint handle;
    internal nint Handle => handle;
    internal PixelRect Bounds { get; }
    internal bool PassThrough { get; private set; }
    internal bool Visible => handle != 0 && NativeMethods.IsWindowVisible(handle);
    internal bool BoundsMatch => NativeMethods.GetWindowRect(handle, out var actual) && actual.Left == Bounds.Left && actual.Top == Bounds.Top
        && actual.Right == Bounds.Right && actual.Bottom == Bounds.Bottom;

    internal TouchWheelReceiver(PixelRect bounds, Action<uint, nuint> wheel, Action unexpectedButton, bool show = true, Action<uint>? buttonObserved = null)
    {
        this.wheel = wheel; this.unexpectedButton = unexpectedButton; this.buttonObserved = buttonObserved; callback = WindowProc; Bounds = bounds;
        // Precision touchpad targeting differs from an injected mouse wheel.
        // Keep the focus-capable surface used by the original working mode.
        handle = NativeMethods.CreateWindowExW(0x00080080, "STATIC", "BFW wheel receiver", 0x80000004,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0, 0, 0, 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法创建触控滚动接收器。");
        try
        {
            original = SetLong(handle, -4, Marshal.GetFunctionPointerForDelegate(callback));
            if (original == 0 || !SetLayeredWindowAttributes(handle, 0, 6, 2)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (show) ShowWindow(handle, 4); // No activation, taskbar item or pointer capture.
        }
        catch { Dispose(); throw; }
    }
    internal void SetPassThrough(bool value)
    {
        CheckThread(); if (handle == 0 || PassThrough == value) return;
        var style = GetLong(handle, -20).ToInt64();
        SetLong(handle, -20, (nint)(value ? style | 0x20 : style & ~0x20));
        PassThrough = value;
    }
    internal bool NativePassThrough => (GetLong(handle, -20).ToInt64() & 0x20) != 0;
    internal bool CanFocus => (GetLong(handle, -20).ToInt64() & 0x08000000) == 0;
    internal nint FixtureHitTest()
    {
        CheckThread(); if (Visible) throw new InvalidOperationException("Native receiver fixtures must stay hidden.");
        var point = (nint)(((Bounds.Top + Bounds.Height / 2) & 0xFFFF) << 16 | ((Bounds.Left + Bounds.Width / 2) & 0xFFFF));
        return SendMessage(handle, 0x84, 0, point);
    }
    internal bool TryFocus()
    {
        CheckThread(); if (!Visible || PassThrough || !NativeMethods.SetForegroundWindow(handle)) return false;
        SetFocus(handle); return NativeMethods.GetForegroundWindow() == handle;
    }
    internal void RaiseWithoutActivation()
    {
        CheckThread();
        if (Visible) NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, 0x13); // Top of ordinary windows; no move, size or activation.
    }
    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message is 0x204 or 0x205 or 0x7B) buttonObserved?.Invoke(message);
        if (message == 0x21) return 3; // MA_NOACTIVATE.
        // STATIC defaults to HTTRANSPARENT without SS_NOTIFY. Explicitly expose
        // a client hit target so physical touchpad targeting can find this HWND.
        if (message == 0x84) return PassThrough ? -1 : 1;
        if (message is 0x20A or 0x20E) { if (!PassThrough) wheel(message, wParam); return 0; }
        if (message is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            // If a hook disappears, disarm the receiver. Never keep swallowing
            // application clicks, nor replay a click into an unverified target.
            SetPassThrough(true); unexpectedButton(); return 0;
        }
        return CallWindowProc(original, window, message, wParam, lParam);
    }
    internal void SendFixtureMessage(uint message, nuint wParam)
    { if (Visible) throw new InvalidOperationException("Native receiver fixtures must stay hidden."); SendMessage(handle, message, wParam, 0); }
    internal void PostFixtureMessage(uint message, nuint wParam)
    {
        CheckThread(); if (Visible) throw new InvalidOperationException("Native receiver fixtures must stay hidden.");
        if (!PostMessage(handle, message, wParam, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    private void CheckThread()
    { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("触控接收器必须在输入线程操作。"); }
    public void Dispose()
    {
        CheckThread(); if (handle == 0) return;
        if (original != 0) SetLong(handle, -4, original);
        NativeMethods.DestroyWindow(handle); handle = 0; GC.KeepAlive(callback);
    }
    private static nint GetLong(nint window, int index) => IntPtr.Size == 8 ? GetWindowLongPtr(window, index) : GetWindowLong(window, index);
    private static nint SetLong(nint window, int index, nint value) => IntPtr.Size == 8 ? SetWindowLongPtr(window, index, value) : SetWindowLong(window, index, (int)value);
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern nint CallWindowProc(nint previous, nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
}
