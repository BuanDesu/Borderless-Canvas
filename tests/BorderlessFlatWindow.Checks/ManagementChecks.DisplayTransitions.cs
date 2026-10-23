using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    private static IEnumerable<(string Name, Action Run)> DisplayTransitionCases()
    {
        yield return ("Paused display reconfiguration preserves world layout, camera, history and labels", ReconfigurePreserves);
        yield return ("Running or reentrant native work refuses display replacement", ReconfigureBusy);
        yield return ("Mixed DPI after pause retains the canvas but cannot resume native management", ReconfigureMixed);
        yield return ("Display reconfiguration cannot conceal required recovery or overwrite its journal", ReconfigureRecovery);
    }
    private static DisplayLayout ChangedLayout(bool mixed = false) => new([
        new DisplayMonitor("new-primary", true, new(-1280, 0, 0, 1024), new(-1280, 0, 0, 1000), 96),
        new DisplayMonitor("secondary", false, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), mixed ? 144u : 96u)]);
    private static void ReconfigurePreserves()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(8000, -4000), Now); h.Tick();
        h.Session.Workspace.AddBookmark("Synthetic area"); var world = h.Session.Workspace.Windows.ToArray();
        var camera = h.Session.Workspace.Camera.Position; var back = h.Session.Workspace.PreviousView; var labels = h.Session.Workspace.Bookmarks.ToArray();
        h.Session.Pause(); var batches = h.Batches; var restores = h.Restorations; var journal = h.Saved;
        var next = ChangedLayout();
        Require(h.Session.ReconfigureDisplay(next) && h.Session.MatchesDisplay(next) && !h.Session.IsRunning
            && h.Session.Workspace.Windows.SequenceEqual(world) && h.Session.Workspace.Camera.Position == camera
            && h.Session.Workspace.Camera.ViewportSize == next.CanvasWorkArea.Size && h.Session.Workspace.PreviousView == back
            && h.Session.Workspace.Bookmarks.SequenceEqual(labels) && h.Batches == batches && h.Restorations == restores && ReferenceEquals(journal, h.Saved), "Display update moved windows, reset world state or changed recovery.");
        Require(h.Session.Start(h.Selected(), Now) && h.Session.Workspace.Windows.SequenceEqual(world) && h.Session.Workspace.Camera.Position == camera, "Explicit resume reset the old canvas.");
        h.Tick(); Require(h.Session.Back(Now), "History was lost after display update."); h.Tick();
        Require(h.Session.Workspace.Camera.Position == back && h.Session.Workspace.Windows.SequenceEqual(world), "Back changed world coordinates.");
    }
    private static void ReconfigureBusy()
    {
        using var h = new Harness(); h.Begin(); var next = ChangedLayout();
        Require(!h.Session.ReconfigureDisplay(next) && h.Session.MatchesDisplay(h.Layout), "Running reconfiguration replaced native geometry.");
        var checkedBusy = false;
        h.DuringBatch = () => { checkedBusy = true; Require(!h.Session.ReconfigureDisplay(next), "Reentrant native batch changed display."); };
        Require(h.Session.Pan(new(100, 50), Now) && checkedBusy && h.Session.MatchesDisplay(h.Layout), "Reentrant display test did not retain its original layout.");
    }
    private static void ReconfigureMixed()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(300, 100), Now); h.Tick(); h.Session.Pause();
        var world = h.Session.Workspace.Windows.ToArray(); var batches = h.Batches;
        Require(h.Session.ReconfigureDisplay(ChangedLayout(mixed: true)) && !h.Session.Start(h.Selected(), Now)
            && !h.Session.IsRunning && h.Batches == batches && h.Session.Workspace.Windows.SequenceEqual(world) && h.Saved!.Entries.Count == 0,
            "Unsupported display transition moved windows or erased the canvas.");
        Require(h.Session.ReconfigureDisplay(h.Layout) && h.Session.Start(h.Selected(), Now) && h.Session.Workspace.Windows.SequenceEqual(world), "Returning to supported displays could not resume preserved layout.");
    }
    private static void ReconfigureRecovery()
    {
        using var h = new Harness(); h.Begin(); h.FailRestore = true; h.Session.Pause(); var journal = h.Saved; var restores = h.Restorations;
        Require(h.Session.ReconfigureDisplay(ChangedLayout()) && h.Session.Status == CanvasSessionStatus.RecoveryRequired
            && ReferenceEquals(journal, h.Saved) && !h.Session.Start(h.Selected(), Now) && h.Session.Status == CanvasSessionStatus.RecoveryRequired
            && ReferenceEquals(journal, h.Saved) && h.Restorations == restores, "Display update claimed recovery or consumed inherited records.");
    }
}
