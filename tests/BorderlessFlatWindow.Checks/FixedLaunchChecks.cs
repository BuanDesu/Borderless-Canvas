using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Core.Geometry;

internal static class FixedLaunchChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Shortcut launch matching never claims an existing or unrelated executable window", Match);
        yield return ("Shortcut launch matching waits for stable geometry and holds its candidate out of automatic admission", Stability);
        yield return ("Shortcut launch requests are bounded, cancellable and expire without assigning another window", Lifetime);
    }
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static InspectedWindow Window(nint handle, int x = 0) => new(Guid.NewGuid(), new WindowObservation
    { Handle = handle, ProcessId = (uint)(100 + handle), ProcessCreationFileTime = 12345, IsAlive = true, MetadataAvailable = true,
        IsVisible = true, IsCloaked = false, Title = "Synthetic", ClassName = "Fixture", Bounds = new RectD(new(x, 0), new(300, 200)), IsOnCurrentDesktop = true },
        new(WindowExclusionReason.None));
    private static WindowCatalogSnapshot Snapshot(params InspectedWindow[] windows) => new(Now, windows);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Match()
    {
        var tracker = new FixedLaunchTracker(); var old = Window(10); var fresh = Window(11); var other = Window(12); var snapshot = Snapshot(old, fresh, other);
        tracker.Begin("fixture.exe", Snapshot(old), Now);
        string Path(NativeWindowIdentity identity) => identity.Handle == other.Observation.Handle ? "unrelated.exe" : "fixture.exe";
        Require(tracker.Observe(snapshot, Now, Path, new HashSet<NativeWindowIdentity>()).Count == 0, "Unstable window was assigned.");
        var result = tracker.Observe(snapshot, Now.AddMilliseconds(220), Path, new HashSet<NativeWindowIdentity>());
        Require(result.SequenceEqual(new[] { fresh.Observation.Identity!.Value }) && tracker.Count == 0, "Existing/unrelated window was assigned instead of the new matching instance.");
    }
    private static void Stability()
    {
        var tracker = new FixedLaunchTracker(); var window = Window(20); var identity = window.Observation.Identity!.Value;
        tracker.Begin("fixture.exe", Snapshot(), Now); tracker.Observe(Snapshot(window), Now, _ => "fixture.exe", new HashSet<NativeWindowIdentity>());
        Require(tracker.HeldIdentities.Contains(identity), "Candidate could enter the canvas before fixed-launch matching.");
        var moved = window with { Observation = window.Observation with { Bounds = new(new PointD(20, 10), new SizeD(300, 200)) } };
        tracker.Observe(Snapshot(moved), Now.AddMilliseconds(150), _ => "fixture.exe", new HashSet<NativeWindowIdentity>());
        Require(tracker.Observe(Snapshot(moved), Now.AddMilliseconds(250), _ => "fixture.exe", new HashSet<NativeWindowIdentity>()).Count == 0, "Geometry changes did not reset stability.");
        Require(tracker.Observe(Snapshot(moved), Now.AddMilliseconds(400), _ => "fixture.exe", new HashSet<NativeWindowIdentity>()).Single() == identity && tracker.HeldIdentities.Count == 0, "Stable candidate did not leave the hold set.");
    }
    private static void Lifetime()
    {
        var tracker = new FixedLaunchTracker(); var tokens = new List<Guid>();
        for (var i = 0; i < 8; i++) tokens.Add(tracker.Begin("fixture.exe", Snapshot(), Now));
        try { tracker.Begin("fixture.exe", Snapshot(), Now); throw new Exception("Launch request limit ignored."); } catch (InvalidOperationException) { }
        tracker.Cancel(tokens[0]); Require(tracker.Count == 7, "Cancelled launch remained active.");
        var results = tracker.Observe(Snapshot(Window(30)), Now.AddSeconds(31), _ => "fixture.exe", new HashSet<NativeWindowIdentity>());
        Require(results.Count == 0 && tracker.Count == 0 && tracker.ExpiredLastObservation == 7 && tracker.HeldIdentities.Count == 0, "Expired launch assigned a window or retained candidate state.");
    }
}
