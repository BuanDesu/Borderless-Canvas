using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

internal static class NavigationChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Automatic navigation follows desktop boundaries and preserves a captured gesture", AutomaticBoundary);
        yield return ("An application drag crossing the desktop cannot become a navigation drag", ApplicationDrag);
        yield return ("Escape suspends automatic navigation until an application boundary is crossed", AutomaticEscape);
        yield return ("Desktop Tab accepts Shift but excludes application input, other modifiers and overview", DesktopTab);
        yield return ("A solitary desktop right click requests exactly one shell click", DesktopRightClick);
        yield return ("Neither chord order opens a desktop context menu after release", ChordContextClick);
        yield return ("Right drag or an excursion outside the desktop cannot open a context menu", ContextClickBoundary);
        yield return ("Inactive input, wheel gestures and cancellation never replay a right click", ContextClickCancellation);
        yield return ("Navigation gate leaves ordinary application input untouched", PassThrough);
        yield return ("Navigation cannot start with a physical or swallowed button held", HeldEntry);
        yield return ("Navigation swallows complete left/right pairs and wheel input", Suppression);
        yield return ("Exiting navigation still drains swallowed button releases", ReleaseAfterExit);
        yield return ("Two-button navigation queues chord movement while keeping the system pointer live", DragGate);
        yield return ("Wheel navigation combines a burst into one history origin", WheelBurst);
        yield return ("Separate wheel bursts retain separate history even before a timer tick", WheelGap);
        yield return ("Shift wheel and horizontal wheel use the expected horizontal directions", Horizontal);
        yield return ("Left-then-right drag grabs the world and records one history entry", Drag);
        yield return ("Right-then-left drag has the same navigation semantics", ReverseDrag);
        yield return ("Single button or a stationary chord creates no navigation history", NoOp);
        yield return ("Wheel then drag ends the first gesture before beginning the next", WheelThenDrag);
        yield return ("Wheel during a chord cannot compete with drag movement", IgnoreWheelDuringDrag);
        yield return ("A mouse-to-touch boundary flushes the drag and resets held-button state before wheel input", MouseToTouch);
        yield return ("Many pointer moves coalesce into one native request per frame", Coalescing);
        yield return ("Escape flushes a final accepted delta and ends one history entry", Escape);
        yield return ("Fault cancellation discards queued deltas but keeps accepted history", Cancel);
        yield return ("Refused navigation discards the pending delta without retrying forever", Rejection);
        yield return ("Disabled navigation axes pass their wheel input to applications", AxisOptions);
        yield return ("Disabled chord navigation passes ordinary buttons without queueing", DragOptions);
        yield return ("Configured wheel distance scales full and partial notches", WheelDistance);
        yield return ("Invalid wheel distance is rejected before capturing input", InvalidDistance);
        yield return ("Precision Ctrl-wheel packets accumulate to one selection per full wheel unit", PrecisionCycle);
        yield return ("Window cycle remainders cannot cross an input boundary or an idle gap", CycleBoundary);
        yield return ("Precision horizontal and vertical pan preserve small deltas within one frame", PrecisionPan);
        yield return ("Direct contact drags the canvas without mouse chords and preserves one return origin across pauses", ContactDrag);
        yield return ("One contact owns its gesture and cancellation never uses a different contact's position", ContactOwnership);
        yield return ("Touch release includes its final position and repeated release cannot move the camera", ContactRelease);
        yield return ("A refused touch pan cannot resume from later move events without a new contact", ContactRefused);
    }
    private static void MouseToTouch()
    {
        var h = new Harness(); Chord(h); h.Send(NavigationInputKind.Move, 40, 60); h.Tick();
        h.Gesture.Accept(new(NavigationInputKind.Exit, PointD.Zero), Now); var before = h.Workspace.Camera.Position;
        h.Send(NavigationInputKind.Wheel, wheel: 120); h.Tick();
        Require(h.Workspace.Camera.Position == before + new PointD(0, -120), "A completed mouse drag blocked touch wheel input.");
        h.Send(NavigationInputKind.HorizontalWheel, wheel: -40); h.Tick();
        Require(h.Workspace.Camera.Position == before + new PointD(-40, -120), "Horizontal touch input retained mouse chord state.");
    }
    private static void ContactDrag()
    {
        var h = new Harness(); h.Gesture.Options = new(twoButtonDrag: false);
        h.Send(NavigationInputKind.PanStart, 100, 100);
        h.Send(NavigationInputKind.PanMove, 160, 140, at: 50); h.Tick(50); h.Tick(2000);
        h.Send(NavigationInputKind.PanMove, 200, 160, at: 2200); h.Tick(2200);
        Require(h.Workspace.Camera.Position == new PointD(-100, -60) && h.Records == 0 && h.Gesture.HasGesture,
            "Touch required a chord or split on an idle interval.");
        h.Send(NavigationInputKind.PanEnd, 200, 160, at: 2300);
        Require(h.Records == 1 && !h.Gesture.HasGesture, "Touch did not finish one history entry.");
        h.Workspace.ReturnToPreviousView(); Require(h.Workspace.Camera.Position == PointD.Zero, "Back did not return across the whole touch drag.");
    }
    private static void ContactOwnership()
    {
        var contact = new ContactPan();
        Require(contact.Accept(1, ContactPhase.Down, new(100, 100))?.Kind == NavigationInputKind.PanStart,
            "First contact did not start.");
        Require(contact.Accept(2, ContactPhase.Down, new(500, 500)) is null && contact.Accept(2, ContactPhase.Move, new(800, 900)) is null
            && contact.Accept(2, ContactPhase.Up, new(800, 900)) is null, "Secondary contact stole the pan.");
        contact.Accept(1, ContactPhase.Move, new(120, 130));
        Require(contact.Accept(1, ContactPhase.Cancel, PointD.Zero)?.Position == new PointD(120, 130)
            && contact.Accept(1, ContactPhase.Move, new(300, 300)) is null && contact.Cancel() is null,
            "Cancel jumped to an unrelated position or left a contact active.");
    }
    private static void ContactRelease()
    {
        var h = new Harness(); var contact = new ContactPan();
        foreach (var (phase, point) in new[] { (ContactPhase.Down, new PointD(100, 100)), (ContactPhase.Move, new PointD(110, 110)), (ContactPhase.Up, new PointD(120, 130)) })
            if (contact.Accept(7, phase, point) is { } input) h.Gesture.Accept(input, Now);
        Require(h.Workspace.Camera.Position == new PointD(-20, -30) && h.Records == 1
            && contact.Accept(7, ContactPhase.Up, new(200, 200)) is null, "Release lost its final delta or repeated it.");
    }
    private static void ContactRefused()
    {
        var h = new Harness { Reject = true };
        h.Send(NavigationInputKind.PanStart, 100, 100); h.Send(NavigationInputKind.PanMove, 120, 140); h.Tick();
        h.Reject = false; h.Send(NavigationInputKind.PanMove, 200, 200); h.Send(NavigationInputKind.PanEnd, 220, 220); h.Tick();
        Require(h.Workspace.Camera.Position == PointD.Zero && h.Requests == 1, "A refused touch sequence resumed without a new contact.");
    }
    private static void PrecisionCycle()
    {
        foreach (var delta in new[] { 1, 40, 120, -1, -40, -120 })
        {
            var wheel = new WindowCycleWheel(); var steps = 0;
            for (var i = 0; i < 120 / Math.Abs(delta); i++) steps += wheel.Add(delta, i);
            Require(steps == Math.Sign(delta), "Precision packets skipped or repeated a window.");
        }
        var reversed = new WindowCycleWheel();
        Require(reversed.Add(100, 0) == 0 && reversed.Add(-100, 1) == 0 && reversed.Add(-120, 2) == -1,
            "Reversing a partial movement selected an extra window.");
    }
    private static void CycleBoundary()
    {
        var wheel = new WindowCycleWheel();
        Require(wheel.Add(100, 0) == 0 && wheel.Add(30, 400) == 0, "Idle carried a partial selection.");
        wheel.Reset(); Require(wheel.Add(100, 500) == 0 && wheel.Add(30, 499) == 0, "Clock reversal carried a partial selection.");
        wheel.Reset(); Require(wheel.Add(100, 600) == 0, "Explicit boundary did not reset.");
        wheel.Reset(); Require(wheel.Add(0, 601) == 0 && wheel.Add(240, 602) == 2 && wheel.Add(int.MaxValue, 603) == 12,
            "Zero, multiple notches or bounded work failed.");
    }
    private static void PrecisionPan()
    {
        var h = new Harness();
        for (var i = 0; i < 40; i++)
        { h.Send(NavigationInputKind.HorizontalWheel, wheel: 1, at: i); h.Send(NavigationInputKind.Wheel, wheel: -1, at: i); }
        h.Tick(40);
        Require(h.Workspace.Camera.Position == new PointD(40, 40) && h.Requests == 1,
            "Small two-axis packets were rounded or executed outside the frame merge.");
    }
    private static void AutomaticBoundary()
    {
        var policy = new DesktopNavigationPolicy();
        Require(policy.ShouldNavigate(true, true, false, false, false), "Desktop failed to enter.");
        Require(!policy.ShouldNavigate(false, true, false, false, false), "Application input captured.");
        Require(policy.ShouldNavigate(false, true, false, true, true), "Captured drag stopped over an application.");
        Require(!policy.ShouldNavigate(true, false, false, true, true) && !policy.ShouldNavigate(true, true, true, true, true), "Unavailable/overview captured input.");
    }
    private static void ApplicationDrag()
    {
        var policy = new DesktopNavigationPolicy(); policy.ObserveButton(NavigationInputKind.LeftDown, false);
        Require(!policy.ShouldNavigate(true, true, false, false, false), "Crossing drag was captured.");
        policy.ObserveButton(NavigationInputKind.LeftUp, false);
        Require(policy.ShouldNavigate(true, true, false, false, false), "Release did not restore desktop navigation.");
    }
    private static void AutomaticEscape()
    {
        var policy = new DesktopNavigationPolicy(); policy.Suspend();
        Require(!policy.ShouldNavigate(true, true, false, false, false), "Escape immediately reentered.");
        policy.ShouldNavigate(false, true, false, false, false);
        Require(policy.ShouldNavigate(true, true, false, false, false), "Application visit did not reset Escape.");
    }
    private static void DesktopTab()
    {
        Require(DesktopNavigationPolicy.OpensOverview(true, true, false, false, false, false), "Plain desktop Tab rejected.");
        Require(!DesktopNavigationPolicy.OpensOverview(false, true, false, false, false, false)
            && !DesktopNavigationPolicy.OpensOverview(true, false, false, false, false, false)
            && !DesktopNavigationPolicy.OpensOverview(true, true, true, false, false, false)
            && !DesktopNavigationPolicy.OpensOverview(true, true, false, true, false, false)
            && !DesktopNavigationPolicy.OpensOverview(true, true, false, false, true, false)
            && !DesktopNavigationPolicy.OpensOverview(true, true, false, false, false, true), "Tab leaked through a context boundary.");
    }
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static bool Context(DesktopContextClick click, NavigationInputKind kind, double x = 0, bool captured = true, bool desktop = true)
        => click.Accept(kind, new(x, 20), captured, desktop, 4);
    private static void DesktopRightClick()
    {
        var click = new DesktopContextClick();
        Require(!Context(click, NavigationInputKind.RightDown) && !Context(click, NavigationInputKind.Move, 2)
            && Context(click, NavigationInputKind.RightUp, 2) && !Context(click, NavigationInputKind.RightUp, 2), "Single right click was lost or repeated.");
    }
    private static void ChordContextClick()
    {
        foreach (var reverse in new[] { false, true }) foreach (var rightFirst in new[] { false, true })
        {
            var click = new DesktopContextClick();
            Context(click, reverse ? NavigationInputKind.RightDown : NavigationInputKind.LeftDown);
            Context(click, reverse ? NavigationInputKind.LeftDown : NavigationInputKind.RightDown);
            Require(!Context(click, rightFirst ? NavigationInputKind.RightUp : NavigationInputKind.LeftUp)
                && !Context(click, rightFirst ? NavigationInputKind.LeftUp : NavigationInputKind.RightUp), "Chord leaked a shell click.");
        }
    }
    private static void ContextClickBoundary()
    {
        var click = new DesktopContextClick(); Context(click, NavigationInputKind.RightDown);
        Context(click, NavigationInputKind.Move, 10); Context(click, NavigationInputKind.Move);
        Require(!Context(click, NavigationInputKind.RightUp), "Right drag became a click after returning to its origin.");
        Context(click, NavigationInputKind.RightDown); Context(click, NavigationInputKind.Move, desktop: false);
        Require(!Context(click, NavigationInputKind.RightUp), "Crossing an application replayed a click.");
        Context(click, NavigationInputKind.RightDown, desktop: false);
        Require(!Context(click, NavigationInputKind.RightUp), "Application-origin right click was captured.");
    }
    private static void ContextClickCancellation()
    {
        var click = new DesktopContextClick(); Context(click, NavigationInputKind.RightDown, captured: false);
        Require(!Context(click, NavigationInputKind.RightUp, captured: false), "Inactive click replayed.");
        Context(click, NavigationInputKind.RightDown); Context(click, NavigationInputKind.Wheel);
        Require(!Context(click, NavigationInputKind.RightUp), "Wheel interaction replayed.");
        Context(click, NavigationInputKind.RightDown); click.Cancel();
        Require(!Context(click, NavigationInputKind.RightUp), "Cancelled navigation replayed.");
        Context(click, NavigationInputKind.RightDown);
        Require(!Context(click, NavigationInputKind.RightUp, captured: false), "Post-exit release replayed.");
    }
    private sealed class Harness
    {
        internal readonly CanvasWorkspace Workspace = new(new(1920, 1080));
        internal readonly NavigationGestureSession Gesture;
        internal int Requests, Records;
        internal bool Reject;
        internal Harness() => Gesture = new(() => Workspace.Camera.Position,
            (target, _) => { Requests++; if (Reject) return false; Workspace.SetCameraPosition(target); return true; },
            origin => { if (origin != Workspace.Camera.Position) Records++; Workspace.RecordNavigationOrigin(origin); });
        internal void Send(NavigationInputKind kind, double x = 0, double y = 0, int wheel = 0, bool shift = false, int at = 0) =>
            Gesture.Accept(new(kind, new(x, y), wheel, shift), Now.AddMilliseconds(at));
        internal void Tick(int at = 0) => Gesture.Tick(Now.AddMilliseconds(at));
    }
    private static void PassThrough()
    {
        var gate = new NavigationInputGate();
        foreach (var kind in Enum.GetValues<NavigationInputKind>()) Require(gate.Route(kind) == (false, false), "Inactive input intercepted.");
    }
    private static void HeldEntry()
    {
        var gate = new NavigationInputGate(); Require(!gate.TryEnter(true), "Entered while physical button held.");
        gate.TryEnter(false); gate.Route(NavigationInputKind.LeftDown); gate.Exit();
        Require(!gate.TryEnter(false), "Entered while a suppressed release is pending.");
        gate.Route(NavigationInputKind.LeftUp); Require(gate.TryEnter(false), "Could not enter after release.");
    }
    private static void Suppression()
    {
        var gate = new NavigationInputGate(); gate.TryEnter(false);
        foreach (var kind in new[] { NavigationInputKind.LeftDown, NavigationInputKind.LeftUp, NavigationInputKind.RightDown,
            NavigationInputKind.RightUp, NavigationInputKind.Wheel, NavigationInputKind.HorizontalWheel })
            Require(gate.Route(kind) == (true, true), "Navigation leaked a click or wheel.");
        Require(!gate.HasHeldButtons, "Completed pair left a held button.");
    }
    private static void ReleaseAfterExit()
    {
        var gate = new NavigationInputGate(); gate.TryEnter(false);
        gate.Route(NavigationInputKind.LeftDown); gate.Route(NavigationInputKind.RightDown); gate.Exit();
        Require(gate.Route(NavigationInputKind.LeftUp) == (true, false) && gate.Route(NavigationInputKind.RightUp) == (true, false)
            && gate.Route(NavigationInputKind.RightUp) == (false, false), "Release leaked or a later normal click was swallowed.");
    }
    private static void DragGate()
    {
        var gate = new NavigationInputGate(); gate.TryEnter(false); gate.Route(NavigationInputKind.RightDown);
        Require(gate.Route(NavigationInputKind.Move) == (false, false), "Single button moved camera.");
        gate.Route(NavigationInputKind.LeftDown); Require(gate.Route(NavigationInputKind.Move) == (false, true), "Chord delta was lost or the system pointer was blocked.");
        gate.Route(NavigationInputKind.RightUp); Require(gate.Route(NavigationInputKind.Move) == (false, false), "Released chord still drags.");
    }
    private static void WheelBurst()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Tick();
        h.Send(NavigationInputKind.Wheel, wheel: -120, at: 100); h.Tick(100); h.Tick(351);
        Require(h.Workspace.Camera.Position == new PointD(0, 240) && h.Records == 1, "Burst history fragmented.");
        h.Workspace.ReturnToPreviousView(); Require(h.Workspace.Camera.Position == PointD.Zero, "Back did not undo the whole burst.");
    }
    private static void WheelGap()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Send(NavigationInputKind.Wheel, wheel: -120, at: 300); h.Tick(600);
        Require(h.Records == 2, "Distinct bursts collapsed."); h.Workspace.ReturnToPreviousView();
        Require(h.Workspace.Camera.Position == new PointD(0, 120), "Second origin incorrect.");
    }
    private static void Horizontal()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120, shift: true); h.Send(NavigationInputKind.HorizontalWheel, wheel: 120); h.Tick(300);
        Require(h.Workspace.Camera.Position == new PointD(240, 0) && h.Records == 1, "Horizontal direction or grouping incorrect.");
    }
    private static void Chord(Harness h, bool reverse = false)
    {
        h.Send(reverse ? NavigationInputKind.RightDown : NavigationInputKind.LeftDown, 400, 300);
        h.Send(reverse ? NavigationInputKind.LeftDown : NavigationInputKind.RightDown, 400, 300);
    }
    private static void Drag() => DragCore(false);
    private static void ReverseDrag() => DragCore(true);
    private static void DragCore(bool reverse)
    {
        var h = new Harness(); Chord(h, reverse); h.Send(NavigationInputKind.Move, 600, 400); h.Tick();
        h.Send(NavigationInputKind.Move, 700, 500); h.Send(NavigationInputKind.RightUp, 700, 500);
        Require(h.Workspace.Camera.Position == new PointD(-300, -200) && h.Records == 1, "Drag direction/history wrong.");
    }
    private static void NoOp()
    {
        var h = new Harness(); h.Send(NavigationInputKind.LeftDown); h.Send(NavigationInputKind.LeftUp);
        Chord(h); h.Send(NavigationInputKind.LeftUp); h.Tick(300);
        Require(h.Requests == 0 && h.Records == 0, "Empty gesture moved camera or recorded history.");
    }
    private static void WheelThenDrag()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120); Chord(h); h.Send(NavigationInputKind.Move, 500, 300);
        h.Send(NavigationInputKind.LeftUp); Require(h.Records == 2 && h.Workspace.Camera.Position == new PointD(-100, 120), "Gesture boundary was lost.");
    }
    private static void IgnoreWheelDuringDrag()
    {
        var h = new Harness(); Chord(h); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Send(NavigationInputKind.Move, 500, 300); h.Send(NavigationInputKind.LeftUp);
        Require(h.Workspace.Camera.Position == new PointD(-100, 0), "Wheel competed with drag.");
    }
    private static void Coalescing()
    {
        var h = new Harness(); Chord(h);
        for (var i = 0; i < 200; i++) h.Send(NavigationInputKind.Move, 401 + i, 301 + i);
        Require(h.Requests == 0, "Native movement happened in input processing."); h.Tick(); h.Send(NavigationInputKind.LeftUp);
        Require(h.Requests == 1 && h.Records == 1 && h.Workspace.Camera.Position == new PointD(-200, -200), "Moves were not coalesced.");
    }
    private static void Escape()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Send(NavigationInputKind.Exit);
        Require(!h.Gesture.HasGesture && h.Workspace.Camera.Position == new PointD(0, 120) && h.Records == 1, "Exit lost final movement or history.");
    }
    private static void Cancel()
    {
        var h = new Harness(); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Tick(); h.Send(NavigationInputKind.Wheel, wheel: -120);
        h.Gesture.Cancel(); h.Tick(500);
        Require(h.Workspace.Camera.Position == new PointD(0, 120) && h.Requests == 1 && h.Records == 1, "Cancel applied stale input or lost accepted history.");
    }
    private static void Rejection()
    {
        var h = new Harness { Reject = true }; h.Send(NavigationInputKind.Wheel, wheel: -120); h.Tick(); h.Tick(500);
        Require(h.Requests == 1 && h.Records == 0 && !h.Gesture.HasGesture && h.Workspace.Camera.Position == PointD.Zero, "Rejected move retried or recorded false history.");
    }
    private static void AxisOptions()
    {
        var gate = new NavigationInputGate { Options = new(verticalWheel: false) }; gate.TryEnter(false);
        Require(gate.Route(NavigationInputKind.Wheel) == (false, false) && gate.Route(NavigationInputKind.Wheel, shift: true) == (true, true), "Disabled vertical axis intercepted input.");
        gate.Options = new(horizontalWheel: false);
        Require(gate.Route(NavigationInputKind.Wheel, shift: true) == (false, false) && gate.Route(NavigationInputKind.HorizontalWheel) == (false, false), "Disabled horizontal axis intercepted input.");
        var h = new Harness(); h.Gesture.Options = new(verticalWheel: false); h.Send(NavigationInputKind.Wheel, wheel: -120); h.Tick(500);
        Require(h.Requests == 0 && h.Records == 0, "Disabled local axis still moved camera.");
    }
    private static void DragOptions()
    {
        var gate = new NavigationInputGate { Options = new(twoButtonDrag: false) }; gate.TryEnter(false);
        foreach (var kind in new[] { NavigationInputKind.LeftDown, NavigationInputKind.RightDown, NavigationInputKind.Move, NavigationInputKind.LeftUp, NavigationInputKind.RightUp })
            Require(gate.Route(kind) == (false, false), "Disabled chord intercepted application input.");
        var h = new Harness(); h.Gesture.Options = new(twoButtonDrag: false); Chord(h); h.Send(NavigationInputKind.Move, 500, 300); h.Tick();
        Require(h.Requests == 0, "Disabled chord still moved camera.");
    }
    private static void WheelDistance()
    {
        var h = new Harness(); h.Gesture.Options = new(wheelDistance: 240);
        h.Send(NavigationInputKind.Wheel, wheel: -120); h.Send(NavigationInputKind.Wheel, wheel: -60); h.Tick(300);
        Require(h.Workspace.Camera.Position == new PointD(0, 360) && h.Records == 1, "Wheel sensitivity lost partial notches or fragmented history.");
    }
    private static void InvalidDistance()
    {
        foreach (var value in new[] { -1, 0, 29, 601, int.MaxValue })
        {
            try { _ = new NavigationInputOptions(wheelDistance: value); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Invalid wheel distance accepted.");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
