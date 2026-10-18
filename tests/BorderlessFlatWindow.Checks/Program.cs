using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

var cases = new (string Name, Action Run)[]
{
    ("Pan preserves every window's world bounds and relative displacement", PanPreservesLayout),
    ("Focus moves the camera to a distant window without pulling that window home", FocusPreservesLayout),
    ("Home and back restore views without gathering windows", HomeAndBack),
    ("Overview preserves a uniform scale and maps negative coordinates round-trip", OverviewIsSpatial),
    ("Bookmarks exceed nine slots and retain stable identities after removal", DynamicBookmarks),
    ("Invalid geometry is rejected before projection", InvalidGeometry),
    ("Missing and closed targets do not move the camera", MissingTargets)
};
var failed = 0;
var allCases = cases.Concat(DiscoveryChecks.Cases()).Concat(CoordinateChecks.Cases()).Concat(LifecycleChecks.Cases())
    .Concat(RecoveryChecks.Cases()).Concat(PlacementChecks.Cases()).Concat(ManagementChecks.Cases()).Concat(NavigationChecks.Cases())
    .Concat(OverviewChecks.Cases()).ToArray();
foreach (var (name, run) in allCases)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"{allCases.Length - failed}/{allCases.Length} checks passed.");
return failed == 0 ? 0 : 1;

static CanvasWindow Window(string name, double x, double y) =>
    new(Guid.NewGuid(), name, new(new PointD(x, y), new SizeD(640, 480)));

static void PanPreservesLayout()
{
    var model = new CanvasWorkspace(new(1280, 720));
    var a = Window("A", -1300, 220);
    var b = Window("B", 840, -900);
    model.UpsertWindow(a); model.UpsertWindow(b);
    var displacement = model.Camera.WorldToViewport(b.Bounds.Position) - model.Camera.WorldToViewport(a.Bounds.Position);
    for (var i = 0; i < 100; i++) model.PanBy(new(137, -91));
    Require(model.Windows.SequenceEqual(new[] { a, b }), "Pan changed the world layout.");
    Require(displacement == model.Camera.WorldToViewport(b.Bounds.Position) - model.Camera.WorldToViewport(a.Bounds.Position),
        "Relative positions changed.");
}

static void FocusPreservesLayout()
{
    var model = new CanvasWorkspace(new(1280, 720));
    var target = Window("Distant", 12000, -8500);
    model.UpsertWindow(target);
    Require(model.FocusWindow(target.Id), "Target not found.");
    Require(model.Windows.Single() == target, "Focus relocated the window.");
    Require(model.Camera.WorldToViewport(target.Bounds.Center) == new PointD(640, 360), "Target not centered.");
}

static void HomeAndBack()
{
    var model = new CanvasWorkspace(new(1280, 720));
    var target = Window("A", 2100, -200);
    model.UpsertWindow(target);
    model.NavigateTo(new(4000, -3000));
    model.ReturnHome();
    Require(model.Camera.Position == PointD.Zero, "Home did not reach the origin.");
    Require(model.ReturnToPreviousView() && model.Camera.Position == new PointD(4000, -3000), "Back lost the previous view.");
    Require(model.Windows.Single() == target, "Home gathered a window.");
}

static void OverviewIsSpatial()
{
    var a = Window("A", -2500, -1500);
    var b = Window("B", 1500, 800);
    var projection = OverviewProjection.Fit([a.Bounds, b.Bounds], new(900, 600));
    var pa = projection.ToMap(a.Bounds);
    var pb = projection.ToMap(b.Bounds);
    Require(pa.Position.X < pb.Position.X && pa.Position.Y < pb.Position.Y, "Spatial order changed.");
    Near(pa.Size.Width / a.Bounds.Size.Width, pb.Size.Height / b.Bounds.Size.Height);
    Near(pb.Position.X - pa.Position.X, (b.Bounds.Position.X - a.Bounds.Position.X) * projection.Scale);
    var original = projection.ToWorld(projection.ToMap(a.Bounds.Center));
    Near(original.X, a.Bounds.Center.X); Near(original.Y, a.Bounds.Center.Y);
    Require(pa.Position.X >= 24 - 1e-8 && pa.Position.Y >= 24 - 1e-8 && pb.Right <= 876 + 1e-8 && pb.Bottom <= 576 + 1e-8,
        "Fitted map exceeds its padding.");
}

static void DynamicBookmarks()
{
    var model = new CanvasWorkspace(new(1280, 720));
    for (var i = 0; i < 128; i++) { model.NavigateTo(new(i * 1500, -i * 800)); model.AddBookmark($"Area {i}"); }
    var last = model.Bookmarks[^1];
    model.RemoveBookmark(model.Bookmarks[0].Id);
    model.ReturnHome();
    Require(model.GoToBookmark(last.Id) && model.Camera.Position == last.CameraPosition, "Bookmark identity shifted.");
    Require(model.Bookmarks.Count == 127, "Bookmarks have a fixed slot limit.");
}

static void InvalidGeometry()
{
    Throws<ArgumentOutOfRangeException>(() => new PointD(double.NaN, 0));
    Throws<ArgumentOutOfRangeException>(() => new SizeD(0, 100));
    Throws<ArgumentOutOfRangeException>(() => new Camera2D(PointD.Zero, default));
    Throws<ArgumentException>(() => OverviewProjection.Fit([], new(200, 100)));
    Throws<ArgumentOutOfRangeException>(() => OverviewProjection.Fit([Window("A", 0, 0).Bounds], new(40, 40)));
}

static void MissingTargets()
{
    var model = new CanvasWorkspace(new(1280, 720));
    var target = Window("A", 2300, 1900);
    model.UpsertWindow(target); model.RemoveWindow(target.Id);
    Require(!model.FocusWindow(target.Id) && !model.GoToBookmark(Guid.NewGuid()), "Missing target accepted.");
    Require(model.Camera.Position == PointD.Zero && !model.ReturnToPreviousView(), "Missing target changed navigation history.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");

static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
