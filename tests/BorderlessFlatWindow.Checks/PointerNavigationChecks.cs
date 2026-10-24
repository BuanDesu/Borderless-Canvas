using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

internal static class PointerNavigationChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Pointer navigation requires outward input at a real desktop outer edge", OuterEdges);
        yield return ("Pointer navigation preserves screen seams and handles offset displays and corners", Seams);
        yield return ("Crosshair consumes relative displacement with bounded sensitivity and edge opt-out", Crosshair);
        yield return ("Continuous pointer movement records one navigation origin per burst", History);
        yield return ("Normalized absolute pointer coordinates map primary and negative virtual surfaces", AbsoluteMapping);
        yield return ("Absolute pointer deltas rebaseline on device, surface, gap and warp changes", AbsoluteBaselines);
        yield return ("Legacy absolute deltas never turn a stationary repeat into continued motion", AbsoluteStationary);
        yield return ("Edge speed ramps from low speed to a bounded plateau under continuous outward input", EdgeRamp);
        yield return ("Edge movement depends on elapsed outward input rather than device distance or polling rate", EdgeRate);
        yield return ("Edge stop, reversal and stale input restart slowly without catch-up motion", EdgeStop);
        yield return ("Edge position changes, seams, disabled settings and reset discard earlier acceleration", EdgeBoundaries);
        yield return ("Corner axes accelerate independently and pointer gain scales speed", EdgeAxes);
    }
    private static void EdgeRamp()
    {
        var edge = new EdgePanMotion(); var options = new NavigationInputOptions(); var pointer = new PointD(1919, 500);
        Require(edge.Observe(new(1, 0), pointer, Screens, options, 0) == PointD.Zero, "First sample jumped.");
        var first = edge.Observe(new(1, 0), pointer, Screens, options, 20).X; var latest = first;
        for (var t = 40; t <= 1600; t += 20) latest = edge.Observe(new(1, 0), pointer, Screens, options, t).X;
        Require(first > 0 && first < 5 && latest > first * 8 && Math.Abs(latest - EdgePanMotion.MaximumSpeed * .02) < 1e-8,
            "Edge did not ramp smoothly to its bounded time-based speed.");
    }
    private static double EdgeDistance(int step, double amount)
    {
        var edge = new EdgePanMotion(); var sum = 0d;
        for (var t = 0; t <= 1600; t += step) sum += edge.Observe(new(amount, 0), new(1919, 500), Screens, new(), t).X;
        return sum;
    }
    private static void EdgeRate()
    {
        var baseline = EdgeDistance(1, 1);
        Require(Math.Abs(baseline - EdgeDistance(8, 80)) < 1e-7 && Math.Abs(baseline - EdgeDistance(16, 2)) < 1e-7,
            "Device distance or polling frequency changed the velocity profile.");
        var edge = new EdgePanMotion();
        for (var i = 0; i < 100; i++) Require(edge.Observe(new(20, 0), new(1919, 500), Screens, new(), 50) == PointD.Zero,
            "Packets within one clock tick invented elapsed movement.");
    }
    private static void EdgeStop()
    {
        var edge = new EdgePanMotion(); var pointer = new PointD(1919, 500); var options = new NavigationInputOptions();
        for (var t = 0; t <= 1200; t += 20) edge.Observe(new(1, 0), pointer, Screens, options, t);
        Require(edge.Observe(PointD.Zero, pointer, Screens, options, 1220) == PointD.Zero
            && edge.Observe(new(1, 0), pointer, Screens, options, 1240) == PointD.Zero
            && edge.Observe(new(1, 0), pointer, Screens, options, 1260).X < 5, "Stopping retained acceleration.");
        Require(edge.Observe(new(-1, 0), pointer, Screens, options, 1280) == PointD.Zero
            && edge.Observe(new(1, 0), pointer, Screens, options, 1300) == PointD.Zero, "Reversal did not stop/reset the edge.");
        Require(edge.Observe(new(1, 0), pointer, Screens, options, 1800) == PointD.Zero
            && edge.Observe(new(1, 0), pointer, Screens, options, 1790) == PointD.Zero, "Idle or clock reversal caught up old movement.");
    }
    private static void EdgeBoundaries()
    {
        var edge = new EdgePanMotion(); var options = new NavigationInputOptions();
        edge.Observe(new(1, 0), new(1919, 500), Screens, options, 0);
        edge.Observe(new(1, 0), new(1919, 500), Screens, options, 20);
        edge.ConstrainToPointer(new(1800, 500), Screens, options);
        Require(edge.Observe(new(1, 0), new(1919, 500), Screens, options, 40) == PointD.Zero, "Leaving the border retained acceleration.");
        PixelRect[] joined = [Screens[0], new(1920, 0, 3840, 1080)];
        Require(edge.Observe(new(1, 0), new(1919, 500), joined, options, 60) == PointD.Zero, "A monitor seam accelerated.");
        Require(edge.Observe(new(1, 0), new(1919, 500), Screens, new(edgePan: false), 80) == PointD.Zero, "Opt-out moved the camera.");
        edge.Reset(); Require(edge.Observe(new(1, 0), new(1919, 500), Screens, options, 100) == PointD.Zero, "Reset retained time.");
    }
    private static void EdgeAxes()
    {
        var edge = new EdgePanMotion(); var options = new NavigationInputOptions(pointerSpeed: 2);
        edge.Observe(new(1, 1), new(1919, 1079), Screens, options, 0);
        var diagonal = edge.Observe(new(100, 2), new(1919, 1079), Screens, options, 20);
        Require(diagonal.X > 0 && diagonal.X == diagonal.Y && diagonal.X < 10, "Corner axes used packet distance.");
        var horizontal = edge.Observe(new(1, -1), new(1919, 1079), Screens, options, 40);
        Require(horizontal.X > diagonal.X && horizontal.Y == 0, "Stopping one corner axis interrupted the other.");
    }
    private static readonly PixelRect[] Screens = [new(0, 0, 1920, 1080)];
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void OuterEdges()
    {
        var options = new NavigationInputOptions();
        Require(PointerNavigation.CameraDelta(new(8, 4), new(1919, 500), Screens, options, false) == new PointD(8, 0), "Right edge did not retain only outward motion.");
        Require(PointerNavigation.CameraDelta(new(-8, 0), new(1919, 500), Screens, options, false) == PointD.Zero, "Inward motion advanced the camera.");
        Require(PointerNavigation.CameraDelta(new(8, 4), new(100, 100), Screens, options, false) == PointD.Zero, "Hover inside a window advanced the camera.");
        Require(PointerNavigation.CameraDelta(new(8, 4), new(1919, 1079), Screens, options, false) == new PointD(8, 4), "Corner lost an exposed axis.");
        Require(PointerNavigation.CameraDelta(PointD.Zero, new(1919, 1079), Screens, options, false) == PointD.Zero, "Hover without continued input moved the camera.");
    }
    private static void Seams()
    {
        PixelRect[] screens = [new(-1920, -200, 0, 880), new(0, 0, 1920, 1080)]; var options = new NavigationInputOptions();
        Require(PointerNavigation.CameraDelta(new(10, 0), new(-1, 200), screens, options, false) == PointD.Zero, "Cross-screen travel was consumed.");
        Require(PointerNavigation.CameraDelta(new(-10, 0), new(0, 200), screens, options, false) == PointD.Zero, "Reverse seam travel was consumed.");
        Require(PointerNavigation.CameraDelta(new(-10, 0), new(0, 1000), screens, options, false) == new PointD(-10, 0), "Exposed offset edge was mistaken for a seam.");
        Require(PointerNavigation.CameraDelta(new(-10, -4), new(-1920, -200), screens, options, false) == new PointD(-10, -4), "Negative origin corner failed.");
    }
    private static void Crosshair()
    {
        var options = new NavigationInputOptions(edgePan: false, pointerSpeed: 1.5);
        Require(PointerNavigation.CameraDelta(new(10, -8), new(960, 540), Screens, options, true) == new PointD(15, -12), "Crosshair lost relative motion or sensitivity.");
        Require(PointerNavigation.CameraDelta(new(10, -8), new(1919, 0), Screens, options, false) == PointD.Zero, "Edge opt-out was ignored.");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, .1, 5 })
        { try { _ = new NavigationInputOptions(pointerSpeed: value); } catch (ArgumentOutOfRangeException) { continue; } throw new InvalidOperationException("Invalid pointer gain accepted."); }
    }
    private static void AbsoluteMapping()
    {
        var screen = new PixelRect(-1920, -200, 1920, 880);
        Require(AbsolutePointerMotion.MapNormalized(PointD.Zero, screen) == new PointD(-1920, -200)
            && AbsolutePointerMotion.MapNormalized(new(65535, 65535), screen) == new PointD(1919, 879), "Absolute endpoints lost virtual origin or bounds.");
        try { AbsolutePointerMotion.MapNormalized(new(65536, 0), screen); throw new Exception("Invalid absolute coordinates accepted."); } catch (ArgumentOutOfRangeException) { }
    }
    private static void AbsoluteBaselines()
    {
        var tracker = new AbsolutePointerMotion(); var screen = Screens[0];
        Require(tracker.Observe(new(100, 200), screen, 1, 0) == PointD.Zero && tracker.Observe(new(130, 190), screen, 1, 16) == new PointD(30, -10), "First absolute sample moved or a later increment was lost.");
        Require(tracker.Observe(new(800, 600), screen, 2, 32) == PointD.Zero && tracker.Observe(new(810, 605), screen, 2, 1000) == PointD.Zero
            && tracker.Observe(new(1900, 1000), screen, 2, 1016) == PointD.Zero, "Device, gap or warp retained stale motion.");
        Require(tracker.Observe(new(10, 20), new(-1920, 0, 1920, 1080), 2, 1032) == PointD.Zero, "Surface change retained a stale baseline.");
    }
    private static void AbsoluteStationary()
    {
        var tracker = new AbsolutePointerMotion(); tracker.Observe(new(100, 200), Screens[0], -1, 0);
        for (var at = 1; at < 100; at++) Require(tracker.Observe(new(100, 200), Screens[0], -1, at) == PointD.Zero, "A repeated absolute position panned the camera.");
        tracker.Reset(); Require(tracker.Observe(new(400, 300), Screens[0], -1, 100) == PointD.Zero, "Reset did not clear absolute history.");
    }
    private static void History()
    {
        var camera = PointD.Zero; var origins = new List<PointD>(); var at = DateTimeOffset.UtcNow;
        var gestures = new NavigationGestureSession(() => camera, (next, _) => { camera = next; return true; }, origins.Add);
        for (var i = 0; i < 8; i++) { gestures.Accept(new(NavigationInputKind.CameraDelta, new(10, 4)), at.AddMilliseconds(i * 16)); gestures.Tick(at.AddMilliseconds(i * 16)); }
        gestures.Tick(at.AddMilliseconds(400));
        Require(camera == new PointD(80, 32) && origins.SequenceEqual(new[] { PointD.Zero }), "Pointer burst wrote per-frame history or lost motion.");
    }
}
