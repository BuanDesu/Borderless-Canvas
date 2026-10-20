using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class OverviewPreparationChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Prepared overview snapshots compare contents and copy mutable input arrays", Snapshot);
        yield return ("Prepared overview identity rejects changed session, world, displays, bookmarks and appearance", Changes);
        yield return ("Taking a matching prepared overview transfers ownership exactly once", Transfer);
        yield return ("Late preparation cannot revive a taken, invalidated or disposed slot", Late);
        yield return ("Prepared overview invalidation does not erase a resource published during disposal", Reentry);
        yield return ("Reentrant disposal cannot give an old preparation the newer producer ticket", ReentrantBegin);
    }
    private static readonly object Owner = new();
    private static readonly Camera2D Camera = new(PointD.Zero, new(1920, 1080));
    private static readonly CanvasWindow Window = new(Guid.NewGuid(), "Fixture", new(PointD.Zero, new(640, 480)));
    private static readonly DisplayMonitor Monitor = new("Fixture", true, new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96);
    private static readonly LocationBookmark Bookmark = new(Guid.NewGuid(), "Fixture", PointD.Zero);
    private static OverviewPreparationSnapshot Key(object? owner = null, Camera2D? camera = null, CanvasWindow? window = null,
        DisplayMonitor? monitor = null, LocationBookmark? bookmark = null, bool writable = true, string state = "ready", int color = 10, bool animate = true, bool taskbar = false)
        => new(owner ?? Owner, camera ?? Camera, [window ?? Window], [monitor ?? Monitor], [bookmark ?? Bookmark], writable, state, [color], animate, taskbar);
    private static void Snapshot()
    {
        var windows = new[] { Window }; var monitors = new[] { Monitor }; var bookmarks = new[] { Bookmark }; var colors = new[] { 10 };
        var snapshot = new OverviewPreparationSnapshot(Owner, Camera, windows, monitors, bookmarks, true, "ready", colors, true, false);
        Require(snapshot.Matches(Key()), "Equal independently created arrays missed.");
        windows[0] = Window with { Title = "Changed" }; monitors[0] = new("Other", true, Monitor.Bounds, Monitor.WorkArea, 96);
        bookmarks[0] = Bookmark with { Name = "Changed" }; colors[0] = 20;
        Require(snapshot.Matches(Key()), "Caller array mutation changed an existing snapshot.");
    }
    private static void Changes()
    {
        var original = Key();
        OverviewPreparationSnapshot[] changed = [Key(owner: new()), Key(camera: Camera.At(new(20, 0))),
            Key(camera: new(PointD.Zero, new(1600, 900))), Key(window: Window with { Title = "New" }),
            Key(window: Window with { Bounds = new(Window.Bounds.Position + new PointD(10, 0), Window.Bounds.Size) }), Key(window: Window with { Id = Guid.NewGuid() }),
            Key(monitor: new("Fixture", true, Monitor.Bounds, Monitor.WorkArea, 144)),
            Key(monitor: new("Fixture", true, Monitor.Bounds, new(0, 0, 1920, 1000), 96)),
            Key(bookmark: Bookmark with { CameraPosition = new(800, 0) }), Key(bookmark: Bookmark with { Name = "New" }),
            Key(writable: false), Key(state: "readonly"), Key(color: 20), Key(animate: false), Key(taskbar: true)];
        Require(changed.All(k => !original.Matches(k)), "A stale model or appearance input was reused.");
    }
    private sealed class Resource : IDisposable
    { internal int Disposals; internal Action? OnDispose; public void Dispose() { Disposals++; OnDispose?.Invoke(); } }
    private static void Transfer()
    {
        using var slot = new OverviewPreparationSlot<Resource>(); var resource = new Resource();
        Require(slot.Publish(slot.Begin(), Key(), resource) && slot.Contains(Key()), "Preparation was not published.");
        Require(ReferenceEquals(slot.Take(Key()), resource) && slot.Take(Key()) is null, "Take did not transfer exactly once.");
        slot.Dispose(); Require(resource.Disposals == 0, "Slot disposed the already transferred resource."); resource.Dispose();
    }
    private static void Late()
    {
        var slot = new OverviewPreparationSlot<Resource>(); var ticket = slot.Begin(); slot.Invalidate(); var late = new Resource();
        Require(!slot.Publish(ticket, Key(), late) && late.Disposals == 1, "Late publication survived invalidation.");
        ticket = slot.Begin(); slot.Take(Key()); late = new(); Require(!slot.Publish(ticket, Key(), late) && late.Disposals == 1, "Take did not cancel its producer.");
        var stale = new Resource(); slot.Publish(slot.Begin(), Key(), stale); Require(slot.Take(Key(color: 20)) is null && stale.Disposals == 1, "Stale appearance was consumed.");
        ticket = slot.Begin(); slot.Dispose(); late = new(); Require(!slot.Publish(ticket, Key(), late) && late.Disposals == 1, "Disposal revived a prepared host.");
    }
    private static void Reentry()
    {
        using var slot = new OverviewPreparationSlot<Resource>(); var replacement = new Resource();
        var previous = new Resource { OnDispose = () => slot.Publish(slot.Begin(), Key(color: 20), replacement) };
        slot.Publish(slot.Begin(), Key(), previous); slot.Invalidate();
        Require(previous.Disposals == 1 && slot.Contains(Key(color: 20)) && replacement.Disposals == 0, "Old cleanup erased a newer prepared host.");
    }
    private static void ReentrantBegin()
    {
        using var slot = new OverviewPreparationSlot<Resource>(); long newerTicket = 0;
        var previous = new Resource { OnDispose = () => newerTicket = slot.Begin() };
        slot.Publish(slot.Begin(), Key(), previous);
        var oldTicket = slot.Begin(); var stale = new Resource(); var fresh = new Resource();
        Require(oldTicket < newerTicket && !slot.Publish(oldTicket, Key(), stale) && stale.Disposals == 1, "Old producer borrowed a reentrant generation.");
        Require(slot.Publish(newerTicket, Key(color: 20), fresh), "Fresh producer was invalidated by old cleanup.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
