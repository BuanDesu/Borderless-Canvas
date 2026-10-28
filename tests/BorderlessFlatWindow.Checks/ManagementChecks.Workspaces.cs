using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    private static IEnumerable<(string, Action)> WorkspaceCases()
    {
        yield return ("Workspace resize uses durable ownership and pause restores original position and dimensions", WorkspaceResize);
        yield return ("Workspace can register a new ordinary window directly at its saved bounds without automatic arrangement", WorkspaceNew);
        yield return ("Workspace minimized preparation and cancellation preserve original show state and journal", WorkspaceMinimized);
        yield return ("Rejected workspace resize restores the original group and retains failed recovery records", WorkspaceRejected);
        yield return ("Workspace registration failure and cancel/exit reentry cannot submit a late layout", WorkspaceReentry);
        yield return ("Workspace geometry follows DPI and relocates a removed monitor's invisible member", WorkspaceDisplays);
        yield return ("Workspace layouts preserve visible negative-world positions on uniform-DPI models and reject mixed-DPI management", WorkspaceNegativeDisplays);
    }
    private static void WorkspaceResize()
    {
        using var h = new Harness(); h.Begin(); var original = h.Native[42].Bounds; var saved = h.Saved;
        var entry = h.Selected().Single(e => e.Observation.Handle == 42); var bounds = Rect(320, 200, 550, 370);
        h.Session.BeginWorkspaceRestore(); Require(h.Session.ApplyWorkspaceMember(entry, bounds, Now), h.Session.Message); h.Tick();
        Require(h.LastBatch.Single().Resize && h.Native[42].Bounds == new RectD(bounds.Position + h.Layout.CanvasWorkArea.Position, bounds.Size)
            && h.Session.Workspace.Windows.Single(w => w.Id == entry.Id).Bounds == bounds && h.Saved == saved
            && h.Session.CheckWorkspaceMember(entry.Id!.Value, entry.Observation.Identity!.Value) == WorkspacePlacement.Confirmed, "Resize bypassed journal or failed confirmation.");
        h.Session.EndWorkspaceRestore(); h.Session.Pause();
        Require(h.Native[42].Bounds == original && h.Saved!.Entries.Count == 0 && h.Native[43].Bounds == Rect(900, 140), "Pause did not restore original dimensions.");
    }
    private static void WorkspaceNew()
    {
        using var h = new Harness(); Require(h.Session.Start([], Now), h.Session.Message); var entry = h.Selected().First(); var original = entry.Observation.Bounds;
        h.Session.BeginWorkspaceRestore(); var target = Rect(230, 190, 500, 320);
        Require(h.Session.ApplyWorkspaceMember(entry, target, Now), h.Session.Message); h.Tick();
        Require(h.Session.ManagedCount == 1 && h.Native[42].Bounds == new RectD(target.Position + h.Layout.CanvasWorkArea.Position, target.Size)
            && h.Session.Workspace.Windows.Single().Bounds == target && h.Batches == 1 && h.Saved!.Entries.Count == 1, "New target was arranged or moved more than once.");
        h.Session.EndWorkspaceRestore(); h.Session.Pause(); Require(h.Native[42].Bounds == original && h.Saved!.Entries.Count == 0, "New registration lost its durable original.");
    }
    private static void WorkspaceMinimized()
    {
        foreach (var cancel in new[] { false, true })
        {
            using var h = new Harness(); h.Native[42] = h.Native[42] with { IsMinimized = true }; h.Session.Start([], Now);
            h.Session.BeginWorkspaceRestore(); var entry = h.Inspection.Inspect().Entries.Single(e => e.Observation.Handle == 42);
            Require(h.Session.RestoreWorkspaceMinimized(entry) && h.Saved!.Entries.Single().Original.Placement.ShowCommand == 2 && !h.Native[42].IsMinimized, "Minimize restore lacked its original show state.");
            if (!cancel)
            {
                entry = h.Selected().Single(e => e.Observation.Handle == 42);
                Require(h.Session.ApplyWorkspaceMember(entry, Rect(250, 210, 540, 330), Now), h.Session.Message); h.Tick();
            }
            h.Session.EndWorkspaceRestore(); if (!cancel) h.Session.Pause();
            Require(h.Native[42].IsMinimized && h.Saved!.Entries.Count == 0, "Original minimize state or cancellation ownership was lost.");
        }
    }
    private static void WorkspaceRejected()
    {
        foreach (var failRestore in new[] { false, true })
        {
            using var h = new Harness(); h.Begin(); var entry = h.Selected().First(); h.RejectPartial = true; h.FailRestore = failRestore;
            h.Session.BeginWorkspaceRestore(); Require(!h.Session.ApplyWorkspaceMember(entry, Rect(300, 200, 500, 350), Now), "Rejected resize reported success.");
            Require(!h.Session.IsRunning && (failRestore ? h.Saved!.Entries.Count == 2 && h.Session.Status == CanvasSessionStatus.RecoveryRequired
                : h.Saved!.Entries.Count == 0 && h.Native[42].Bounds == Rect(100, 140)), "Failed layout lost recovery evidence or original size.");
            h.FailRestore = false;
        }
    }
    private static void WorkspaceReentry()
    {
        foreach (var boundary in new[] { "cancel", "exit", "storage" })
        {
            using var h = new Harness(); h.Session.Start([], Now); var entry = h.Selected().First(); h.Session.BeginWorkspaceRestore();
            if (boundary == "storage") h.InvalidDataSaves = 1;
            else h.DuringCapture = _ => { if (boundary == "exit") h.Session.Dispose(); else h.Session.EndWorkspaceRestore(); };
            Require(!h.Session.ApplyWorkspaceMember(entry, Rect(300, 200, 500, 350), Now) && h.Batches == 0 && h.Saved?.Entries.Count is null or 0,
                "A cancelled/failed registration moved a target or leaked a journal.");
        }
        using var submitted = new Harness(); submitted.Begin(); var target = submitted.Selected().First(); submitted.Session.BeginWorkspaceRestore();
        submitted.DuringBatch = submitted.Session.EndWorkspaceRestore;
        Require(!submitted.Session.ApplyWorkspaceMember(target, Rect(300, 200, 500, 350), Now)
            && !submitted.Session.IsRunning && submitted.Saved!.Entries.Count == 0 && submitted.Native[42].Bounds == Rect(100, 140), "Cancellation during submission stranded a late resize.");
    }
    private static void WorkspaceDisplays()
    {
        using var h = new Harness(); var member = new WorkspaceMember(Guid.NewGuid(), Guid.NewGuid(), "Synthetic", "C:\\Synthetic\\A.exe", "Fixture",
            Rect(-8000, 6000, 400, 300), null);
        var bookmark = new LocationBookmark(Guid.NewGuid(), "Saved", new(-900, 200), new(Guid.NewGuid(), 192, [member]));
        var projected = h.Session.ProjectWorkspaceMember(bookmark, member);
        Require(projected.Size == new SizeD(200, 150) && projected.Position == bookmark.CameraPosition + new PointD(24, 24), "Removed monitor or DPI projection was unsafe.");
    }
    private static void WorkspaceNegativeDisplays()
    {
        using var h = new Harness(multiple: true, sameDpi: true);
        var camera = new PointD(-5000, -2400);
        var position = camera + h.Layout.Monitors[1].WorkArea.Position - h.Layout.CanvasWorkArea.Position + new PointD(10, 20);
        var member = new WorkspaceMember(Guid.NewGuid(), Guid.NewGuid(), "Synthetic left monitor", "C:\\Synthetic\\A.exe", "Fixture", new(position, new(400, 300)), null);
        var bookmark = new LocationBookmark(Guid.NewGuid(), "Negative world", camera, new(Guid.NewGuid(), 96, [member]));
        Require(h.Session.ProjectWorkspaceMember(bookmark, member) == member.Bounds, "Visible negative geometry was relocated or scaled.");
        using var mixed = new Harness(multiple: true);
        Require(!mixed.Layout.SupportsCanvas && !mixed.Session.Start([], Now), "Mixed-DPI model bypassed the existing unsupported-layout guard.");
    }
}
