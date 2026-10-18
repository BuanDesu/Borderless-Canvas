using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Geometry;

internal static class PreviewChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Overview preview resources survive selection and geometry changes without duplicate registration", Reuse);
        yield return ("Overview closes all native resources and dispose is idempotent", Close);
        yield return ("Search exclusion and closed targets release previews without removing map cards", Remove);
        yield return ("Disabling previews releases resources and permits a fresh enable", Disable);
        yield return ("Preview registration failure is isolated, cached and explicitly retryable", RegisterFailure);
        yield return ("Preview update failure releases the failing source and leaves other cards usable", UpdateFailure);
        yield return ("Invalid preview dimensions release resources and keep a title fallback", SizeFailure);
        yield return ("A lost native identity releases its preview before update", IdentityLost);
        yield return ("A replaced process identity never reuses an old preview registration", Replace);
        yield return ("Offscreen, overlapping and tiny cards do not consume preview resources", Fallbacks);
        yield return ("Overview resource limits prefer the selected window and retain every map card", Capacity);
        yield return ("Preview aspect fitting stays inside translated destination client pixels", Geometry);
        yield return ("Preview failure cache disappears with its target or replaced identity", FailureRemoval);
        yield return ("Invalid preview input cannot partially release the current resources", Invalid);
        yield return ("Closing during native registration releases the late resource without reviving overview", CloseDuringCreate);
        yield return ("Reentrant native callbacks cannot recursively acquire previews", Reentry);
    }
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly NativeWindowIdentity IdentityA = new(100, 200, 300), IdentityB = new(101, 201, 301);
    private static PreviewCard Card(Guid id, NativeWindowIdentity identity, PreviewState? fallback = null) => new(id, identity, new(100, 200, 500, 500), fallback);
    private sealed class Preview : IOverviewPreview
    {
        public bool IsCurrent { get; set; } = true;
        public SizeD? SourceSize { get; set; } = new(640, 480);
        public bool Succeed = true;
        public int Disposals, Updates;
        public PixelRect? Last;
        public bool Update(PixelRect bounds) { Updates++; Last = bounds; return Succeed; }
        public void Dispose() => Disposals++;
    }
    private sealed class Port : IOverviewPreviewPort
    {
        public int Calls;
        public bool Reject;
        public Action? DuringCreate;
        public readonly List<Preview> Created = [];
        public IOverviewPreview? Create(NativeWindowIdentity identity)
        { Calls++; DuringCreate?.Invoke(); if (Reject) return null; var preview = new Preview(); Created.Add(preview); return preview; }
    }
    private static void Reuse()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true);
        model.Synchronize([Card(A, IdentityA) with { ContentBounds = new(200, 300, 600, 700) }], null, true);
        Require(port.Calls == 1 && port.Created.Single().Updates == 2 && port.Created[0].Last!.Value.Top >= 300, "Registration was recreated or bounds were stale.");
    }
    private static void Close()
    {
        var port = new Port(); var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA), Card(B, IdentityB)], A, true); model.Dispose(); model.Dispose();
        Require(model.ResourceCount == 0 && port.Created.All(resource => resource.Disposals == 1), "Resource leak or duplicate dispose.");
    }
    private static void Remove()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA), Card(B, IdentityB)], A, true);
        model.Synchronize([Card(A, IdentityA, PreviewState.Unmatched)], null, true);
        Require(model.ResourceCount == 0 && model.States.Count == 1 && model.States[A] == PreviewState.Unmatched && port.Created.All(p => p.Disposals == 1), "Excluded or closed resource remained.");
    }
    private static void Disable()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port); var cards = new[] { Card(A, IdentityA) };
        model.Synchronize(cards, A, true); model.Synchronize(cards, A, false);
        Require(model.States[A] == PreviewState.Disabled && port.Created[0].Disposals == 1, "Disabled preview survived.");
        model.Synchronize(cards, A, true); Require(port.Calls == 2 && model.States[A] == PreviewState.Preview, "Enable could not recreate resource.");
    }
    private static void RegisterFailure()
    {
        var port = new Port { Reject = true }; using var model = new OverviewPreviewSession(port); var cards = new[] { Card(A, IdentityA) };
        model.Synchronize(cards, A, true); model.Synchronize(cards, A, true);
        Require(port.Calls == 1 && model.States[A] == PreviewState.Unavailable, "Failure retried every update.");
        port.Reject = false; model.RetryFailed(); model.Synchronize(cards, A, true);
        Require(port.Calls == 2 && model.States[A] == PreviewState.Preview, "Explicit retry failed.");
    }
    private static void UpdateFailure()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port); var cards = new[] { Card(A, IdentityA), Card(B, IdentityB) };
        model.Synchronize(cards, A, true); port.Created[0].Succeed = false; model.Synchronize(cards, A, true);
        Require(model.States[A] == PreviewState.Unavailable && model.States[B] == PreviewState.Preview && port.Created[0].Disposals == 1, "One failed source blocked others.");
    }
    private static void SizeFailure()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port); var cards = new[] { Card(A, IdentityA) };
        model.Synchronize(cards, A, true); port.Created[0].SourceSize = default(SizeD); model.Synchronize(cards, A, true);
        Require(model.States[A] == PreviewState.Unavailable && port.Created[0].Disposals == 1, "Invalid dimensions remained active.");
    }
    private static void IdentityLost()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port); var cards = new[] { Card(A, IdentityA) };
        model.Synchronize(cards, A, true); port.Created[0].IsCurrent = false; model.Synchronize(cards, A, true);
        Require(port.Created[0].Updates == 1 && port.Created[0].Disposals == 1 && model.ResourceCount == 0, "Stale identity was updated.");
    }
    private static void Replace()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true);
        model.Synchronize([Card(A, IdentityA with { ProcessCreationFileTime = 999 })], A, true);
        Require(port.Created[0].Disposals == 1 && port.Calls == 2 && model.ResourceCount == 1, "Old process resource reused.");
    }
    private static void Fallbacks()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        foreach (var reason in new[] { PreviewState.Offscreen, PreviewState.Overlap, PreviewState.TooSmall })
        {
            model.Synchronize([Card(A, IdentityA, reason)], A, true);
            Require(model.States[A] == reason && model.States.Count == 1, "Fallback removed its card.");
        }
        Require(port.Calls == 0, "Fallback sources registered thumbnails.");
    }
    private static void Capacity()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port, 1); var cards = new[] { Card(A, IdentityA), Card(B, IdentityB) };
        model.Synchronize(cards, A, true); model.Synchronize(cards, B, true);
        Require(model.ResourceCount == 1 && model.States.Count == 2 && model.States[B] == PreviewState.Preview
            && model.States[A] == PreviewState.Capacity && port.Created[0].Disposals == 1, "Selection did not receive bounded resources.");
    }
    private static void Geometry()
    {
        var frame = new PixelRect(100, 200, 500, 500); var fit = OverviewPreviewSession.Fit(new(1600, 900), frame);
        Require(frame.Contains(fit) && fit.Width == 400 && fit.Height == 225 && fit.Top == 237, "Preview was stretched, clipped or offset into root coordinates incorrectly.");
    }
    private static void FailureRemoval()
    {
        var port = new Port { Reject = true }; using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true); model.Synchronize([], null, true); port.Reject = false;
        model.Synchronize([Card(A, IdentityA)], A, true); Require(port.Calls == 2, "Closed source retained failure cache.");
    }
    private static void Invalid()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true);
        try { model.Synchronize([Card(A, IdentityA), Card(A, IdentityB)], A, true); throw new Exception("Duplicate accepted."); }
        catch (ArgumentException) { }
        Require(model.ResourceCount == 1 && port.Created[0].Disposals == 0, "Validation changed existing resources.");
    }
    private static void CloseDuringCreate()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port); port.DuringCreate = model.Dispose;
        model.Synchronize([Card(A, IdentityA)], A, true);
        Require(model.ResourceCount == 0 && model.States.Count == 0 && port.Created.Single().Disposals == 1,
            "Late registration survived closure.");
    }
    private static void Reentry()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        port.DuringCreate = () => model.Synchronize([Card(B, IdentityB)], B, true);
        model.Synchronize([Card(A, IdentityA)], A, true);
        Require(port.Calls == 1 && model.ResourceCount == 1 && model.States.ContainsKey(A) && !model.States.ContainsKey(B),
            "Native callback recursively modified preview resources.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
