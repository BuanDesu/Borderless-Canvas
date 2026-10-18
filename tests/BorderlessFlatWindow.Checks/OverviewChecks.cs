using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class OverviewChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Overview keeps world relationships, native aspect and a uniform projection", Geometry);
        yield return ("Overview query changes matching and selection without refitting or reordering", Search);
        yield return ("Overview direction selection follows spatial neighbors on every axis", Directions);
        yield return ("Overview directions cannot wrap to an opposite half-plane", NoWrap);
        yield return ("Overview empty results cannot confirm or hit dimmed cards", EmptySearch);
        yield return ("Overview cancel preserves the captured camera after selection and search", Cancel);
        yield return ("Overview confirms only a still-existing matched selection", Confirm);
        yield return ("Closed overview targets disappear while the projection remains fixed", Closed);
        yield return ("Overview updates current title and bounds without changing the projection", Update);
        yield return ("Overview tab cycling reaches coincident and overlapping windows", Cycle);
        yield return ("Overview selected card wins overlapping map hit tests", Hit);
        yield return ("Overview refits only explicitly or after a real map-size change", Refit);
        yield return ("Overview can remain open with no windows and return to its viewport", Empty);
        yield return ("Overview rejects duplicate and empty target identities", Invalid);
        yield return ("Overview spatial selection handles very distant finite world coordinates", Far);
    }
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static CanvasWindow Window(Guid id, string title, double x, double y, double width = 640, double height = 480) =>
        new(id, title, new(new(x, y), new(width, height)));
    private static CanvasWindow[] Windows() => [Window(A, "Editor", -800, 0), Window(B, "Browser", 0, 0),
        Window(C, "资料", 800, 0), Window(D, "Notes", 0, -700)];
    private static SpatialOverviewSession Session(Guid? selected = null) => new(Windows(), new(new(100, -100), new(1920, 1080)), new(1200, 800), selected ?? B);
    private static void Geometry()
    {
        var model = Session(); var before = model.Windows.ToArray(); var a = model.Projection.ToMap(before[0].Bounds); var b = model.Projection.ToMap(before[1].Bounds);
        Require(Math.Abs(a.Size.Width / a.Size.Height - 640d / 480) < 1e-10 && a.Position.X < b.Position.X
            && Math.Abs(b.Position.X - a.Position.X - 800 * model.Projection.Scale) < 1e-8, "Uniform layout was changed.");
        Require(model.Windows.SequenceEqual(before), "Overview changed world windows.");
    }
    private static void Search()
    {
        var model = Session(); var projection = model.Projection; var before = model.Windows.ToArray();
        model.Search("  资料  "); Require(model.SelectedId == C && model.Matches.Count == 1 && ReferenceEquals(projection, model.Projection)
            && model.Windows.SequenceEqual(before), "Search reflowed or changed projection.");
        model.Search("browser"); Require(model.SelectedId == B, "Case-insensitive title query failed.");
    }
    private static void Directions()
    {
        var model = Session(); Require(model.MoveSelection(OverviewDirection.Left) && model.SelectedId == A, "Left incorrect.");
        model.Select(B); Require(model.MoveSelection(OverviewDirection.Right) && model.SelectedId == C, "Right incorrect.");
        model.Select(B); Require(model.MoveSelection(OverviewDirection.Up) && model.SelectedId == D, "Up incorrect.");
        Require(model.MoveSelection(OverviewDirection.Down) && model.SelectedId == B, "Down incorrect.");
    }
    private static void NoWrap()
    {
        var model = Session(A); Require(!model.MoveSelection(OverviewDirection.Left) && model.SelectedId == A, "Direction wrapped.");
        model.Search("資料"); Require(!model.MoveSelection(OverviewDirection.Right), "No-match direction found a window.");
    }
    private static void EmptySearch()
    {
        var model = Session(); model.Search("not present");
        Require(model.SelectedId is null && model.Confirm() is null && model.Status == OverviewStatus.Open
            && model.HitTest(model.Projection.ToMap(Windows()[0].Bounds.Center)) is null, "Dimmed target remained confirmable.");
    }
    private static void Cancel()
    {
        var model = Session(); var original = model.StartCamera;
        model.MoveSelection(OverviewDirection.Up); model.Search("Editor"); var restored = model.Cancel();
        Require(restored == original && model.Status == OverviewStatus.Cancelled && model.Confirm() is null
            && !model.Select(A), "Cancel changed the camera or allowed later confirmation.");
    }
    private static void Confirm()
    {
        var model = Session(); model.Select(C); var target = model.Confirm();
        Require(target?.Id == C && model.Status == OverviewStatus.Confirmed && !model.MoveSelection(OverviewDirection.Left), "Confirmation was not terminal.");
    }
    private static void Closed()
    {
        var model = Session(C); var projection = model.Projection;
        model.Synchronize(Windows().Where(window => window.Id != C));
        Require(!model.Windows.Any(window => window.Id == C) && model.SelectedId != C && !model.Select(C)
            && ReferenceEquals(projection, model.Projection), "Closed target survived or refit the map.");
        model.Search("资料"); Require(model.Confirm() is null, "Closed target could confirm.");
    }
    private static void Update()
    {
        var model = Session(); var projection = model.Projection;
        model.Search("Editor"); model.Synchronize(Windows().Select(window => window.Id == A ? Window(A, "Renamed", -1200, 40) : window));
        Require(model.Matches.Count == 0 && model.SelectedId is null && model.Windows.Single(window => window.Id == A).Bounds.Position == new PointD(-1200, 40)
            && ReferenceEquals(projection, model.Projection), "Update kept stale query results or reset projection.");
    }
    private static void Cycle()
    {
        var windows = new[] { Window(A, "A", 0, 0), Window(B, "B", 0, 0), Window(C, "C", 0, 0) };
        var model = new SpatialOverviewSession(windows, new(PointD.Zero, new(1920, 1080)), new(1200, 800), A);
        model.Cycle(1); Require(model.SelectedId == B, "Coincident target unreachable.");
        model.Cycle(-1); Require(model.SelectedId == A, "Reverse cycle failed.");
        model.Cycle(-1); Require(model.SelectedId == C, "Cycle did not wrap.");
    }
    private static void Hit()
    {
        var model = new SpatialOverviewSession([Window(A, "A", -100, 0), Window(B, "B", -100, 0)],
            new(PointD.Zero, new(1920, 1080)), new(1200, 800), A);
        Require(model.HitTest(model.Projection.ToMap(new PointD(0, 100))) == A, "Selected overlap was hidden.");
        model.Select(B); Require(model.HitTest(model.Projection.ToMap(new PointD(0, 100))) == B, "Hit selection was stale.");
    }
    private static void Refit()
    {
        var model = Session(); var projection = model.Projection; var selected = model.SelectedId;
        model.Refit(new(1600, 900)); Require(!ReferenceEquals(projection, model.Projection) && model.MapSize == new SizeD(1600, 900)
            && model.SelectedId == selected, "Explicit resize failed or reset selection.");
    }
    private static void Empty()
    {
        var camera = new Camera2D(new(-700, 500), new(1920, 1080));
        var model = new SpatialOverviewSession([], camera, new(1200, 800));
        Require(model.SelectedId is null && model.Confirm() is null && model.Cancel() == camera, "Empty overview lost its viewport.");
    }
    private static void Invalid()
    {
        foreach (var windows in new[] { new[] { Window(Guid.Empty, "bad", 0, 0) }, new[] { Window(A, "A", 0, 0), Window(A, "dup", 20, 20) } })
        {
            try { _ = new SpatialOverviewSession(windows, new(PointD.Zero, new(1920, 1080)), new(1200, 800)); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid overview target accepted.");
        }
    }
    private static void Far()
    {
        var model = new SpatialOverviewSession([Window(A, "A", 0, 0), Window(B, "far", 1e180, 1e180), Window(C, "nearer", 1e160, 1e160)],
            new(PointD.Zero, new(1920, 1080)), new(1200, 800), A);
        Require(model.MoveSelection(OverviewDirection.Right) && model.SelectedId == C, "Far squared-distance overflow selected the wrong neighbor.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
