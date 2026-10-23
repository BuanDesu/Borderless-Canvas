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
    private static void History()
    {
        var camera = PointD.Zero; var origins = new List<PointD>(); var at = DateTimeOffset.UtcNow;
        var gestures = new NavigationGestureSession(() => camera, (next, _) => { camera = next; return true; }, origins.Add);
        for (var i = 0; i < 8; i++) { gestures.Accept(new(NavigationInputKind.CameraDelta, new(10, 4)), at.AddMilliseconds(i * 16)); gestures.Tick(at.AddMilliseconds(i * 16)); }
        gestures.Tick(at.AddMilliseconds(400));
        Require(camera == new PointD(80, 32) && origins.SequenceEqual(new[] { PointD.Zero }), "Pointer burst wrote per-frame history or lost motion.");
    }
}
