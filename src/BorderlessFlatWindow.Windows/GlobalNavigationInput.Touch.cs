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
    private long touchVerticalPackets, touchHorizontalPackets, touchHookSeen;
    private nint touchReceiverForeground;
    private ForegroundWindowBookmark? touchReturnFocus;
    private string touchFocusStatus = "未进入";
    private string touchRouteStatus = "等待输入", touchReceiverStatus = "未开启";
    public string TouchInputMessage => $"滚动接管：钩子 {Volatile.Read(ref touchHookPackets)} / 接收窗口 {Volatile.Read(ref touchWindowPackets)}"
        + $" · 纵向 {Volatile.Read(ref touchVerticalPackets)} / 横向 {Volatile.Read(ref touchHorizontalPackets)} · 焦点 {Volatile.Read(ref touchFocusStatus)}"
        + $" · 钩子看见 {Volatile.Read(ref touchHookSeen)} ({Volatile.Read(ref touchRouteStatus)})";
    public string TouchReceiverMessage => Volatile.Read(ref touchReceiverStatus);
    public void FocusTouchNavigation() => Post(FocusTouchReceiver);
    private bool IsTouchReceiver(nint handle) => touchReceivers.Any(receiver => receiver.Handle == handle);
    private void FocusTouchReceiver()
    {
        if (!nativeInput || !touchEnabled || !available || overviewOpen || MenuActive() || Down(1) || Down(2) || Down(4) || Down(5) || Down(6)
            || !GetPointer(out var point)) return;
        var receiver = touchReceivers.FirstOrDefault(receiver => point.X >= receiver.Bounds.Left && point.X < receiver.Bounds.Right
            && point.Y >= receiver.Bounds.Top && point.Y < receiver.Bounds.Bottom) ?? touchReceivers.FirstOrDefault();
        if (receiver is null || receiver.PassThrough) return;
        var foreground = ForegroundWindow;
        if (IsTouchReceiver(foreground)) return;
        try
        {
            var candidate = ForegroundWindowBookmark.Capture(foreground);
            if (candidate is not null) { touchReturnFocus?.Dispose(); touchReturnFocus = candidate; }
            Volatile.Write(ref touchFocusStatus, receiver.TryFocus() ? "已接收" : "未获得，点击双指平移重试");
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or COMException)
        { Volatile.Write(ref touchFocusStatus, "焦点暂不可用，滚动接收仍保留"); }
    }
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
                touchEnabled = true; mode = NavigationMode.Touch; touchHookPackets = touchWindowPackets = touchVerticalPackets = touchHorizontalPackets = touchHookSeen = 0;
                Volatile.Write(ref touchRouteStatus, "等待输入");
                Volatile.Write(ref touchFocusStatus, "等待接收");
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
        var rejection = !available ? "未启用" : overviewOpen ? "总览打开" : MenuActive() ? "菜单打开" : !TouchTarget(input.Point) ? "目标不在接收区" : null;
        if (rejection is not null) { Volatile.Write(ref touchRouteStatus, rejection); cycleWheel.Reset(); return false; }
        var horizontal = kind == NavigationInputKind.HorizontalWheel;
        var shift = !horizontal && Down(0x10); var control = !horizontal && Down(0x11);
        var delta = (int)unchecked((short)(input.Data >> 16));
        if (!QueueTouchWheel(kind, delta, shift, control)) { Volatile.Write(ref touchRouteStatus, "轴已禁用"); return false; }
        Volatile.Write(ref touchRouteStatus, "已接管");
        touchHookPackets++; return true;
    }
    private bool QueueTouchWheel(NavigationInputKind kind, int delta, bool shift, bool control)
    {
        var horizontal = kind == NavigationInputKind.HorizontalWheel;
        if (!control && (horizontal || shift ? !gate.Options.HorizontalWheel : !gate.Options.VerticalWheel)) { cycleWheel.Reset(); return false; }
        if (control) delta = cycleWheel.Add(delta, InputTime) * 120; else cycleWheel.Reset();
        if (delta != 0)
        {
            if (horizontal || shift) touchHorizontalPackets++; else touchVerticalPackets++;
            Queue(new(control ? NavigationInputKind.CycleWindow : kind, PointD.Zero, delta, shift));
        }
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
        TouchPointerPassThrough(contextTap: kind is NavigationInputKind.RightDown or NavigationInputKind.RightUp);
    }
    private void TouchPointerPassThrough(bool contextTap = false)
    {
        // The desktop's modern menu can open asynchronously after the tap's up.
        // Keep the complete click exposed while that menu obtains its own HWND.
        touchPassThroughUntil = Math.Max(touchPassThroughUntil, InputTime + (contextTap ? 500 : 100));
        foreach (var receiver in touchReceivers) receiver.SetPassThrough(true);
    }
    private void RefreshTouchReceivers()
    {
        if (touchEnabled && nativeInput && GetPointer(out var sample))
        {
            var inside = touchAreas.Any(area => sample.X >= area.Left && sample.X < area.Right && sample.Y >= area.Top && sample.Y < area.Bottom);
            Volatile.Write(ref touchReceiverStatus, $"接收层 {touchReceivers.Count} · 边界匹配 {touchReceivers.All(receiver => receiver.BoundsMatch)} · 光标在区域 {inside} · 命中接收层 {IsTouchReceiver(GetAncestor(WindowFromPoint(sample), 2))}");
        }
        if (!touchEnabled || touchReceivers.Count == 0 || InputTime < touchPassThroughUntil || MenuActive()
            || Down(1) || Down(2) || Down(4) || Down(5) || Down(6)) return;
        var foreground = ForegroundWindow;
        if (nativeInput && touchReceivers.Any(receiver => receiver.Visible))
            Volatile.Write(ref touchFocusStatus, IsTouchReceiver(foreground) ? "已接收" : "应用交互中");
        foreach (var receiver in touchReceivers)
        {
            var rearm = receiver.PassThrough; receiver.SetPassThrough(false);
            if (rearm || foreground != touchReceiverForeground) receiver.RaiseWithoutActivation();
        }
        touchReceiverForeground = foreground;
        // Focus is requested only on entry or by an explicit toolbar action.
        // Hovering the desktop after a tap must not cancel a shell context menu.
    }
    private void DisposeTouchReceivers()
    {
        // Restore only while our receiver still owns foreground. An application
        // chosen by a real click keeps its focus throughout mode exit.
        try { if (nativeInput && IsTouchReceiver(ForegroundWindow)) touchReturnFocus?.TryRestore(); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or COMException) { }
        finally
        {
            touchReturnFocus?.Dispose(); touchReturnFocus = null;
            foreach (var receiver in touchReceivers) receiver.Dispose(); touchReceivers.Clear();
            touchPassThroughUntil = 0; touchReceiverForeground = 0;
        }
    }
    private bool TouchTarget(Point point)
    {
        if (!nativeInput) return true;
        if (!touchAreas.Any(area => point.X >= area.Left && point.X < area.Right && point.Y >= area.Top && point.Y < area.Bottom)) return false;
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
