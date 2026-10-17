using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class CoordinateChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Negative monitor origins and taskbar offsets round-trip at native scale", RoundTrip);
        yield return ("All four taskbar edges determine viewport size and physical origin", TaskbarEdges);
        yield return ("Camera pan changes physical position without changing native dimensions", Pan);
        yield return ("Projection rounds negative and positive half pixels consistently without resize jitter", Rounding);
        yield return ("Int32 endpoints are accepted while overflowing edges and sizes are rejected", IntegerBounds);
        yield return ("Far and non-finite intermediate coordinates fail without clamping or exceptions", FarCoordinates);
        yield return ("Invalid and overflowing physical rectangles are rejected", InvalidPixels);
        yield return ("Work areas outside their monitor and unknown geometry cannot form a display", InvalidWorkArea);
        yield return ("Layout requires one primary and unique display names", InvalidTopology);
        yield return ("Only a single known 96 DPI screen matches the initial baseline", Baseline);
        yield return ("Display snapshots copy their input and expose read-only collections", ImmutableSnapshot);
        yield return ("Viewport changes preserve camera, layout and navigation history", ResizeViewport);
    }

    private static void RoundTrip()
    {
        var area = new PixelRect(-1920, -1160, 0, -80);
        var mapper = new DesktopCoordinateMapper(new(8000, -6000), area);
        var physical = new PixelRect(-1800, -1000, -1160, -520);
        var world = mapper.PhysicalToWorld(physical);
        Require(world.Position == new PointD(8120, -5840), "Work-area offset lost.");
        Require(mapper.TryProjectWindow(world, out var projected) && projected == physical, "Rectangle did not round-trip.");
        Require(mapper.WorldToPhysical(mapper.Camera.Position) == area.Position, "Camera is not at work-area origin.");
        var point = new PointD(-1234.25, -570.75);
        Require(mapper.WorldToPhysical(mapper.PhysicalToWorld(point)) == point, "Point did not round-trip.");
    }

    private static void TaskbarEdges()
    {
        PixelRect[] areas = [new(0, 40, 1920, 1080), new(50, 0, 1920, 1080), new(0, 0, 1870, 1080), new(0, 0, 1920, 1040)];
        foreach (var area in areas)
        {
            var mapper = new DesktopCoordinateMapper(PointD.Zero, area);
            Require(mapper.Camera.ViewportSize == area.Size && mapper.WorldToPhysical(PointD.Zero) == area.Position,
                "Taskbar inset was ignored.");
        }
    }

    private static void Pan()
    {
        var area = new PixelRect(-1920, 40, 0, 1080);
        var world = Rect(200, -300, 640, 480);
        var a = new DesktopCoordinateMapper(PointD.Zero, area);
        var b = new DesktopCoordinateMapper(new(9000, -7000), area);
        Require(a.TryProjectWindow(world, out var pa) && b.TryProjectWindow(world, out var pb)
            && pa.Size == pb.Size && pb.Position - pa.Position == new PointD(-9000, 7000), "Pan changed native size or displacement.");
    }

    private static void Rounding()
    {
        var mapper = new DesktopCoordinateMapper(PointD.Zero, new(0, 0, 1000, 1000));
        Require(mapper.TryProjectWindow(Rect(-10.5, 10.5, 640, 480), out var p)
            && p.Left == -11 && p.Top == 11 && p.Width == 640, "Half-pixel rounding changed.");
        for (var i = 0; i < 10; i++)
            Require(new DesktopCoordinateMapper(new(i / 10d, 0), mapper.WorkArea)
                .TryProjectWindow(Rect(10.4, 0, 640, 480), out p) && p.Width == 640, "Fractional camera resized window.");
        Require(!mapper.TryProjectWindow(Rect(0, 0, 0.4, 480), out _), "Zero rounded size admitted.");
    }

    private static void IntegerBounds()
    {
        var mapper = new DesktopCoordinateMapper(PointD.Zero, new(0, 0, 1000, 1000));
        Require(mapper.TryProjectWindow(Rect(int.MinValue, int.MinValue, 1, 1), out _), "Min coordinate rejected.");
        Require(mapper.TryProjectWindow(Rect(int.MaxValue - 1d, int.MaxValue - 1d, 1, 1), out _), "Max edge rejected.");
        Require(!mapper.TryProjectWindow(Rect(int.MaxValue, 0, 1, 1), out _), "Right edge overflow admitted.");
        Require(!mapper.TryProjectWindow(Rect(0, int.MaxValue, 1, 1), out _), "Bottom edge overflow admitted.");
        Require(!mapper.TryProjectWindow(Rect((double)int.MinValue - 1, 0, 1, 1), out _), "Position underflow admitted.");
        Require(!mapper.TryProjectWindow(Rect(0, 0, (double)int.MaxValue + 1, 1), out _), "Size overflow admitted.");
    }

    private static void FarCoordinates()
    {
        var area = new PixelRect(0, 0, 1000, 1000);
        var mapper = new DesktopCoordinateMapper(new(-double.MaxValue, 0), area);
        Require(!mapper.TryProjectWindow(Rect(double.MaxValue, 0, 100, 100), out var failed) && failed == default,
            "Infinite intermediate was not rejected.");
        Require(!new DesktopCoordinateMapper(PointD.Zero, area).TryProjectWindow(Rect(1e20, 0, 100, 100), out _), "Far coordinate clamped.");
        Require(!mapper.TryProjectWindow(default, out _), "Default rectangle admitted.");
    }

    private static void InvalidPixels()
    {
        Throws<ArgumentOutOfRangeException>(() => new PixelRect(0, 0, 0, 100));
        Throws<ArgumentOutOfRangeException>(() => new PixelRect(int.MinValue, 0, int.MaxValue, 100));
        Throws<ArgumentOutOfRangeException>(() => new DesktopCoordinateMapper(PointD.Zero, default));
        Require(!default(PixelRect).Contains(default), "Default rectangle valid.");
    }

    private static DisplayMonitor Monitor(string name = "screen", bool primary = true, uint? dpi = 96) =>
        new(name, primary, new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), dpi);

    private static void InvalidWorkArea()
    {
        Throws<ArgumentException>(() => new DisplayMonitor("screen", true, new(0, 0, 1000, 1000), new(-1, 0, 1000, 900), 96));
        Throws<ArgumentException>(() => new DisplayMonitor("screen", true, default, default, 96));
        Throws<ArgumentOutOfRangeException>(() => Monitor(dpi: 0));
        Throws<ArgumentOutOfRangeException>(() => new DesktopCoordinateMapper(PointD.Zero, new(0, 0, 100, 100)).PhysicalToWorld(default(PixelRect)));
    }

    private static void InvalidTopology()
    {
        Throws<ArgumentException>(() => new DisplayLayout([]));
        Throws<ArgumentException>(() => new DisplayLayout([Monitor(primary: false)]));
        Throws<ArgumentException>(() => new DisplayLayout([Monitor(), Monitor("other")]));
        Throws<ArgumentException>(() => new DisplayLayout([Monitor(), Monitor("SCREEN", false)]));
    }

    private static void Baseline()
    {
        Require(new DisplayLayout([Monitor()]).IsSingleScreen100Percent, "100% baseline missed.");
        Require(!new DisplayLayout([Monitor(dpi: null)]).IsSingleScreen100Percent, "Unknown DPI became 100%.");
        Require(!new DisplayLayout([Monitor(dpi: 144)]).IsSingleScreen100Percent, "150% declared compatible.");
        Require(!new DisplayLayout([Monitor(), Monitor("other", false)]).IsSingleScreen100Percent, "Multiscreen declared baseline.");
    }

    private static void ImmutableSnapshot()
    {
        var input = new List<DisplayMonitor> { Monitor() };
        var snapshot = new DisplayLayout(input);
        input.Clear();
        Require(snapshot.Monitors.Count == 1, "Input changed snapshot.");
        Throws<NotSupportedException>(() => ((IList<DisplayMonitor>)snapshot.Monitors).Clear());
    }

    private static void ResizeViewport()
    {
        var model = new CanvasWorkspace(new(1280, 720));
        var window = new CanvasWindow(Guid.NewGuid(), "Fixture", Rect(-100, 200, 640, 480));
        model.UpsertWindow(window); model.NavigateTo(new(500, -800));
        model.SetViewportSize(new(1920, 1040));
        Require(model.Camera.Position == new PointD(500, -800) && model.Windows.Single() == window, "Resize changed layout.");
        Require(model.ReturnToPreviousView() && model.Camera.Position == PointD.Zero && model.Camera.ViewportSize == new SizeD(1920, 1040), "Resize polluted history.");
        Throws<ArgumentOutOfRangeException>(() => model.SetViewportSize(default));
        Require(model.Camera.ViewportSize == new SizeD(1920, 1040), "Failed resize corrupted camera.");
    }

    private static RectD Rect(double x, double y, double width, double height) => new(new PointD(x, y), new SizeD(width, height));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
