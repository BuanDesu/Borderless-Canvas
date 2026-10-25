using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    private static IEnumerable<(string Name, Action Run)> LifetimeCases()
    {
        yield return ("An empty canvas starts and admits future windows without resetting its view", EmptyStart);
        yield return ("Runtime windows can close in any order and the final empty canvas accepts another window", RuntimeClose);
        yield return ("Hidden windows stay fixed while survivors pan, then rejoin their saved world position", HiddenRejoin);
        yield return ("A closing active window cannot pull the camera toward a formerly parked window", CloseForeground);
        yield return ("Closing a pending animation target cancels activation without pausing other windows", CloseMotion);
        yield return ("A missing catalog target with live native ownership cannot be silently forgotten", UnverifiedDisappearance);
        yield return ("Minimizing the final window preserves the active camera, restoration position and subsequent admission", MinimizedLast);
        yield return ("Minimizing and then closing the final window leaves the canvas ready for a new window", MinimizedThenClosed);
        yield return ("Foreground restoration of a minimized offscreen window finds its preserved world position", RestoreMinimizedForeground);
    }
    private static void RestoreMinimizedForeground()
    {
        using var h = new Harness(); h.Begin(); h.Tick(foreground: 42); var world = h.Session.Workspace.Windows.ToArray();
        h.Session.Navigate(new(9000, 0), Now); h.Tick(foreground: 42);
        h.Native[42] = h.Native[42] with { IsMinimized = true }; h.Tick(foreground: 43);
        Require(h.Session.IsRunning && h.Session.Workspace.Camera.Position == new PointD(9000, 0), "Minimize fallback unexpectedly focused the parked survivor.");
        h.Session.Pan(new(400, 200), Now); h.Tick(foreground: 43);
        h.Native[42] = h.Native[42] with { IsMinimized = false, Bounds = Rect(100, 140) }; h.Tick(foreground: 42); h.Tick(foreground: 42);
        Require(h.Session.IsRunning && h.Session.Workspace.Windows.SequenceEqual(world) && h.Session.ActiveWindowId == world[0].Id
            && h.Native[42].Bounds!.Value.Position.X < h.Layout.CanvasWorkArea.Right && h.Native[42].Bounds!.Value.Right > h.Layout.CanvasWorkArea.Left,
            "Taskbar-style restore failed to locate the saved world geometry.");
    }
    private static void MinimizedLast()
    {
        using var h = new Harness(); h.Native.Remove(43); h.Begin(); h.Session.Navigate(new(500, 200), Now); h.Tick();
        var world = h.Session.Workspace.Windows.Single();
        h.Native[42] = h.Native[42] with { IsMinimized = true, Bounds = Rect(-32000, -32000) }; h.Tick();
        Require(h.Session.IsRunning && !h.Session.HasPending, "Last-window minimize stopped the canvas or retained motion confirmation.");
        h.Session.Pan(new(120, 40), Now); h.Tick();
        Require(h.Native[42].Bounds == Rect(-32000, -32000) && h.Session.Workspace.Windows.Single() == world, "Minimized native position polluted its world layout.");
        h.Native[44] = Window(44, Rect(80, 80)); Require(h.Session.AddWindows(Late(h, 44), Now), h.Session.Message); h.Tick();
        Require(h.Session.CycleWindow(1, Now), "Visible successor cannot be selected."); h.Tick();
        Require(h.Session.ManagedIds.Count == 2 && h.Native[42].IsMinimized, "Admission unexpectedly restored a minimized window.");
        h.Native[42] = h.Native[42] with { IsMinimized = false, Bounds = Rect(100, 140) }; h.Tick(); h.Tick();
        Require(h.Session.IsRunning && h.Session.Workspace.Windows.Single(item => item.Id == world.Id) == world, "Restoring a minimized window changed its world position.");
    }
    private static void MinimizedThenClosed()
    {
        using var h = new Harness(); h.Native.Remove(43); h.Begin(); h.Session.Navigate(new(900, -200), Now); h.Tick();
        var camera = h.Session.Workspace.Camera;
        h.Native[42] = h.Native[42] with { IsMinimized = true }; h.Tick(); h.Native.Remove(42); h.Tick();
        Require(h.Session.IsRunning && h.Session.ManagedIds.Count == 0 && h.Session.Workspace.Camera == camera && h.Saved!.Entries.Count == 0,
            "Closing the minimized final window ended the empty canvas or retained its record.");
        h.Native[44] = Window(44, Rect(70, 90)); Require(h.Session.AddWindows(Late(h, 44), Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Camera == camera, "New admission reset the empty camera.");
    }
    private static void EmptyStart()
    {
        using var h = new Harness(); h.Native.Clear(); Require(h.Session.Start([], Now), h.Session.Message);
        Require(h.Session.IsRunning && h.Session.ManagedIds.Count == 0, "Empty launch did not stay ready.");
        h.Session.Navigate(new(900, -400), Now); h.Tick();
        h.Native[44] = Window(44, Rect(100, 100)); Require(h.Session.AddWindows(Late(h, 44), Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Camera.Position == new PointD(900, -400) && h.Session.ManagedIds.Count == 1, "Future admission reset the empty view.");
    }
    private static void RuntimeClose()
    {
        using var h = new Harness(); h.Begin(); h.Native[44] = Window(44, Rect(50, 60)); h.Session.AddWindows(Late(h, 44), Now); h.Tick();
        h.Session.Navigate(new(300, -200), Now); h.Tick(); var camera = h.Session.Workspace.Camera; h.Session.Workspace.AddBookmark("Area");
        foreach (var handle in new nint[] { 43, 44, 42 })
        {
            var restores = h.Restorations; h.Native.Remove(handle); h.Tick();
            Require(h.Session.IsRunning && h.Restorations == restores && h.Session.Workspace.Camera == camera
                && h.Saved!.Entries.Count == h.Native.Count, "A runtime close paused/restored the desktop or retained stale ownership.");
        }
        h.Native[45] = Window(45, Rect(80, 80)); Require(h.Session.AddWindows(Late(h, 45), Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Camera == camera && h.Session.Workspace.Bookmarks.Count == 1, "Post-close admission reset camera or workspaces.");
    }
    private static void HiddenRejoin()
    {
        using var h = new Harness(); h.Begin(); var world = h.Session.Workspace.Windows.ToArray();
        h.Native[42] = h.Native[42] with { IsVisible = false }; h.Tick(); var hidden = h.Native[42].Bounds;
        h.Session.Pan(new(200, 80), Now); h.Tick();
        Require(h.Session.IsRunning && h.Native[42].Bounds == hidden && h.Native[43].Bounds == Rect(700, 60), "Hidden target moved or survivor stopped.");
        h.Native[42] = h.Native[42] with { IsVisible = true }; h.Tick(); h.Tick();
        Require(h.Session.IsRunning && h.Native[42].Bounds == Rect(-100, 60) && h.Session.Workspace.Windows.SequenceEqual(world), "Reappearance lost world geometry.");
    }
    private static void CloseForeground()
    {
        using var h = new Harness(); h.Begin(); h.Tick(foreground: 42); h.Session.Navigate(new(8000, 0), Now); h.Tick(foreground: 42);
        h.Native.Remove(42); h.Tick(foreground: 43);
        Require(h.Session.IsRunning && h.Session.Workspace.Camera.Position == new PointD(8000, 0), "OS foreground fallback overrode the closing view.");
    }
    private static void CloseMotion()
    {
        using var h = new Harness(); h.Begin(); h.Session.CycleWindow(1, Now, animate: true);
        var target = h.Session.Workspace.Windows[0].Id; var handle = h.Selected().Single(entry => entry.Id == target).Observation.Handle;
        h.Native.Remove(handle); h.Tick(); h.Session.AdvanceCameraTransition(Now.AddMilliseconds(500));
        Require(h.Session.IsRunning && !h.Session.HasCameraTransition && h.Activations == 0, "Closed animation target retained activation or stopped survivors.");
    }
    private static void UnverifiedDisappearance()
    {
        using var h = new Harness(); h.Begin(); var snapshot = h.Inspection.Inspect();
        h.Session.Observe(snapshot with { Entries = snapshot.Entries.Where(entry => entry.Observation.Handle != 42).ToArray() }, 0, Now);
        Require(!h.Session.IsRunning && h.Restorations > 0, "A live owned window was silently forgotten on an incomplete snapshot.");
    }
}
