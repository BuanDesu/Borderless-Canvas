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
        check("A temporarily unavailable UI wake does not strand startup input until a settings drain", StartupWake);
        check("Absolute remote mouse samples keep crosshair active and accumulate normalized motion at the center", RemoteAbsolute);
        check("Legacy remote movement falls back without exit, skips baseline and duplicate positions, and restores ownership", RemoteLegacy);
        check("Pointer source handovers rebaseline instead of double-counting or jumping the camera", SourceHandover);
        check("Precision desktop wheel routes both axes and Ctrl-wheel accumulates without leaking or skipping windows", PrecisionWheel);
        check("Delivered input wakes retry after a stopped UI timer without a settings drain or additional input", DeliveredWake);
        check("A new canvas input session clears an earlier suspension without bypassing Escape in the current session", SessionReentry);
        check("Edge worker accelerates only on fresh outward input and stops on inward movement, location, pause and modifiers", EdgeWorker);
    }
    private static void EdgeWorker()
    {
        using var h = new Harness(); h.Input.SynchronizeRoutingCheck(() => h.Desktop.Pointer = new(1919, 500)); h.Input.Drain();
        h.Input.RelativeRoutingCheck(new(100, 0));
        for (var i = 0; i < 65; i++) h.Input.RelativeRoutingCheck(new(1, 0), () => h.Desktop.TickCount += 20);
        var deltas = h.Input.Drain().Where(item => item.Input.Kind == NavigationInputKind.CameraDelta).ToArray();
        Require(deltas.Length == 65 && deltas[^1].Input.Position.X > deltas[0].Input.Position.X * 8, "Worker did not ramp the edge.");
        h.Input.TimerRoutingCheck(() => h.Desktop.TickCount += 40);
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta), "An idle timer continued edge movement.");
        h.Input.RelativeRoutingCheck(new(-1, 0)); h.Input.RelativeRoutingCheck(new(1, 0));
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta), "Reversal retained old movement.");
        h.Input.RelativeRoutingCheck(new(1, 0), () => h.Desktop.TickCount += 20);
        Require(h.Input.Drain().Single(item => item.Input.Kind == NavigationInputKind.CameraDelta).Input.Position.X < 5, "Edge did not restart slowly.");
        h.Input.MouseRoutingCheck(0x200, new(100, 100)); h.Input.Drain();
        h.Input.RelativeRoutingCheck(new(1, 0), () => h.Desktop.TickCount += 10);
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta), "An absolute pointer exit retained edge acceleration.");
        h.Input.SetAvailable(false); h.Input.SetAvailable(true); h.Input.SynchronizeRoutingCheck(); h.Input.Drain();
        h.Input.RelativeRoutingCheck(new(1, 0), () => h.Desktop.TickCount += 10);
        h.Input.KeyRoutingCheck(0x11); h.Input.RelativeRoutingCheck(new(1, 0), () => h.Desktop.TickCount += 10);
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta), "Pause/modifier boundary retained acceleration.");
    }
    private static void DeliveredWake()
    {
        var platform = new Platform(); var delivered = 0;
        using var input = new GlobalNavigationInput(() => Interlocked.Increment(ref delivered), null, null, platform);
        input.SetAvailable(true); input.SynchronizeRoutingCheck();
        var before = Volatile.Read(ref delivered);
        // The callback ran, but startup/recovery stopped the timer before Drain.
        input.AcknowledgeUiWake();
        for (var i = 0; i < 100; i++) input.Configure(new());
        input.SynchronizeRoutingCheck();
        Require(Volatile.Read(ref delivered) == before, "Acknowledging delivery expanded a raw-rate notification burst before Drain.");
        input.TimerRoutingCheck(() => platform.TickCount += 100);
        Require(Volatile.Read(ref delivered) > before, "A successful UI post stayed coalesced after its consumer timer stopped.");
        var retried = Volatile.Read(ref delivered);
        input.TimerRoutingCheck(); input.TimerRoutingCheck();
        Require(Volatile.Read(ref delivered) == retried, "Unacknowledged UI callbacks accumulated.");
        input.Drain(); input.TimerRoutingCheck();
        Require(!input.HasPendingUiNotification && Volatile.Read(ref delivered) == retried, "Drained state kept waking the UI.");
    }
    private static void SessionReentry()
    {
        using var h = new Harness();
        h.Input.Exit(); h.Input.SynchronizeRoutingCheck(); h.Input.Drain();
        Require(!h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120), "Explicit suspension immediately reentered.");
        h.Input.SetAvailable(false); h.Input.SetAvailable(true); h.Input.SynchronizeRoutingCheck(); h.Input.Drain();
        Require(h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 120), "A fresh session inherited an earlier automatic navigation suspension.");
        Require(h.Input.Drain().Any(item => item.Input.Kind == NavigationInputKind.Wheel), "Fresh session input failed to reach its consumer.");
    }
    private static void PrecisionWheel()
    {
        using var h = new Harness(); h.Desktop.Keys.Add(0x11);
        for (var i = 0; i < 119; i++) Require(h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 1), "Partial Ctrl-wheel leaked to Explorer.");
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CycleWindow), "A sub-notch packet selected a window.");
        h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 1);
        Require(h.Input.Drain().Single(item => item.Input.Kind == NavigationInputKind.CycleWindow).Input.WheelDelta == 120,
            "One accumulated wheel unit did not select once.");
        h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 100);
        h.Input.KeyRoutingCheck(0xA2, true, () => h.Desktop.Keys.Clear());
        h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, 30, () => h.Desktop.Keys.Add(0x11));
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CycleWindow), "Ctrl release carried a partial unit.");
        h.Desktop.Keys.Clear();
        Require(h.Input.MouseRoutingCheck(0x20E, h.Desktop.Pointer, 1) && h.Input.MouseRoutingCheck(0x20A, h.Desktop.Pointer, -1), "Precision pan leaked.");
        var pans = h.Input.Drain();
        Require(pans.Count == 2 && pans[0].Input.Kind == NavigationInputKind.HorizontalWheel && pans[0].Input.WheelDelta == 1
            && pans[1].Input.Kind == NavigationInputKind.Wheel && pans[1].Input.WheelDelta == -1, "Precision pan changed direction or magnitude.");
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
        public PixelRect PrimaryScreen => VirtualScreen;
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
    private static void StartupWake()
    {
        var platform = new Platform(); var uiReady = false; var delivered = 0;
        using var input = new GlobalNavigationInput(() =>
        {
            if (!Volatile.Read(ref uiReady)) throw new InvalidOperationException("Synthetic owner handle is not ready.");
            Interlocked.Increment(ref delivered);
        }, null, null, platform);
        input.SetAvailable(true); input.SynchronizeRoutingCheck();
        Volatile.Write(ref uiReady, true);
        input.TimerRoutingCheck();
        Require(Volatile.Read(ref delivered) > 0, "Failed startup delivery was not retried while idle.");
        Require(input.MouseRoutingCheck(0x20A, platform.Pointer, 120, () => platform.Keys.Add(0x11)), "Worker did not accept startup wheel.");
        Require(Volatile.Read(ref delivered) > 0, "A failed initial wake stayed coalesced until an explicit Drain (as on settings close).");
        Require(input.Drain().Any(item => item.Input.Kind == NavigationInputKind.CycleWindow), "Initial settings were not usable before opening settings.");
    }
    private static void RemoteAbsolute()
    {
        using var h = new Harness(); h.Crosshair();
        h.Input.SampleRoutingCheck(new(new(10000, 20000), Absolute: true, Device: 44));
        h.Input.SampleRoutingCheck(new(PointD.Zero, Absolute: true, Device: 44, Buttons: 1));
        h.Input.SampleRoutingCheck(new(new(11000, 20500), Absolute: true, Device: 44), () => h.Desktop.TickCount += 16);
        var delta = h.Input.Drain().Where(item => item.Input.Kind == NavigationInputKind.CameraDelta).Single().Input.Position;
        Require(h.Input.State.Crosshair && h.Input.PointerSourceMessage.Contains("绝对") && Math.Abs(delta.X - 1000 * 1920 / 65535d) < .00001
            && Math.Abs(delta.Y - 500 * 1080 / 65535d) < .00001 && h.Desktop.Pointer == new PointD(960, 540), "Absolute remote input exited or changed scale/center.");
    }
    private static void RemoteLegacy()
    {
        using var h = new Harness(); var before = h.Desktop.Pointer; h.Crosshair();
        h.Input.RelativeRoutingCheck(new(1, 0)); h.Input.Drain();
        h.Input.MouseRoutingCheck(0x200, h.Desktop.Pointer, before: () => h.Desktop.TickCount += 700); h.Input.TimerRoutingCheck();
        Require(h.Input.State.Crosshair && h.Input.PointerSourceMessage.Contains("普通") && h.Desktop.Clip == h.Desktop.VirtualScreen, "Legacy remote movement exited instead of adapting its capture.");
        foreach (var position in new[] { new PointD(1100, 600), new(1110, 606), new(1110, 606), new(960, 540), new(1115, 610) })
            Require(h.Input.MouseRoutingCheck(0x200, position, before: () => h.Desktop.TickCount += 16), "Legacy movement escaped to the application.");
        var total = h.Input.Drain().Where(item => item.Input.Kind == NavigationInputKind.CameraDelta).Select(item => item.Input.Position).Aggregate(PointD.Zero, (sum, delta) => sum + delta);
        Require(total == new PointD(15, 10) && h.Input.State.Crosshair && h.Desktop.Pointer == new PointD(960, 540), "Legacy baseline, duplicate or center echo became camera movement.");
        h.Input.KeyRoutingCheck(0x1B); h.Input.KeyRoutingCheck(0x1B, true);
        Require(h.Desktop.Clip is null && h.Desktop.Pointer == before && !h.Input.State.Crosshair, "Legacy capture did not restore after Escape.");
    }
    private static void SourceHandover()
    {
        using var h = new Harness(); h.Crosshair();
        h.Input.SampleRoutingCheck(new(new(10000, 20000), Absolute: true, Device: 44));
        h.Input.RelativeRoutingCheck(new(50, 20));
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta), "Source handover double-counted a sample.");
        h.Input.RelativeRoutingCheck(new(7, -2));
        Require(h.Input.Drain().Single(item => item.Input.Kind == NavigationInputKind.CameraDelta).Input.Position == new PointD(7, -2), "Relative motion did not resume after a handover.");
        h.Input.SampleRoutingCheck(new(new(60000, 50000), Absolute: true, VirtualDesktop: true, Device: 66));
        Require(h.Input.Drain().All(item => item.Input.Kind != NavigationInputKind.CameraDelta) && h.Input.State.Crosshair, "A different absolute source jumped or exited.");
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
        foreach (var reason in new[] { "pause", "focus", "menu", "external" })
        {
            using var h = new Harness(); var before = h.Desktop.Pointer; h.Crosshair();
            h.Input.RelativeRoutingCheck(new(1, 1)); h.Input.Drain();
            h.Input.TimerRoutingCheck(() =>
            {
                if (reason == "focus") h.Desktop.Foreground++;
                if (reason == "menu") h.Desktop.MenuActive = true;
            });
            if (reason == "pause") { h.Input.SetAvailable(false); h.Input.SynchronizeRoutingCheck(); }
            if (reason == "external")
                h.Input.RelativeRoutingCheck(new(1, 1), () => h.Desktop.Clip = new(50, 50, 100, 100));
            Require(!h.Input.State.Crosshair && (reason == "external" ? h.Desktop.Clip == new PixelRect(50, 50, 100, 100) : h.Desktop.Clip is null && h.Desktop.Pointer == before), "Boundary failed to safely release: " + reason);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
