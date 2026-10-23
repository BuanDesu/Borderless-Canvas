using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class NavigationRoutingChecks
{
    internal static int RunStandalone()
    {
        var errors = 0; var count = 0;
        Run((name, action) =>
        {
            count++;
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { errors++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        });
        Console.WriteLine($"{count - errors}/{count} isolated input-worker checks passed (no hooks or desktop input).");
        return errors == 0 ? 0 : 1;
    }
    internal static void Run(Action<string, Action> check)
    {
        check("Desktop worker consumes Ctrl-wheel and complete Tab pairs while application input passes through", Desktop);
        check("The first desktop button down starts a complete two-button gesture despite async button timing", Chord);
        check("Crosshair is independent of manual navigation and continuously consumes relative motion and clicks", Crosshair);
        check("Crosshair Escape and toggle restore cursor ownership and drain held button releases", Exit);
        check("Crosshair pause, focus, menu and input-source changes release only its own capture", Boundaries);
        check("Hook liveness requires sustained raw movement without callbacks and defers across unsafe boundaries", HookLiveness);
        check("Crosshair menu entry waits for closure and cannot outlive cancellation, pause or timeout", MenuEntry);
    }
    private sealed class Platform : INavigationInputPlatform
    {
        public long TickCount { get; set; } = 10000;
        public nint Foreground { get; set; } = 123;
        public bool MenuActive { get; set; }
        public bool Desktop = true;
        public HashSet<int> Keys = [];
        public PointD Pointer = new(200, 180);
        public PixelRect VirtualScreen => new(0, 0, 1920, 1080);
        public PixelRect? Clip;
        public bool Down(int key) => Keys.Contains(key);
        public bool IsDesktop(PointD point) => Desktop;
        public bool GetPointer(out PointD point) { point = Pointer; return true; }
        public bool ReadClip(out PixelRect rectangle) { rectangle = Clip ?? VirtualScreen; return true; }
        public bool WriteClip(PixelRect? rectangle) { Clip = rectangle; return true; }
        public bool SetPointer(PointD point) { Pointer = point; return true; }
    }
    private sealed class Harness : IDisposable
    {
        internal readonly Platform Desktop = new();
        internal readonly GlobalNavigationInput Input;
        internal Harness(bool desktop = true)
        {
            Desktop.Desktop = desktop;
            Input = new(() => { }, null, null, Desktop);
            Input.ConfigureScreens([Desktop.VirtualScreen]); Input.SetAvailable(true); Input.SynchronizeRoutingCheck(); Input.Drain();
        }
        internal void Crosshair() { Input.ToggleCrosshair(); Input.SynchronizeRoutingCheck(); Input.Drain(); }
        public void Dispose() => Input.Dispose();
    }
    private static void Desktop()
    {
        using var h = new Harness();
        Require(h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120, () => h.Desktop.Keys.Add(0x11)), "Ctrl-wheel escaped to the shell.");
        Require(h.Input.Drain().Single().Input.Kind == NavigationInputKind.CycleWindow && h.Input.State.Mode == NavigationMode.Desktop, "Ctrl-wheel did not reach the canvas.");
        Require(!h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120, () => h.Desktop.Desktop = false) && h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CycleWindow), "An application lost its Ctrl-wheel.");
        h.Input.SynchronizeRoutingCheck(() => { h.Desktop.Desktop = true; h.Desktop.Keys.Clear(); });
        Require(h.Input.KeyRoutingCheck(9) && h.Input.KeyRoutingCheck(9, true) && h.Input.Drain().Count(item => item.Input.Kind == NavigationInputKind.Overview) == 1, "Tab leaked or repeated.");
        h.Input.EndOverview(long.MaxValue); // Cannot close a different generation.
        h.Input.SetAvailable(false); h.Input.SynchronizeRoutingCheck(); h.Input.Drain();
        Require(!h.Input.KeyRoutingCheck(9) && !h.Input.KeyRoutingCheck(9, true) && !h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120), "Paused input remained captured.");
        h.Input.SetAvailable(true); h.Input.SynchronizeRoutingCheck();
        Require(h.Input.State.Available && h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120, () => h.Desktop.Keys.Add(0x11)), "Restart did not restore capture.");
    }
    private static void Chord()
    {
        foreach (var reverse in new[] { false, true })
        {
            using var h = new Harness(desktop: false);
            var first = reverse ? 2 : 1; var second = reverse ? 1 : 2;
            var down = reverse ? 0x204u : 0x201u; var other = reverse ? 0x201u : 0x204u;
            Require(h.Input.MouseRoutingCheck(down, h.Desktop.Pointer, before: () => { h.Desktop.Desktop = true; h.Desktop.Keys.Add(first); })
                && h.Input.MouseRoutingCheck(other, h.Desktop.Pointer, before: () => h.Desktop.Keys.Add(second)), "First down was mistaken for a foreign drag.");
            h.Input.MouseRoutingCheck(0x200, new(250, 210));
            Require(h.Input.MouseRoutingCheck(0x202, new(250, 210), before: () => h.Desktop.Keys.Remove(1))
                && h.Input.MouseRoutingCheck(0x205, new(250, 210), before: () => h.Desktop.Keys.Remove(2)), "Button releases escaped.");
            Require(h.Input.Drain().Any(item => item.Input.Kind == NavigationInputKind.Move), "Chord did not move the canvas.");
        }
    }
    private static void Crosshair()
    {
        using var h = new Harness(); h.Crosshair();
        Require(h.Input.State is { Crosshair: true, Manual: false, Active: true } && h.Desktop.Pointer == new PointD(960, 540), "Crosshair reused manual navigation.");
        for (var i = 0; i < 20; i++)
        {
            h.Input.RelativeRoutingCheck(new(8, -5), () => h.Desktop.TickCount += 16);
            h.Input.MouseRoutingCheck(0x200, h.Desktop.Pointer);
        }
        Require(h.Input.Drain().Where(item => item.Input.Kind == NavigationInputKind.CameraDelta).Select(item => item.Input.Position).Aggregate(PointD.Zero, (sum, delta) => sum + delta) == new PointD(160, -100)
            && h.Input.State.Crosshair && h.Desktop.Pointer == new PointD(960, 540), "Relative movement stopped or moved the centered cursor.");
        Require(h.Input.MouseRoutingCheck(0x201, h.Desktop.Pointer, before: () => h.Desktop.Keys.Add(1)), "Crosshair click reached an underlying app.");
        h.Input.RelativeRoutingCheck(new(12, 3));
        Require(h.Input.MouseRoutingCheck(0x202, h.Desktop.Pointer, before: () => h.Desktop.Keys.Remove(1)) && h.Input.State.Crosshair
            && h.Input.Drain().Any(item => item.Input.Kind == NavigationInputKind.CameraDelta && item.Input.Position == new PointD(12, 3)), "Click exited or blocked relative movement.");
        foreach (var (down, up, button) in new[] { (0x204u, 0x205u, 0), (0x207u, 0x208u, 0), (0x20Bu, 0x20Cu, 1) })
            Require(h.Input.MouseRoutingCheck(down, h.Desktop.Pointer, button) && h.Input.MouseRoutingCheck(up, h.Desktop.Pointer, button) && h.Input.State.Crosshair,
                "A right/middle/auxiliary click escaped or exited crosshair.");
    }
    private static void HookLiveness()
    {
        var monitor = new InputHookLiveness(); monitor.Callback(1000);
        Require(!monitor.ObserveRawMovement(1500, true) && !monitor.ObserveRawMovement(1700, true)
            && !monitor.ObserveRawMovement(1900, true) && monitor.ObserveRawMovement(1950, true), "Hook loss was not bounded or fired too early.");
        monitor.Callback(2000);
        Require(!monitor.ObserveRawMovement(2650, true) && !monitor.ObserveRawMovement(2900, false)
            && !monitor.ObserveRawMovement(3000, true) && !monitor.ObserveRawMovement(3249, true)
            && monitor.ObserveRawMovement(3250, true), "A callback or unsafe boundary failed to reset hook-loss evidence.");
    }
    private static void MenuEntry()
    {
        foreach (var outcome in new[] { "close", "cancel", "pause", "timeout" })
        {
            using var h = new Harness(desktop: false);
            h.Input.SynchronizeRoutingCheck(() => h.Desktop.MenuActive = true);
            h.Input.ToggleCrosshair(afterMenu: true); h.Input.SynchronizeRoutingCheck();
            Require(!h.Input.State.Crosshair && h.Desktop.Clip is null, "Menu entry captured before menu closure.");
            if (outcome == "cancel") { h.Input.ToggleCrosshair(); h.Input.SynchronizeRoutingCheck(); }
            if (outcome == "pause") { h.Input.SetAvailable(false); h.Input.SynchronizeRoutingCheck(); }
            if (outcome == "timeout") h.Input.TimerRoutingCheck(() => h.Desktop.TickCount += 1300);
            h.Input.TimerRoutingCheck(() => h.Desktop.MenuActive = false);
            Require(h.Input.State.Crosshair == (outcome == "close") && (h.Desktop.Clip is not null) == (outcome == "close"), "Deferred menu request survived an invalid boundary: " + outcome);
        }
    }
    private static void Exit()
    {
        using var h = new Harness(); var before = h.Desktop.Pointer; h.Crosshair();
        h.Input.MouseRoutingCheck(0x201, h.Desktop.Pointer, before: () => h.Desktop.Keys.Add(1));
        Require(h.Input.KeyRoutingCheck(0x1B) && h.Input.KeyRoutingCheck(0x1B, true) && !h.Input.State.Crosshair && h.Desktop.Clip is null && h.Desktop.Pointer == before
            && h.Input.LastExitReason.Contains("退出准星"), "Escape did not release cursor ownership or retain its cause.");
        Require(h.Input.MouseRoutingCheck(0x202, before, before: () => h.Desktop.Keys.Remove(1)), "A captured button release escaped after Escape.");
        h.Crosshair(); Require(h.Input.State.Crosshair, "Crosshair did not reenter.");
        h.Input.ToggleCrosshair(); h.Input.SynchronizeRoutingCheck();
        Require(!h.Input.State.Crosshair && h.Desktop.Clip is null && h.Desktop.Pointer == before, "Toggle did not release crosshair.");
        h.Input.Toggle(); h.Input.SynchronizeRoutingCheck();
        Require(h.Input.State is { Manual: true, Crosshair: false }, "Manual mode inherited crosshair ownership.");
    }
    private static void Boundaries()
    {
        foreach (var reason in new[] { "pause", "focus", "menu", "source", "external" })
        {
            using var h = new Harness(); var before = h.Desktop.Pointer; h.Crosshair();
            h.Input.RelativeRoutingCheck(new(1, 1)); h.Input.Drain();
            h.Input.TimerRoutingCheck(() =>
            {
                if (reason == "focus") h.Desktop.Foreground++;
                if (reason == "menu") h.Desktop.MenuActive = true;
            });
            if (reason == "pause") { h.Input.SetAvailable(false); h.Input.SynchronizeRoutingCheck(); }
            if (reason == "source")
            {
                h.Input.MouseRoutingCheck(0x200, h.Desktop.Pointer, before: () => h.Desktop.TickCount += 700);
                h.Input.TimerRoutingCheck();
            }
            if (reason == "external")
                h.Input.RelativeRoutingCheck(new(1, 1), () => h.Desktop.Clip = new(50, 50, 100, 100));
            Require(!h.Input.State.Crosshair && (reason == "external" ? h.Desktop.Clip == new PixelRect(50, 50, 100, 100) : h.Desktop.Clip is null && h.Desktop.Pointer == before), "Boundary failed to safely release: " + reason);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
