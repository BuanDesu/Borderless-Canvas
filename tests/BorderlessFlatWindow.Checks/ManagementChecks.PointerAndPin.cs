using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    private static IEnumerable<(string Name, Action Run)> PointerAndPinCases()
    {
        yield return ("Window cycling traverses intermediate camera frames before activation and keeps world geometry", AnimatedCycle);
        yield return ("Rapid wheel retargeting continues from the current frame and records one return origin", RetargetCycle);
        yield return ("Explicit navigation and overview suspension cancel older window-cycle activation", CancelCycle);
        yield return ("Detaching a visible window keeps its physical position and releases only its recovery ownership", Detach);
        yield return ("Detached windows rejoin at the current screen position without disturbing the camera", Rejoin);
        yield return ("Detachment refuses pending movement and cannot claim success after a journal write failure", DetachFailure);
        yield return ("All-window start and admission skip only access-denied identities after complete registration rollback", DefaultScope);
        yield return ("Resuming an expanded default scope keeps old world positions and camera while adding new physical positions", ExpandedResume);
    }
    private static void AnimatedCycle()
    {
        using var h = new Harness(); h.Begin(); var world = h.Session.Workspace.Windows.ToArray(); var before = h.Session.Workspace.Camera.Position;
        Require(h.Session.CycleWindow(1, Now, animate: true) && h.Session.HasCameraTransition && h.Session.Workspace.Camera.Position == before, "Cycle jumped directly to its destination.");
        Require(h.Session.AdvanceCameraTransition(Now.AddMilliseconds(70)), "Intermediate frame rejected."); h.Tick(70);
        var middle = h.Session.Workspace.Camera.Position;
        Require(middle != before && h.Activations == 0 && h.Session.Workspace.Windows.SequenceEqual(world), "Intermediate frame activated a window or altered world layout.");
        h.Session.AdvanceCameraTransition(Now.AddMilliseconds(150));
        Require(h.Activations == 0, "Target activated before native geometry was observed."); h.Tick(150);
        Require(!h.Session.HasCameraTransition && h.Activations == 1 && h.Session.Workspace.Camera.Position != middle && h.Session.Workspace.PreviousView == before, "Cycle did not finish or record a single return origin.");
    }
    private static void RetargetCycle()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Camera.Position;
        h.Session.CycleWindow(1, Now, true); h.Session.AdvanceCameraTransition(Now.AddMilliseconds(60)); h.Tick(60);
        var middle = h.Session.Workspace.Camera.Position;
        h.Session.CycleWindow(1, Now.AddMilliseconds(60), true);
        Require(h.Session.Workspace.Camera.Position == middle, "Retarget jumped back to the old origin.");
        h.Session.AdvanceCameraTransition(Now.AddMilliseconds(210)); h.Tick(210);
        Require(h.Activations == 1 && h.Session.Workspace.PreviousView == before && h.Session.Back(Now.AddMilliseconds(220)), "Retarget published an intermediate history or activation.");
        h.Tick(220); Require(h.Session.Workspace.Camera.Position == before, "Back did not return to the complete cycle origin.");
    }
    private static void CancelCycle()
    {
        using var h = new Harness(); h.Begin(); h.Session.CycleWindow(1, Now, true); h.Session.AdvanceCameraTransition(Now.AddMilliseconds(60)); h.Tick(60);
        h.Session.Navigate(new(400, 200), Now.AddMilliseconds(70)); h.Tick(70);
        Require(!h.Session.HasCameraTransition && !h.Session.AdvanceCameraTransition(Now.AddMilliseconds(500)) && h.Session.Workspace.Camera.Position == new PointD(400, 200) && h.Activations == 0, "Old cycle overrode explicit navigation.");
        h.Session.CycleWindow(1, Now.AddMilliseconds(600), true); h.Session.SuspendNavigation(true);
        Require(!h.Session.HasCameraTransition && !h.Session.AdvanceCameraTransition(Now.AddMilliseconds(900)), "Overview retained background camera animation.");
    }
    private static void Detach()
    {
        using var h = new Harness(); h.Begin(); var target = h.Selected()[0]; var id = target.Id!.Value; var before = h.Native[target.Observation.Handle].Bounds;
        var batches = h.Batches; var restores = h.Restorations; var camera = h.Session.Workspace.Camera;
        Require(h.Session.DetachWindow(id) && h.Session.IsRunning && h.Session.ManagedIds.Count == 1 && !h.Session.Workspace.Windows.Any(window => window.Id == id)
            && h.Native[target.Observation.Handle].Bounds == before && h.Batches == batches && h.Restorations == restores && h.Saved!.Entries.Count == 1 && h.Session.Workspace.Camera == camera,
            "Detachment moved/restored a window or lost another owner's journal.");
        h.Session.Pan(new(200, 100), Now); h.Tick();
        Require(h.Native[target.Observation.Handle].Bounds == before, "Detached window followed the canvas.");
    }
    private static void Rejoin()
    {
        using var h = new Harness(); h.Begin(); var target = h.Selected()[0]; h.Session.DetachWindow(target.Id!.Value);
        h.Session.Navigate(new(800, 300), Now); h.Tick(); var before = h.Native[target.Observation.Handle].Bounds; var camera = h.Session.Workspace.Camera.Position;
        var entry = h.Selected().Single(item => item.Id == target.Id);
        Require(h.Session.AddWindows([entry], Now, preservePhysicalPosition: true) && h.Native[target.Observation.Handle].Bounds == before
            && h.Session.Workspace.Camera.Position == camera && h.Saved!.Entries.Count == 2, "Rejoin moved the native window or reset the camera.");
        h.Tick(); h.Session.Pan(new(100, 0), Now); h.Tick();
        Require(h.Native[target.Observation.Handle].Bounds!.Value.Position.X == before!.Value.Position.X - 100, "Rejoined window did not resume canvas movement.");
    }
    private static void DetachFailure()
    {
        using var h = new Harness(); h.Begin(); var id = h.Session.ManagedIds[0]; h.Delayed = true; h.Session.Pan(new(50, 0), Now);
        Require(!h.Session.DetachWindow(id) && h.Saved!.Entries.Count == 2, "Pending native placement was detached.");
        h.Delayed = false; foreach (var batch in h.Pending) h.Apply(batch); h.Tick();
        h.InvalidDataSaves = 1;
        Require(!h.Session.DetachWindow(id) && !h.Session.IsRunning, "Failed durable release continued native management or claimed a pin.");
    }
    private static void DefaultScope()
    {
        using var h = new Harness(); h.DeniedMark = 42;
        Require(h.Session.Start(h.Selected(), Now, skipUnavailable: true) && h.Session.ManagedIds.Count == 1 && h.Session.SkippedUnavailable.Single().Handle == 42
            && h.Saved!.Entries.Count == 1 && h.Saved.Entries.All(entry => entry.Identity.Handle != 42), "Access-denied registration left ownership or blocked another eligible window.");
        h.Tick(); h.Native[44] = Window(44, Rect(200, 200)); h.DeniedMark = 44;
        var before = h.Session.Workspace.Windows.ToArray(); var entries = h.Selected().Where(entry => entry.Observation.Handle == 44).ToArray();
        Require(!h.Session.AddWindows(entries, Now, skipUnavailable: true) && h.Session.IsRunning && h.Session.Workspace.Windows.SequenceEqual(before)
            && h.Session.SkippedUnavailable.Any(identity => identity.Handle == 44) && h.Saved!.Entries.Count == 1, "An inaccessible new window disturbed the existing canvas.");
    }
    private static void ExpandedResume()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(700, -200), Now); h.Tick();
        var old = h.Session.Workspace.Windows.ToArray(); h.Session.Pause();
        h.Native[44] = Window(44, Rect(200, 200));
        Require(h.Session.Start(h.Selected(), Now, skipUnavailable: true) && h.Session.Workspace.Camera.Position == new PointD(700, -200)
            && old.All(window => h.Session.Workspace.Windows.Single(item => item.Id == window.Id).Bounds == window.Bounds), "Expanded scope reset old world positions or camera.");
        h.Tick(); Require(h.Native[44].Bounds == Rect(200, 200), "New window did not retain its current physical position on resume.");
    }
}
