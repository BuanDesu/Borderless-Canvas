using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class LifecycleChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Incremental observations preserve identity and do not enumerate unrelated windows", Incremental);
        yield return ("Event bursts coalesce to one current read per HWND without full polling", Coalesce);
        yield return ("Destroy plus same-process HWND reuse allocates a fresh identity", Reuse);
        yield return ("Late location and show notifications cannot resurrect a closed window", LateEvents);
        yield return ("Destroy and recreate events are safe regardless of batching order", BatchOrdering);
        yield return ("Failed incremental reads retain pending events for retry", Retry);
        yield return ("Queue overflow reconciles and still retires observed destroyed identities", Overflow);
        yield return ("Explicit desktop-change hints reconcile without an idle polling loop", DesktopChange);
        yield return ("COM reentrancy queues new notifications in the next batch without dropping them", Reentrancy);
        yield return ("Real map subtracts work-area origin and preserves world positions during overview pan", SpatialMap);
        yield return ("Expected movement echoes preserve world positions; external drag updates them", MovementEcho);
        yield return ("Expired or cancelled movement expectations cannot swallow external changes", Expiration);
        yield return ("Closed or excluded windows clear map selection and expectations", ClosedSelection);
    }

    private static RectD Rect(double x, double y) => new(new PointD(x, y), new SizeD(640, 480));
    private static WindowObservation Window(nint handle = 42) => new()
    {
        Handle = handle, ProcessId = 100, ProcessCreationFileTime = 123456789, Title = "fixture", ClassName = "Fixture",
        Bounds = Rect(100, 80), IsAlive = true, MetadataAvailable = true, IsVisible = true,
        IsCloaked = false, IsOnCurrentDesktop = true
    };
    private sealed class Catalog : ITrackedDesktopWindowCatalog, IWindowObservationSource
    {
        internal readonly Dictionary<nint, WindowObservation> Native = [];
        internal readonly WindowInspectionService Inspection;
        internal int Reads;
        internal int Enumerations;
        internal bool Fail;
        internal Action? OnRead;
        internal Catalog() { Native[42] = Window(); Inspection = new(this, new(999)); }
        public IReadOnlyList<WindowObservation> ReadWindows() { Enumerations++; return Native.Values.ToArray(); }
        public WindowCatalogSnapshot Inspect() => Inspection.Inspect();
        public WindowCatalogSnapshot Current => Inspection.Current;
        public IReadOnlyList<DesktopWindowInfo> GetWindows() => Current.AdmittedWindows;
        public void ForgetWindow(nint handle) => Inspection.Forget(handle);
        public void RefreshWindow(nint handle)
        {
            Reads++;
            OnRead?.Invoke();
            if (Fail) throw new InvalidOperationException("Read failed.");
            Inspection.ApplyObservation(Native.GetValueOrDefault(handle) ?? new() { Handle = handle });
        }
    }
    private static void Incremental()
    {
        var catalog = new Catalog(); catalog.Inspect();
        var id = catalog.Current.AdmittedWindows.Single().Id;
        catalog.Native[42] = Window() with { Title = "renamed", Bounds = Rect(-400, 900) };
        catalog.RefreshWindow(42);
        Require(catalog.Current.AdmittedWindows.Single().Id == id && catalog.Enumerations == 1, "Incremental update rescanned or changed ID.");
    }
    private static void Coalesce()
    {
        var catalog = new Catalog(); var buffer = new WindowEventBuffer(); catalog.Inspect();
        for (var i = 0; i < 10000; i++) buffer.Add(new(42, WindowChangeKind.Location));
        buffer.Add(new(42, WindowChangeKind.Foreground));
        Require(buffer.Count == 1 && buffer.ForegroundHandle == 42, "Burst was not coalesced.");
        buffer.Drain(catalog); buffer.Drain(catalog);
        Require(catalog.Reads == 1 && catalog.Enumerations == 1, "Idle or burst triggered redundant reads.");
    }
    private static void Reuse()
    {
        var catalog = new Catalog(); var old = catalog.Inspect().AdmittedWindows.Single().Id;
        var buffer = new WindowEventBuffer(); buffer.Add(new(42, WindowChangeKind.Destroyed)); buffer.Add(new(42, WindowChangeKind.Created));
        var current = buffer.Drain(catalog).AdmittedWindows.Single();
        Require(old != current.Id && current.Identity == Window().Identity, "Same-process reuse inherited old ID.");
    }
    private static void LateEvents()
    {
        var catalog = new Catalog(); catalog.Inspect(); catalog.Native.Clear();
        var buffer = new WindowEventBuffer();
        buffer.Add(new(42, WindowChangeKind.Destroyed)); buffer.Add(new(42, WindowChangeKind.Location)); buffer.Add(new(42, WindowChangeKind.Shown));
        Require(buffer.Drain(catalog).Entries.Count == 0, "Stale event resurrected closed window.");
    }
    private static void BatchOrdering()
    {
        foreach (var kinds in new[] { new[] { WindowChangeKind.Created, WindowChangeKind.Destroyed }, new[] { WindowChangeKind.Destroyed, WindowChangeKind.Created } })
        {
            var catalog = new Catalog(); var id = catalog.Inspect().AdmittedWindows.Single().Id;
            var buffer = new WindowEventBuffer(); foreach (var kind in kinds) buffer.Add(new(42, kind));
            Require(buffer.Drain(catalog).AdmittedWindows.Single().Id != id, "Batch order retained invalid identity.");
        }
    }
    private static void Retry()
    {
        var catalog = new Catalog(); catalog.Inspect(); var buffer = new WindowEventBuffer();
        buffer.Add(new(42, WindowChangeKind.Name)); catalog.Fail = true;
        Throws<InvalidOperationException>(() => buffer.Drain(catalog));
        Require(buffer.Count == 1, "Failure discarded pending notification.");
        catalog.Fail = false; buffer.Drain(catalog); Require(buffer.Count == 0, "Retry failed.");
    }
    private static void Overflow()
    {
        var catalog = new Catalog(); var id = catalog.Inspect().AdmittedWindows.Single().Id;
        var buffer = new WindowEventBuffer();
        for (var i = 100; i < 3000; i++) buffer.Add(new(i, WindowChangeKind.Created));
        buffer.Add(new(42, WindowChangeKind.Destroyed)); // A destroy arrives after capacity is reached.
        var result = buffer.Drain(catalog);
        Require(buffer.Count == 0 && catalog.Enumerations == 2 && result.AdmittedWindows.Single().Id != id, "Overflow lost retirement or did not reconcile.");
    }
    private static void DesktopChange()
    {
        var catalog = new Catalog(); catalog.Inspect(); var buffer = new WindowEventBuffer();
        buffer.Add(new(0, WindowChangeKind.DesktopChanged)); buffer.Drain(catalog); buffer.Drain(catalog);
        Require(catalog.Enumerations == 2 && catalog.Reads == 0, "Desktop change did not reconcile exactly once.");
    }
    private static void Reentrancy()
    {
        var catalog = new Catalog(); catalog.Inspect(); var buffer = new WindowEventBuffer();
        buffer.Add(new(42, WindowChangeKind.Location));
        catalog.OnRead = () => { buffer.Add(new(77, WindowChangeKind.Created)); catalog.OnRead = null; };
        buffer.Drain(catalog);
        Require(buffer.Count == 1 && catalog.Reads == 1, "Reentrant hint was lost or read in the current batch.");
        buffer.Drain(catalog); Require(buffer.Count == 0 && catalog.Reads == 2, "Reentrant hint did not drain.");
    }
    private static (Catalog Catalog, CanvasWorkspace Workspace, DesktopSpatialModel Model) Spatial()
    {
        var catalog = new Catalog(); catalog.Inspect(); var workspace = new CanvasWorkspace(new(1280, 720));
        var model = new DesktopSpatialModel(workspace, new PixelRect(40, 20, 1920, 1080));
        model.Synchronize(catalog.Current, 42, DateTimeOffset.UtcNow);
        return (catalog, workspace, model);
    }
    private static void SpatialMap()
    {
        var (catalog, workspace, model) = Spatial();
        var before = workspace.Windows.Single();
        Require(before.Bounds.Position == new PointD(60, 60), "Physical work-area origin not subtracted.");
        workspace.NavigateTo(new(5000, -3000)); model.Synchronize(catalog.Current, 42, DateTimeOffset.UtcNow);
        Require(workspace.Windows.Single() == before, "Overview panning changed world positions.");
        Require(model.ActiveWindowId == before.Id, "Active window not highlighted.");
    }
    private static void MovementEcho()
    {
        var (catalog, workspace, model) = Spatial(); var before = workspace.Windows.Single(); var now = DateTimeOffset.UtcNow;
        model.ExpectMove(before.Id, Rect(-400, 80), 1, now.AddSeconds(2)); model.PhysicalCameraPosition = new(500, 0);
        catalog.Native[42] = Window() with { Bounds = Rect(-400, 80) }; catalog.RefreshWindow(42);
        model.Synchronize(catalog.Current, 42, now); model.Synchronize(catalog.Current, 42, now);
        Require(workspace.Windows.Single().Bounds == before.Bounds, "Self move or repeated echo rewrote world bounds.");
        catalog.Native[42] = Window() with { Bounds = Rect(-200, 80) }; catalog.RefreshWindow(42); model.Synchronize(catalog.Current, 42, now);
        Require(workspace.Windows.Single().Bounds.Position == new PointD(260, 60), "External drag not reflected in world coordinates.");
    }
    private static void Expiration()
    {
        var (catalog, workspace, model) = Spatial(); var id = workspace.Windows.Single().Id; var now = DateTimeOffset.UtcNow;
        model.ExpectMove(id, Rect(900, 800), 1, now.AddSeconds(-1));
        catalog.Native[42] = Window() with { Bounds = Rect(900, 800) }; catalog.RefreshWindow(42); model.Synchronize(catalog.Current, 42, now);
        Require(workspace.Windows.Single().Bounds.Position == new PointD(860, 780), "Expired move swallowed external update.");
        model.ExpectMove(id, Rect(300, 200), 2, now.AddSeconds(5));
        Throws<ArgumentException>(() => model.ExpectMove(id, Rect(300, 200), 1, now.AddSeconds(5)));
        model.CancelExpectedMove(id); catalog.Native[42] = Window() with { Bounds = Rect(300, 200) }; catalog.RefreshWindow(42);
        model.Synchronize(catalog.Current, 42, now); Require(workspace.Windows.Single().Bounds.Position == new PointD(260, 180), "Cancelled expectation swallowed drag.");
    }
    private static void ClosedSelection()
    {
        var (catalog, workspace, model) = Spatial(); var id = workspace.Windows.Single().Id;
        model.ExpectMove(id, Rect(100, 80), 1, DateTimeOffset.UtcNow.AddSeconds(1));
        catalog.Native[42] = Window() with { IsMinimized = true }; catalog.RefreshWindow(42);
        model.Synchronize(catalog.Current, 42, DateTimeOffset.UtcNow);
        Require(workspace.Windows.Count == 0 && model.ActiveWindowId is null, "Excluded target stayed on map.");
        Throws<ArgumentOutOfRangeException>(() => model.WorkArea = default);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
