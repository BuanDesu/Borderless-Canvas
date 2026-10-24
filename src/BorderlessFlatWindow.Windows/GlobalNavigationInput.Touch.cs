using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using System.Text;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private bool touchEnabled;
    private readonly List<TouchWheelReceiver> touchReceivers = [];
    private PixelRect[] touchAreas = [];
    private long touchPassThroughUntil, touchHookPackets, touchWindowPackets;
    private nint touchReceiverForeground;
    public string TouchInputMessage => $"滚动接管：钩子 {Volatile.Read(ref touchHookPackets)} / 接收窗口 {Volatile.Read(ref touchWindowPackets)}";
    public bool SetTouchNavigation(bool enabled)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (!enabled && !touchEnabled) { completion.SetResult(true); return; }
            Leave("触控导航已退出。");
            if (!enabled || !available || overviewOpen) { completion.SetResult(!enabled); return; }
            try
            {
                touchEnabled = true; mode = NavigationMode.Touch; touchHookPackets = touchWindowPackets = 0;
                if (nativeInput) foreach (var area in touchAreas)
                    touchReceivers.Add(new(area, ReceiveTouchWheel, () => Post(() => Leave("触控接收器已停用，请检查输入接管后重试。"))));
                touchReceiverForeground = ForegroundWindow;
                Publish(true, State.Registered, State.Hotkey, "触控导航 · 两指纵横滚动移动画布；单击和拖动交给应用；Esc 退出。");
                completion.SetResult(true);
            }
            catch (Exception error) { Leave("触控接收器未能启动。" + error.Message); completion.SetException(error); }
        });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
    private bool RouteTouchWheel(NavigationInputKind kind, MouseInput input)
    {
        if (kind is not (NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)) return false;
        if (!available || overviewOpen || MenuActive() || !TouchTarget(input.Point)) { cycleWheel.Reset(); return false; }
        var horizontal = kind == NavigationInputKind.HorizontalWheel;
        var shift = !horizontal && Down(0x10); var control = !horizontal && Down(0x11);
        var delta = (int)unchecked((short)(input.Data >> 16));
        if (!QueueTouchWheel(kind, delta, shift, control)) return false;
        touchHookPackets++; return true;
    }
    private bool QueueTouchWheel(NavigationInputKind kind, int delta, bool shift, bool control)
    {
        var horizontal = kind == NavigationInputKind.HorizontalWheel;
        if (!control && (horizontal || shift ? !gate.Options.HorizontalWheel : !gate.Options.VerticalWheel)) { cycleWheel.Reset(); return false; }
        if (control) delta = cycleWheel.Add(delta, InputTime) * 120; else cycleWheel.Reset();
        if (delta != 0) Queue(new(control ? NavigationInputKind.CycleWindow : kind, PointD.Zero, delta, shift));
        return true;
    }
    private void ReceiveTouchWheel(uint message, nuint wParam)
    {
        if (!touchEnabled || !available || overviewOpen || MenuActive()) { cycleWheel.Reset(); return; }
        var horizontal = message == 0x20E;
        if (QueueTouchWheel(horizontal ? NavigationInputKind.HorizontalWheel : NavigationInputKind.Wheel,
            unchecked((short)(wParam >> 16)), !horizontal && (wParam & 4) != 0, !horizontal && (wParam & 8) != 0)) touchWindowPackets++;
    }
    private void TouchButtonPassThrough(NavigationInputKind kind)
    {
        if (!touchEnabled || kind is not (NavigationInputKind.LeftDown or NavigationInputKind.LeftUp or NavigationInputKind.RightDown or NavigationInputKind.RightUp)) return;
        TouchPointerPassThrough();
    }
    private void TouchPointerPassThrough()
    {
        touchPassThroughUntil = InputTime + 100;
        foreach (var receiver in touchReceivers) receiver.SetPassThrough(true);
    }
    private void RefreshTouchReceivers()
    {
        if (!touchEnabled || touchReceivers.Count == 0 || InputTime < touchPassThroughUntil || MenuActive()
            || Down(1) || Down(2) || Down(4) || Down(5) || Down(6)) return;
        var foreground = ForegroundWindow;
        foreach (var receiver in touchReceivers)
        {
            var rearm = receiver.PassThrough; receiver.SetPassThrough(false);
            if (rearm || foreground != touchReceiverForeground) receiver.RaiseWithoutActivation();
        }
        touchReceiverForeground = foreground;
    }
    private void DisposeTouchReceivers()
    { foreach (var receiver in touchReceivers) receiver.Dispose(); touchReceivers.Clear(); touchPassThroughUntil = 0; touchReceiverForeground = 0; }
    private bool TouchTarget(Point point)
    {
        if (!nativeInput) return true;
        var window = WindowFromPoint(point); if (window == 0) return false;
        var root = GetAncestor(window, 2); if (root == 0) root = window;
        if (touchReceivers.Any(receiver => receiver.Handle == root)) return true;
        GetWindowThreadProcessId(root, out var process);
        if (process == (uint)Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassName(root, name, name.Capacity);
        return name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    private nint WindowUnderTouchReceiver(Point point)
    {
        var window = GetAncestor(WindowFromPoint(point), 2);
        if (!touchReceivers.Any(receiver => receiver.Handle == window)) return window;
        for (var count = 0; count < 256 && window != 0; count++, window = GetWindow(window, 2))
        {
            if (touchReceivers.Any(receiver => receiver.Handle == window) || !BorderlessFlatWindow.Windows.Interop.NativeMethods.IsWindowVisible(window)
                || !BorderlessFlatWindow.Windows.Interop.NativeMethods.GetWindowRect(window, out var bounds)) continue;
            if (point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom) return window;
        }
        return 0;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
}
