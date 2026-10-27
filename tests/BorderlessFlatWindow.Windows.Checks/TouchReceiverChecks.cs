using BorderlessFlatWindow.Windows.Interop;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Navigation;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class TouchReceiverChecks
{
    internal static void RunHitTesting(Action<string, Action> check) => check(
        "Native hit testing exposes only the own source during touch pass-through and rearms without activation", NativeHitTesting);
    private static void NativeHitTesting()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        var work = Screen.PrimaryScreen!.WorkingArea;
        using var source = new HitSource { Bounds = new(work.Left + 32, work.Top + 32, 160, 100), Opacity = .04 };
        source.Show(); FixtureDesktop.PlaceOwnWindow(source.Handle);
        NativeMethods.SetWindowPos(source.Handle, 0, 0, 0, 0, 0, 0x13);
        var bounds = source.RectangleToScreen(source.ClientRectangle);
        var point = new HitPoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        Require(HitWindowFromPoint(point) == source.Handle, "Own source did not become the bounded hit target.");
        using (var receiver = new TouchWheelReceiver(new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), (_, _) => { }, () => { }))
        {
            receiver.RaiseWithoutActivation();
            Require(HitWindowFromPoint(point) == receiver.Handle, "Armed receiver was skipped by actual window hit testing.");
            receiver.SetPassThrough(true);
            Require(HitWindowFromPoint(point) == source.Handle, "Pass-through did not expose the original own source.");
            receiver.SetPassThrough(false);
            Require(HitWindowFromPoint(point) == receiver.Handle, "Rearmed receiver was not restored as the hit target.");
        }
        Require(HitWindowFromPoint(point) == source.Handle && WindowEventListener.ForegroundWindow == foreground,
            "Touch hit-target lifecycle changed foreground or leaked an input surface.");
    }
    private sealed class HitSource : Form
    {
        internal HitSource() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; } }
    }
    [StructLayout(LayoutKind.Sequential)] private struct HitPoint { internal int X, Y; }
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern nint HitWindowFromPoint(HitPoint point);
    internal static int RunStandalone()
    {
        var count = 0; var failures = 0;
        Run((name, action) =>
        {
            count++;
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        });
        Console.WriteLine($"{count - failures}/{count} hidden receiver checks passed (no hooks or desktop input).");
        return failures == 0 ? 0 : 1;
    }
    internal static void Run(Action<string, Action> check)
    {
        check("Hidden native touch receiver handles direct two-axis messages, toggles click transparency and fails open without activation", Messages);
        check("The real input worker bridges direct window wheel, button transparency, delayed rearm and pause cleanup on a hidden receiver", WorkerBridge);
        check("Queued native vertical, horizontal and Shift-wheel messages traverse the real worker message pump", QueuedWheel);
        check("The native wheel surface is a real client hit target, becomes transparent for buttons and rearms", HitTarget);
        check("A touchpad context tap leaves time for the desktop menu and never rearms during a menu", ContextHandoff);
    }
    private static void ContextHandoff()
    {
        var platform = new Platform { Desktop = true }; using var input = new GlobalNavigationInput(() => { }, null, null, platform);
        input.ConfigureScreens([platform.VirtualScreen]); input.SetAvailable(true); input.SynchronizeRoutingCheck();
        Require(input.SetTouchNavigation(true), "Touch did not start."); input.Drain(); input.CreateHiddenTouchReceiverForCheck();
        Require(input.MouseRoutingCheck(0x204, new(500, 500), before: () => platform.Keys.Add(2)), "Context tap down was not held for desktop handoff.");
        Require(input.MouseRoutingCheck(0x205, new(500, 500), before: () => platform.Keys.Remove(2)), "Context tap up was not paired.");
        input.TimerRoutingCheck(() => platform.TickCount += 200);
        Require(input.TouchReceiverTransparentForCheck(), "Receiver rearmed before a delayed desktop context menu could open.");
        platform.MenuActive = true; input.TimerRoutingCheck(() => platform.TickCount += 600);
        Require(input.TouchReceiverTransparentForCheck(), "Receiver covered an active context menu.");
        platform.MenuActive = false; input.TimerRoutingCheck();
        Require(!input.TouchReceiverTransparentForCheck(), "Receiver did not return after the menu closed.");
        Require(input.MouseRoutingCheck(0x20E, new(500, 500), 1), "Horizontal precision input did not resume after the context menu.");
        var packets = input.Drain(); Require(packets.Count == 1 && packets[0].Input.Kind == NavigationInputKind.HorizontalWheel && packets[0].Input.WheelDelta == 1,
            "A context tap became navigation or lost the following gesture.");
    }
    private static void HitTarget()
    {
        using var receiver = new TouchWheelReceiver(new(-3000, -3000, -2600, -2700), (_, _) => { }, () => { }, show: false);
        Require(receiver.FixtureHitTest() == 1, "An armed surface returned HTTRANSPARENT instead of HTCLIENT; physical touchpad input has no receiver.");
        receiver.SetPassThrough(true); Require(receiver.FixtureHitTest() == -1, "Button pass-through did not expose the application.");
        receiver.SetPassThrough(false); Require(receiver.FixtureHitTest() == 1, "The receiver did not rearm its hit target.");
        Require(receiver.CanFocus && !receiver.TryFocus(), "A hidden fixture must never receive foreground.");
    }
    private static void QueuedWheel()
    {
        var platform = new Platform(); using var input = new GlobalNavigationInput(() => { }, null, null, platform);
        input.ConfigureScreens([platform.VirtualScreen]); input.SetAvailable(true); input.SynchronizeRoutingCheck();
        Require(input.SetTouchNavigation(true), "Touch did not start."); input.Drain(); var handle = input.CreateHiddenTouchReceiverForCheck();
        input.PostTouchReceiverForCheck(0x20A, -32); input.PostTouchReceiverForCheck(0x20E, 24);
        input.PostTouchReceiverForCheck(0x20A, -17, shift: true); input.SynchronizeRoutingCheck();
        var packets = input.Drain();
        Require(packets.Count == 3 && packets[0].Input.Kind == NavigationInputKind.Wheel && packets[0].Input.WheelDelta == -32
            && packets[1].Input.Kind == NavigationInputKind.HorizontalWheel && packets[1].Input.WheelDelta == 24
            && packets[2].Input.Shift && packets[2].Input.WheelDelta == -17, "Queued wheel messages were dropped by the input loop.");
        var camera = PointD.Zero; var gesture = new NavigationGestureSession(() => camera, (position, _) => { camera = position; return true; }, _ => { });
        foreach (var packet in packets) gesture.Accept(packet.Input, packet.At);
        gesture.Tick(DateTimeOffset.UtcNow);
        Require(camera == new PointD(41, 32), "A diagonal two-axis gesture lost precision or reversed an axis.");
        input.SetAvailable(false); input.SynchronizeRoutingCheck(); Require(!NativeMethods.IsWindow(handle), "Queued receiver survived pause.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Platform : INavigationInputPlatform
    {
        internal readonly HashSet<int> Keys = [];
        public long TickCount { get; set; } = 1000;
        public nint Foreground => 0;
        public bool MenuActive { get; set; }
        public bool Down(int key) => Keys.Contains(key);
        internal bool Desktop;
        public bool IsDesktop(PointD point) => Desktop;
        public bool GetPointer(out PointD point) { point = new(500, 500); return true; }
        public PixelRect VirtualScreen => new(0, 0, 1920, 1080);
        public PixelRect PrimaryScreen => VirtualScreen;
        public bool ReadClip(out PixelRect rectangle) { rectangle = VirtualScreen; return true; }
        public bool WriteClip(PixelRect? rectangle) => throw new InvalidOperationException("Touch fixture must not clip.");
        public bool SetPointer(PointD point) => throw new InvalidOperationException("Touch fixture must not move the pointer.");
    }
    private static void WorkerBridge()
    {
        var foreground = WindowEventListener.ForegroundWindow; var platform = new Platform();
        using var input = new GlobalNavigationInput(() => { }, null, null, platform);
        input.ConfigureScreens([platform.VirtualScreen]); input.SetAvailable(true); input.SynchronizeRoutingCheck();
        if (!input.SetTouchNavigation(true)) throw new InvalidOperationException("Touch fixture failed to enter.");
        input.Drain(); var handle = input.CreateHiddenTouchReceiverForCheck();
        input.TouchReceiverMessageForCheck(0x20A, 1); input.TouchReceiverMessageForCheck(0x20E, -40);
        var packets = input.Drain();
        if (packets.Count != 2 || packets[0].Input.WheelDelta != 1 || packets[1].Input.WheelDelta != -40) throw new InvalidOperationException("Native direct messages failed to reach the worker queue.");
        foreach (var (down, up, key) in new[] { (0x201u, 0x202u, 1), (0x204u, 0x205u, 2), (0x207u, 0x208u, 4), (0x20Bu, 0x20Cu, 5) })
        {
            if (input.MouseRoutingCheck(down, new(500, 500), before: () => platform.Keys.Add(key)) || !input.TouchReceiverTransparentForCheck()) throw new InvalidOperationException("A physical button was swallowed or its receiver stayed opaque.");
            input.TimerRoutingCheck(() => { platform.TickCount += 200; input.Heartbeat(); });
            if (!input.TouchReceiverTransparentForCheck()) throw new InvalidOperationException("Receiver rearmed during app dragging.");
            if (input.MouseRoutingCheck(up, new(500, 500), before: () => platform.Keys.Remove(key))) throw new InvalidOperationException("A physical button release was swallowed.");
            input.TimerRoutingCheck(() => { platform.TickCount += key == 2 ? 600 : 200; input.Heartbeat(); });
            if (input.TouchReceiverTransparentForCheck()) throw new InvalidOperationException("Receiver did not rearm after a completed app click.");
        }
        input.SetAvailable(false); input.SynchronizeRoutingCheck();
        if (NativeMethods.IsWindow(handle) || WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Worker receiver survived pause or changed foreground.");
        input.SetAvailable(true); input.SynchronizeRoutingCheck();
        if (!input.SetTouchNavigation(true)) throw new InvalidOperationException("Touch receiver could not restart after pause.");
        input.Drain(); var restarted = input.CreateHiddenTouchReceiverForCheck();
        input.TimerRoutingCheck(() => { platform.TickCount += 500; input.Heartbeat(); });
        input.PostTouchReceiverForCheck(0x20A, 7); input.SynchronizeRoutingCheck();
        if (input.Drain().Single().Input.WheelDelta != 7) throw new InvalidOperationException("Restarted idle receiver lost its first wheel message.");
        input.SetAvailable(false); input.SynchronizeRoutingCheck();
        if (NativeMethods.IsWindow(restarted) || WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Restarted receiver leaked or changed foreground.");
    }
    private static void Messages()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        var packets = new List<(uint Message, nuint Value)>(); var disarmed = 0;
        using var receiver = new TouchWheelReceiver(new(-3000, -3000, -2600, -2700), (message, value) => packets.Add((message, value)), () => disarmed++, show: false);
        var handle = receiver.Handle;
        if (receiver.Visible || receiver.NativePassThrough) throw new InvalidOperationException("Native receiver fixture unexpectedly showed or missed its initial style.");
        receiver.SendFixtureMessage(0x20A, 1u << 16);
        receiver.SendFixtureMessage(0x20E, unchecked((nuint)(-40 << 16)));
        receiver.SendFixtureMessage(0x20A, (80u << 16) | 8);
        if (packets.Count != 3 || unchecked((short)(packets[1].Value >> 16)) != -40 || (packets[2].Value & 8) == 0)
            throw new InvalidOperationException("Direct precision, horizontal or Ctrl messages were lost.");
        receiver.SetPassThrough(true); receiver.SendFixtureMessage(0x20A, 120u << 16);
        if (!receiver.NativePassThrough || packets.Count != 3) throw new InvalidOperationException("Native transparent interval still captured a wheel.");
        receiver.SetPassThrough(false); receiver.SendFixtureMessage(0x207, 0);
        if (!receiver.NativePassThrough || disarmed != 1) throw new InvalidOperationException("An unexpected button could leave an opaque input layer.");
        receiver.Dispose();
        if (NativeMethods.IsWindow(handle) || WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Native receiver leaked or activated the desktop.");
    }
}
