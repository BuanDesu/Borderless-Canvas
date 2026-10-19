using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class RuntimeAdmissionChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("New-window placement prefers a complete fit in the current viewport", Empty);
        yield return ("New-window placement finds a narrow existing hole without moving its neighbors", Hole);
        yield return ("A full viewport places new windows nearby without shrinking or rearranging existing windows", Full);
        yield return ("Repeated late windows receive distinct empty world regions", Repeated);
        yield return ("Empty-space placement follows negative and very distant camera coordinates", Distant);
        yield return ("Oversized late windows keep their native size and remain reachable", Oversized);
        yield return ("New-window placement rejects invalid geometry and is deterministic across obstacle order", Geometry);
        yield return ("Rectangle spacing accepts an exact gap and rejects a one-pixel intrusion", Gap);
        yield return ("Auto admission excludes the entire pre-existing desktop including unselected windows", Baseline);
        yield return ("Auto admission waits for new window bounds and titles to settle", Stable);
        yield return ("Auto admission removes hidden or closed pending candidates and never re-adds managed IDs", Retire);
        yield return ("Auto admission still applies owner, desktop, visibility and special-state exclusions", Unsupported);
        yield return ("Identity-table resets cannot make an old native window appear newly opened", IdentityReset);
        yield return ("A destroyed handle or changed process identity can represent a genuinely new auto candidate", Reuse);
        yield return ("An unknown pre-existing identity remains excluded until a real destroy hint", Unknown);
    }
    private static RectD Rect(double x, double y, double w, double h) => new(new(x, y), new(w, h));
    private static readonly RectD View = Rect(0, 0, 1920, 1040);
    private static void Empty()
    {
        Require(NearbyWindowPlacement.TryPlace(new(640, 480), View, [], out var placed)
            && NearbyWindowPlacement.FitsInside(placed, View) && placed.Size == new SizeD(640, 480), "New window did not fit its viewport.");
    }
    private static void Hole()
    {
        var view = Rect(0, 0, 1000, 600); var occupied = new[] { Rect(0, 0, 400, 600), Rect(650, 0, 350, 600) }; var copy = occupied.ToArray();
        Require(NearbyWindowPlacement.TryPlace(new(200, 160), view, occupied, out var placed) && NearbyWindowPlacement.FitsInside(placed, view)
            && occupied.All(rect => !NearbyWindowPlacement.OverlapsWithGap(rect, placed)) && occupied.SequenceEqual(copy), "Existing hole was missed or neighbors changed.");
    }
    private static void Full()
    {
        Require(NearbyWindowPlacement.TryPlace(new(640, 480), View, [View], out var placed) && !NearbyWindowPlacement.FitsInside(placed, View)
            && !NearbyWindowPlacement.OverlapsWithGap(placed, View) && Math.Abs(placed.Center.Y - View.Center.Y) <= View.Size.Height + 480,
            "Full viewport compressed windows or failed to find nearby space.");
    }
    private static void Repeated()
    {
        var occupied = new List<RectD>();
        for (var i = 0; i < 32; i++)
        {
            Require(NearbyWindowPlacement.TryPlace(new(320, 220), View, occupied, out var placed)
                && occupied.All(rect => !NearbyWindowPlacement.OverlapsWithGap(rect, placed)), "Late windows overlap or placement failed.");
            occupied.Add(placed);
        }
    }
    private static void Distant()
    {
        foreach (var point in new[] { new PointD(-4000, -3000), new PointD(1e12, -1e12) })
        {
            var view = new RectD(point, View.Size);
            Require(NearbyWindowPlacement.TryPlace(new(640, 480), view, [new(point, new(700, 500))], out var placed)
                && NearbyWindowPlacement.FitsInside(placed, view), "Placement used physical origin instead of camera.");
            var planner = new WindowPlacementPlanner(new(0, 0, 1920, 1040), [new(0, 0, 1920, 1080)]);
            Require(planner.TryPlan(placed, point, out var native) && native is { IsParked: false }, "Distant world placement could not project safely.");
        }
    }
    private static void Oversized()
    {
        var size = new SizeD(2400, 700);
        Require(NearbyWindowPlacement.TryPlace(size, View, [Rect(300, 200, 500, 400)], out var placed)
            && placed.Size == size && !NearbyWindowPlacement.OverlapsWithGap(placed, Rect(300, 200, 500, 400)), "Oversized window was resized or overlapped.");
    }
    private static void Geometry()
    {
        Require(!NearbyWindowPlacement.TryPlace(default, View, [], out _) && !NearbyWindowPlacement.TryPlace(new(100, 100), default, [], out _)
            && !NearbyWindowPlacement.TryPlace(new(100, 100), View, [default], out _), "Invalid bounds accepted.");
        var rects = new[] { Rect(0, 0, 700, 800), Rect(900, 100, 700, 700) };
        Require(NearbyWindowPlacement.TryPlace(new(200, 200), View, rects, out var a) && NearbyWindowPlacement.TryPlace(new(200, 200), View, rects.Reverse(), out var b)
            && a == b, "Obstacle enumeration changed the chosen position.");
    }
    private static void Gap() => Require(!NearbyWindowPlacement.OverlapsWithGap(Rect(0, 0, 100, 100), Rect(116, 0, 100, 100))
        && NearbyWindowPlacement.OverlapsWithGap(Rect(0, 0, 100, 100), Rect(115, 0, 100, 100)), "Gap edges were inconsistent.");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00+08:00");
    private sealed class Desktop : IWindowObservationSource
    {
        internal readonly Dictionary<nint, WindowObservation> Native = [];
        internal readonly WindowInspectionService Catalog;
        internal readonly RuntimeAdmissionTracker Tracker = new();
        internal readonly DisplayLayout Layout = new([new("primary", true, new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96)]);
        internal Desktop() { Catalog = new(this, new(999)); Native[42] = Window(42); Tracker.Reset(Catalog.Inspect()); }
        public IReadOnlyList<WindowObservation> ReadWindows() => Native.Values.ToArray();
        internal void Tick(int ms = 0, IReadOnlySet<Guid>? managed = null) => Tracker.Observe(Catalog.Inspect(), managed ?? new HashSet<Guid>(), Layout, Now.AddMilliseconds(ms));
        internal static WindowObservation Window(nint handle) => new()
        {
            Handle = handle, ProcessId = (uint)(100 + handle), ProcessCreationFileTime = 123456789,
            Title = "New fixture", ClassName = "Fixture", Bounds = Rect(100, 100, 440, 310), IsAlive = true, MetadataAvailable = true,
            IsVisible = true, IsCloaked = false, IsOnCurrentDesktop = true
        };
    }
    private static void Baseline()
    { var d = new Desktop(); d.Tick(); Require(d.Tracker.PendingHandles.Count == 0 && d.Tracker.Ready(Now.AddSeconds(1)).Count == 0, "Old desktop was auto admitted."); }
    private static void Stable()
    {
        var d = new Desktop(); d.Native[43] = Desktop.Window(43); d.Tick(); Require(d.Tracker.Ready(Now.AddMilliseconds(100)).Count == 0, "Window admitted before settling.");
        d.Native[43] = d.Native[43] with { Bounds = Rect(150, 100, 440, 310), Title = "Settled" }; d.Tick(150);
        Require(d.Tracker.Ready(Now.AddMilliseconds(300)).Count == 0, "Initialization change did not reset stability."); d.Tick(400);
        Require(d.Tracker.Ready(Now.AddMilliseconds(400)).Single().Observation.Handle == 43, "Stable window never became ready.");
    }
    private static void Retire()
    {
        var d = new Desktop(); d.Native[43] = Desktop.Window(43); d.Tick(); var id = d.Catalog.Current.Entries.Single(entry => entry.Observation.Handle == 43).Id!.Value;
        d.Tick(300, new HashSet<Guid> { id }); Require(d.Tracker.PendingHandles.Count == 0, "Managed window stayed pending.");
        d.Tick(400); d.Native[43] = d.Native[43] with { IsVisible = false }; d.Tick(700); Require(d.Tracker.PendingHandles.Count == 0, "Hidden candidate remained pending.");
        d.Native.Remove(43); d.Tick(900); Require(d.Tracker.PendingHandles.Count == 0, "Closed candidate survived.");
    }
    private static void Unsupported()
    {
        foreach (var edit in new Func<WindowObservation, WindowObservation>[] { w => w with { OwnerHandle = 42 }, w => w with { IsToolWindow = true },
            w => w with { IsOnCurrentDesktop = false }, w => w with { IsCloaked = true }, w => w with { IsMinimized = true },
            w => w with { IsMaximized = true }, w => w with { IsTopMost = true }, w => w with { MetadataAvailable = false } })
        { var d = new Desktop(); d.Native[43] = edit(Desktop.Window(43)); d.Tick(); Require(d.Tracker.PendingHandles.Count == 0, "Unsupported new window became eligible."); }
    }
    private static void IdentityReset()
    { var d = new Desktop(); d.Catalog.Forget(42); d.Tick(); Require(d.Tracker.PendingHandles.Count == 0, "GUID reset authorized an old window."); }
    private static void Reuse()
    {
        var d = new Desktop(); d.Native[42] = d.Native[42] with { ProcessCreationFileTime = 999999 }; d.Tick();
        Require(d.Tracker.PendingHandles.Count == 1, "Changed process could not represent a new window.");
        d = new Desktop(); d.Tracker.ForgetDestroyed(42); d.Catalog.Forget(42); d.Tick();
        Require(d.Tracker.PendingHandles.Count == 1, "Verified destroyed handle could not be reused.");
    }
    private static void Unknown()
    {
        var d = new Desktop(); d.Native[42] = d.Native[42] with { ProcessCreationFileTime = null }; d.Tracker.Reset(d.Catalog.Inspect());
        d.Native[42] = Desktop.Window(42); d.Tick(); Require(d.Tracker.PendingHandles.Count == 0, "Unverified baseline became new without evidence.");
        d.Tracker.ForgetDestroyed(42); d.Tick(300); Require(d.Tracker.PendingHandles.Count == 1, "Destroyed unknown baseline never became eligible.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
