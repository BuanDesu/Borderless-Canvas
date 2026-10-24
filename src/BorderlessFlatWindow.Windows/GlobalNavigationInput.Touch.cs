using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using System.Text;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private bool touchEnabled;
    public bool SetTouchNavigation(bool enabled)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (!enabled && !touchEnabled) { completion.SetResult(true); return; }
            Leave("触控导航已退出。");
            if (!enabled || !available || overviewOpen) { completion.SetResult(!enabled); return; }
            touchEnabled = true; mode = NavigationMode.Touch;
            Publish(true, State.Registered, State.Hotkey, "触控导航 · 两指纵横滚动移动画布；单击和拖动交给应用；Esc 退出。");
            completion.SetResult(true);
        });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
    private bool RouteTouchWheel(NavigationInputKind kind, MouseInput input)
    {
        if (kind is not (NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)) return false;
        if (!available || overviewOpen || MenuActive() || !TouchTarget(input.Point)) { cycleWheel.Reset(); return false; }
        var horizontal = kind == NavigationInputKind.HorizontalWheel;
        var shift = !horizontal && Down(0x10); var control = !horizontal && Down(0x11);
        if (!control && (horizontal || shift ? !gate.Options.HorizontalWheel : !gate.Options.VerticalWheel)) { cycleWheel.Reset(); return false; }
        var delta = (int)unchecked((short)(input.Data >> 16));
        if (control) delta = cycleWheel.Add(delta, InputTime) * 120; else cycleWheel.Reset();
        if (delta != 0) Queue(new(control ? NavigationInputKind.CycleWindow : kind, new(input.Point.X, input.Point.Y), delta, shift));
        return true;
    }
    private bool TouchTarget(Point point)
    {
        if (!nativeInput) return true;
        var window = WindowFromPoint(point); if (window == 0) return false;
        var root = GetAncestor(window, 2); if (root == 0) root = window;
        GetWindowThreadProcessId(root, out var process);
        if (process == (uint)Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassName(root, name, name.Capacity);
        return name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
}
