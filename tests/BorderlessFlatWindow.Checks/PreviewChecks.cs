using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Core.Canvas;

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
        yield return ("Explicit fallback cards retain their reason and do not acquire native resources", Fallbacks);
        yield return ("Overview resource limits prefer the selected window and retain every map card", Capacity);
        yield return ("Preview aspect fitting stays inside translated destination client pixels", Geometry);
        yield return ("Preview failure cache disappears with its target or replaced identity", FailureRemoval);
        yield return ("Invalid preview input cannot partially release the current resources", Invalid);
        yield return ("Closing during native registration releases the late resource without reviving overview", CloseDuringCreate);
        yield return ("Reentrant native callbacks cannot recursively acquire previews", Reentry);
        yield return ("Unchanged preview commands do not cross the native boundary again", Unchanged);
        yield return ("Default overview previews include more than sixteen windows", AllWindows);
        yield return ("Viewport preview crops partial windows and keeps negative camera coordinates", ViewportCrop);
        yield return ("Viewport preview preserves letterboxing, excludes outside windows and drops subpixel slices", ViewportEdges);
        yield return ("Animation reuses validated sources without registration or size queries", Animation);
        yield return ("Workspace source slices exclude monitor gaps and taskbar areas without changing scale", MonitorSlices);
        yield return ("Animation callbacks cannot reconcile registrations recursively", AnimationReentry);
        yield return ("Preview batches cover a full synchronization and never enter animation", Batches);
        yield return ("Preview batches release on acquisition failures and reentrant closure", BatchFailures);
    }
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly NativeWindowIdentity IdentityA = new(100, 200, 300), IdentityB = new(101, 201, 301);
    private static PreviewCard Card(Guid id, NativeWindowIdentity identity, PreviewState? fallback = null) => new(id, identity, new(100, 200, 500, 500), fallback);
    private sealed class Preview : IOverviewPreview
    {
        public bool IsCurrent { get; set; } = true;
        private SizeD? size = new(640, 480);
        public int SizeQueries;
        public SizeD? SourceSize { get { SizeQueries++; return size; } set => size = value; }
        public bool Succeed = true;
        public int Disposals, Updates;
        public PixelRect? Last;
        public Action? DuringUpdate;
        public bool Update(PixelRect bounds) { Updates++; Last = bounds; DuringUpdate?.Invoke(); return Succeed; }
        public bool Update(PixelRect bounds, RectD? crop, byte opacity) => Update(bounds);
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
        model.Synchronize(cards, A, true); port.Created[0].Succeed = false; model.Synchronize(cards.Select(c => c with { ContentBounds = new(20, 20, 400, 400) }), A, true);
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
    private static void Unchanged()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        for (var i = 0; i < 100; i++) model.Synchronize([Card(A, IdentityA)], A, true);
        Require(port.Calls == 1 && port.Created.Single().Updates == 1, "Idle polling resent native updates.");
    }
    private static void AllWindows()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize(Enumerable.Range(0, 48).Select(_ => Card(Guid.NewGuid(), IdentityA)), null, true);
        Require(model.ResourceCount == 48 && model.States.Values.All(s => s == PreviewState.Preview), "An implicit sixteen-window limit remains.");
    }
    private static void ViewportCrop()
    {
        var window = new RectD(new(-1200, -500), new(800, 600));
        var slice = ViewportPreview.Project(window, new(new(-1000, -400), new(1600, 900)), new(100, 200, 420, 380))!;
        Require(slice.Destination == new PixelRect(100, 200, 220, 300) && slice.SourceFraction == new RectD(new(.25, 1d / 6), new(.75, 5d / 6)), "Partial window was stretched or its hidden pixels leaked outside the viewport.");
    }
    private static void ViewportEdges()
    {
        var viewport = new RectD(new(1_000_000, -1_000_000), new(1600, 900));
        var frame = new PixelRect(0, 0, 200, 200);
        var slice = ViewportPreview.Project(viewport, viewport, frame)!;
        Require(slice.Destination == new PixelRect(0, 44, 200, 156) && slice.SourceFraction == new RectD(PointD.Zero, new(1, 1)), "Workspace viewport aspect changed.");
        Require(ViewportPreview.Project(new(new(0, 0), new(100, 100)), viewport, frame) is null, "An outside window was included.");
        Require(ViewportPreview.Project(new(viewport.Position, new(.001, .001)), viewport, frame) is null, "Subpixel crop acquired a resource.");
    }
    private static void Animation()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true); var queries = port.Created[0].SizeQueries;
        for (var i = 0; i < 60; i++) model.Animate([Card(A, IdentityA) with { ContentBounds = new(i, i, i + 100, i + 80) }], (byte)(i * 4));
        Require(port.Calls == 1 && port.Created[0].SizeQueries == queries && port.Created[0].Updates == 61, "Animation entered discovery/registration or source size paths.");
    }
    private static void MonitorSlices()
    {
        var viewport = new RectD(new(-400, 200), new(2000, 1000));
        var frame = new PixelRect(10, 20, 410, 220);
        var window = new RectD(new(400, 800), new(600, 400));
        var left = ViewportPreview.Project(window, viewport, frame, new(new(-400, 200), new(1000, 900)))!;
        var right = ViewportPreview.Project(window, viewport, frame, new(new(600, 200), new(1000, 700)))!;
        Require(left.Destination == new PixelRect(170, 140, 210, 200) && right.Destination == new PixelRect(210, 140, 290, 160), "Monitor height or taskbar exclusion changed projection.");
        Require(left.SourceFraction == new RectD(PointD.Zero, new(1d / 3, .75)) && right.SourceFraction == new RectD(new(1d / 3, 0), new(2d / 3, .25)), "Monitor slices used the wrong source pixels.");
    }
    private static void AnimationReentry()
    {
        var port = new Port(); using var model = new OverviewPreviewSession(port);
        model.Synchronize([Card(A, IdentityA)], A, true);
        port.Created[0].DuringUpdate = () => model.Synchronize([Card(B, IdentityB)], B, true);
        model.Animate([Card(A, IdentityA) with { ContentBounds = new(20, 20, 300, 300) }]);
        Require(port.Calls == 1 && model.ResourceCount == 1 && model.States.ContainsKey(A), "Animation recursively replaced native resources.");
    }
    private sealed class BatchPort : IOverviewPreviewPort
    {
        internal int Begins, Ends, Active, Creates;
        internal Action? DuringBegin, DuringCreate;
        public IDisposable BeginBatch() { Begins++; Active++; DuringBegin?.Invoke(); return new Scope(this); }
        public IOverviewPreview? Create(NativeWindowIdentity identity)
        {
            Require(Active == 1, "Acquisition escaped its synchronous query scope."); Creates++; DuringCreate?.Invoke(); return new Preview();
        }
        private sealed class Scope(BatchPort owner) : IDisposable
        { public void Dispose() { owner.Active--; owner.Ends++; } }
    }
    private static void Batches()
    {
        var port = new BatchPort(); using var model = new OverviewPreviewSession(port);
        var cards = new[] { Card(A, IdentityA), Card(B, IdentityB) };
        model.Synchronize(cards, A, true); model.Animate(cards, 120);
        Require(port.Begins == 1 && port.Ends == 1 && port.Active == 0 && port.Creates == 2, "Batch or animation lifetime changed.");
        try { model.Synchronize([cards[0], cards[0]], A, true); } catch (ArgumentException) { }
        Require(port.Begins == 1 && model.ResourceCount == 2, "Invalid input opened a batch or changed live resources.");
    }
    private static void BatchFailures()
    {
        var port = new BatchPort(); using var model = new OverviewPreviewSession(port);
        port.DuringCreate = () => throw new IOException("Fixture acquisition failure");
        try { model.Synchronize([Card(A, IdentityA)], A, true); throw new InvalidOperationException("Failure swallowed."); } catch (IOException) { }
        Require(port.Ends == 1 && port.Active == 0 && model.ResourceCount == 0, "Failure leaked its query service.");
        port.DuringCreate = null; port.DuringBegin = model.Dispose;
        model.Synchronize([Card(A, IdentityA)], A, true);
        Require(port.Ends == 2 && port.Active == 0 && model.ResourceCount == 0 && port.Creates == 1, "Reentrant closure acquired a resource or leaked a batch.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
