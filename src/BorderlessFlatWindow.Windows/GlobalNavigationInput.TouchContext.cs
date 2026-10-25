using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private readonly NavigationInputGate touchContextRelease = new();
    private TouchDesktopTarget touchDesktopTarget;
    private long touchContextSent;
    private string touchContextOutcome = "等待";
    private readonly record struct TouchDesktopTarget(nint Window, nint Root, uint ShellProcess);

    private bool BeginTouchDesktopContext(NavigationInputKind kind, Point point)
    {
        if (kind != NavigationInputKind.RightDown) return false;
        if (touchContextRelease.HasHeldButtons) return true;
        if (!available || overviewOpen || MenuActive() || TouchContextModifiers() || Down(1) || Down(4) || Down(5) || Down(6)
            || !TryTouchDesktopTarget(point, out var target) || !touchContextRelease.TryEnter(false)) return false;
        contextGeneration++; touchDesktopTarget = target;
        touchContextRelease.Route(kind); touchContextRelease.Exit();
        contextClick.Accept(kind, new(point.X, point.Y), true, true, TouchContextTolerance);
        Volatile.Write(ref touchContextOutcome, "等待抬起"); return true;
    }
    private int TouchContextTolerance => Math.Max(2, Math.Max(GetSystemMetrics(68), GetSystemMetrics(69)));
    private bool TouchContextModifiers() => Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C);
    private void ObserveTouchDesktopContext(NavigationInputKind kind, Point point)
    {
        if (touchContextRelease.HasHeldButtons || kind is NavigationInputKind.LeftDown or NavigationInputKind.LeftUp)
            contextClick.Accept(kind, new(point.X, point.Y), true, IsDesktop(point), TouchContextTolerance);
    }
    private void FinishTouchDesktopContext(Point point)
    {
        TouchButtonPassThrough(NavigationInputKind.RightUp);
        var target = touchDesktopTarget; touchDesktopTarget = default;
        var completed = contextClick.Accept(NavigationInputKind.RightUp, new(point.X, point.Y), true,
            touchEnabled && available && !overviewOpen && !MenuActive() && IsDesktop(point), TouchContextTolerance);
        if (!completed) { Volatile.Write(ref touchContextOutcome, "已取消"); return; }
        var generation = contextGeneration;
        Volatile.Write(ref touchContextOutcome, "等待桌面核验");
        Post(() => ReplayTouchDesktopContext(target, point, generation));
    }
    private bool TryTouchDesktopTarget(Point point, out TouchDesktopTarget target)
    {
        target = default;
        if (!nativeInput)
        {
            // A synthetic identity is confined to the no-native-input fixture.
            if (!platform.IsDesktop(new(point.X, point.Y))) return false;
            target = new(1, 1, 1); return true;
        }
        if (!touchAreas.Any(area => point.X >= area.Left && point.X < area.Right && point.Y >= area.Top && point.Y < area.Bottom)) return false;
        var window = WindowFromPoint(point); if (window == 0) return false;
        var root = GetAncestor(window, 2); if (!NativeIsDesktopRoot(root)) return false;
        var shell = GetTouchShellWindow(); if (shell == 0) return false;
        GetWindowThreadProcessId(shell, out var shellProcess); GetWindowThreadProcessId(root, out var process);
        if (shellProcess == 0 || process != shellProcess) return false;
        target = new(window, root, shellProcess); return true;
    }
    private void ReplayTouchDesktopContext(TouchDesktopTarget target, Point released, long generation)
    {
        if (!touchEnabled || !available || overviewOpen || MenuActive() || generation != contextGeneration
            || TouchContextModifiers() || Down(1) || Down(2) || Down(4) || Down(5) || Down(6)
            || InputTime - Volatile.Read(ref heartbeat) > 1800 || !GetPointer(out var point)
            || Math.Abs(point.X - released.X) > 2 || Math.Abs(point.Y - released.Y) > 2)
        { Volatile.Write(ref touchContextOutcome, "环境变化，已取消"); return; }
        TouchPointerPassThrough(contextTap: true);
        if (!TryTouchDesktopTarget(point, out var current) || current != target)
        { Volatile.Write(ref touchContextOutcome, "桌面目标变化，已取消"); return; }
        touchContextValidated++;
        if (!nativeInput) return; // Fixture path never synthesizes OS input.
        NativeInput[] pair = [new() { Mouse = new() { Flags = 0x08, Extra = ContextClickTag } },
            new() { Mouse = new() { Flags = 0x10, Extra = ContextClickTag } }];
        var sent = SendInput(2, pair, Marshal.SizeOf<NativeInput>());
        if (sent == 1) SendInput(1, [pair[1]], Marshal.SizeOf<NativeInput>());
        if (sent == 2) { touchContextSent++; Volatile.Write(ref touchContextOutcome, "已交还桌面"); }
        else Volatile.Write(ref touchContextOutcome, "系统未接受右键，可重新轻点");
    }
    [DllImport("user32.dll", EntryPoint = "GetShellWindow")] private static extern nint GetTouchShellWindow();
}
