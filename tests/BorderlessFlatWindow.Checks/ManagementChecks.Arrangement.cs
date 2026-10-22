using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    private static IEnumerable<(string Name, Action Run)> ArrangementCases()
    {
        yield return ("Arrangement preview is read-only and submission moves only chosen native identities", ArrangementSelection);
        yield return ("Arrangement undo restores world positions after navigation without changing camera history or labels", ArrangementUndo);
        yield return ("Stale or foreign arrangement previews cannot move windows", ArrangementStale);
        yield return ("Pending, suspended and paused sessions cannot submit arrangements or undo", ArrangementDeferred);
        yield return ("Partially rejected arrangements restore originals without publishing the proposed layout", ArrangementRejected);
        yield return ("Arrangement timeout restores all windows and clears undo", ArrangementTimeout);
        yield return ("External edits invalidate selected-window undo while title and unrelated moves preserve it", ArrangementExternal);
        yield return ("No-op arrangement does not consume the preceding undo", ArrangementNoop);
        yield return ("Closing during arrangement submission restores originals before disposing ownership", ArrangementDispose);
        yield return ("Pause/resume invalidates old previews even when the logical geometry is identical", ArrangementEpoch);
        yield return ("Multiscreen arrangement uses the primary work-area offset and preserves the other target", ArrangementMultipleScreens);
    }
    private static RectD ArrangementRegion => new(new(300, 150), new(1000, 600));
    private static CanvasArrangementPlan Plan(Harness h, bool both = false) => h.Session.PreviewArrangement(
        h.Session.Workspace.Windows.Take(both ? 2 : 1).Select(w => w.Id).ToArray(), ArrangementRegion) ?? throw new InvalidOperationException(h.Session.Message);
    private static void ArrangementSelection()
    {
        using var h = new Harness(); h.Begin();
        var before = h.Session.Workspace.Windows.ToArray(); var batches = h.Batches; var journal = h.Saved;
        var plan = Plan(h);
        Require(h.Batches == batches && h.Session.Workspace.Windows.SequenceEqual(before) && ReferenceEquals(journal, h.Saved), "Preview changed authoritative state.");
        Require(h.Session.ApplyArrangement(plan, Now), h.Session.Message); h.Tick();
        Require(h.LastBatch.Count == 1 && h.Native[43].Bounds == Rect(900, 140) && h.Activations == 0
            && h.Session.Workspace.Windows[1] == before[1] && h.Session.Workspace.Windows[0].Bounds.Size == before[0].Bounds.Size
            && ReferenceEquals(journal, h.Saved) && h.Session.CanUndoArrangement, "Arrangement affected unselected/native size/recovery registration.");
        h.Session.Pause(); Require(h.Native[42].Bounds == Rect(100, 140) && h.Native[43].Bounds == Rect(900, 140), "Pause lost original placement.");
    }
    private static void ArrangementUndo()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray();
        Require(h.Session.ApplyArrangement(Plan(h, true), Now), h.Session.Message); h.Tick();
        h.Session.Pan(new(150, -70), Now); h.Tick(); var camera = h.Session.Workspace.Camera; var history = h.Session.Workspace.PreviousView;
        var label = h.Session.Workspace.AddBookmark("Synthetic");
        Require(h.Session.UndoArrangement(Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Windows.SequenceEqual(before) && h.Session.Workspace.Camera == camera
            && h.Session.Workspace.PreviousView == history && h.Session.Workspace.Bookmarks.Single() == label
            && h.Native[42].Bounds == Rect(-50, 210) && !h.Session.CanUndoArrangement, "Undo changed camera, history, bookmarks or native size.");
    }
    private static void ArrangementStale()
    {
        using var h = new Harness(); h.Begin(); var plan = Plan(h); var batches = h.Batches;
        h.Native[43] = h.Native[43] with { Bounds = Rect(950, 180) }; h.Tick();
        Require(!h.Session.ApplyArrangement(plan, Now) && h.Batches == batches, "Changed obstacle accepted stale preview.");
        using var other = new Harness(); other.Begin();
        Require(!other.Session.ApplyArrangement(plan, Now), "Foreign plan accepted.");
        plan = Plan(h); h.Native[44] = Window(44, Rect(1700, 700, 300, 200));
        var added = h.Selected().Single(e => e.Observation.Handle == 44);
        Require(h.Session.AddWindows([added], Now), h.Session.Message); h.Tick(); batches = h.Batches;
        Require(!h.Session.ApplyArrangement(plan, Now) && h.Batches == batches, "New obstacle accepted stale preview.");
    }
    private static void ArrangementDeferred()
    {
        using var h = new Harness(); h.Begin(); var plan = Plan(h); h.Session.SuspendNavigation(true); var batches = h.Batches;
        Require(!h.Session.ApplyArrangement(plan, Now) && h.Batches == batches, "Suspended arrangement moved.");
        h.Session.SuspendNavigation(false); h.Delayed = true;
        Require(h.Session.ApplyArrangement(plan, Now) && !h.Session.CanUndoArrangement && !h.Session.UndoArrangement(Now)
            && h.Session.PreviewArrangement(plan.Targets.Select(w => w.Id).ToArray(), ArrangementRegion) is null, "Pending movement allowed another command.");
        h.Apply(h.Pending.Single()); h.Tick(); h.Session.Pause(); batches = h.Batches;
        Require(!h.Session.ApplyArrangement(plan, Now) && !h.Session.UndoArrangement(Now) && h.Batches == batches, "Paused command moved.");
    }
    private static void ArrangementRejected()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray(); h.RejectPartial = true;
        Require(!h.Session.ApplyArrangement(Plan(h, true), Now) && !h.Session.IsRunning && !h.Session.CanUndoArrangement
            && h.Session.Workspace.Windows.SequenceEqual(before) && h.Native[42].Bounds == Rect(100, 140)
            && h.Native[43].Bounds == Rect(900, 140) && h.Saved!.Entries.Count == 0, "Partial rejection leaked layout/recovery.");
    }
    private static void ArrangementTimeout()
    {
        using var h = new Harness(); h.Begin(); h.Delayed = true;
        Require(h.Session.ApplyArrangement(Plan(h, true), Now), h.Session.Message); h.Tick(1700);
        Require(!h.Session.IsRunning && !h.Session.CanUndoArrangement && h.Native[42].Bounds == Rect(100, 140)
            && h.Native[43].Bounds == Rect(900, 140) && h.Saved!.Entries.Count == 0, "Timeout left a partial native group.");
    }
    private static void ArrangementExternal()
    {
        using var h = new Harness(); h.Begin(); Require(h.Session.ApplyArrangement(Plan(h), Now), h.Session.Message); h.Tick();
        h.Native[42] = h.Native[42] with { Title = "Changed synthetic title" }; h.Tick();
        h.Native[43] = h.Native[43] with { Bounds = Rect(1000, 140) }; h.Tick(); Require(h.Session.CanUndoArrangement, "Unrelated update invalidated undo.");
        h.Native[42] = h.Native[42] with { Bounds = Rect(200, 200) }; h.Tick(); var batches = h.Batches;
        Require(!h.Session.CanUndoArrangement && !h.Session.UndoArrangement(Now) && h.Batches == batches, "Undo overwrote a manual edit.");
    }
    private static void ArrangementNoop()
    {
        using var h = new Harness(); h.Begin(); Require(h.Session.ApplyArrangement(Plan(h), Now), h.Session.Message); h.Tick();
        var noop = Plan(h); var batches = h.Batches;
        Require(noop.ChangedCount == 0 && !h.Session.ApplyArrangement(noop, Now) && h.Batches == batches && h.Session.CanUndoArrangement, "No-op consumed undo.");
    }
    private static void ArrangementDispose()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray();
        h.DuringBatch = () => h.Session.Dispose();
        Require(!h.Session.ApplyArrangement(Plan(h, true), Now) && !h.Session.IsRunning
            && h.Session.Workspace.Windows.SequenceEqual(before) && h.Native[42].Bounds == Rect(100, 140)
            && h.Native[43].Bounds == Rect(900, 140) && h.Saved!.Entries.Count == 0, "Reentrant disposal leaked pending ownership/layout.");
    }
    private static void ArrangementEpoch()
    {
        using var h = new Harness(); h.Begin(); var plan = Plan(h); h.Session.Pause(); h.Begin(); var batches = h.Batches;
        Require(!h.Session.ApplyArrangement(plan, Now) && h.Batches == batches, "Prior recovery epoch accepted an old preview.");
        plan = Plan(h); h.Native[42] = h.Native[42] with { Title = "Renamed before submit" }; h.Tick();
        Require(h.Session.ApplyArrangement(plan, Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Windows.First().Title == "Renamed before submit", "Frozen preview overwrote the current title.");
    }
    private static void ArrangementMultipleScreens()
    {
        using var h = new Harness(multiple: true, sameDpi: true); h.Begin();
        Require(h.Session.Pan(new(80, -20), Now), h.Session.Message); h.Tick();
        var before = h.Session.Workspace.Windows.ToArray(); var other = h.Native[43].Bounds;
        var primary = h.Layout.Primary.WorkArea;
        var area = new RectD(h.Session.Workspace.Camera.Position + primary.Position - h.Layout.CanvasWorkArea.Position, primary.Size);
        var plan = h.Session.PreviewArrangement([before[0].Id], area);
        Require(plan is not null && h.Session.ApplyArrangement(plan, Now), h.Session.Message); h.Tick();
        Require(h.Native[42].Bounds == Rect(16, 56) && h.Native[43].Bounds == other, "Primary work-area offset or other window changed.");
        Require(h.Session.UndoArrangement(Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Windows.SequenceEqual(before) && h.Native[42].Bounds == Rect(20, 160), "Multiscreen undo drifted.");
    }
}
