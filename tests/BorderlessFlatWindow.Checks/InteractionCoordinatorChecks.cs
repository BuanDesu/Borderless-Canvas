using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Startup;

internal static class InteractionCoordinatorChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Launch survives transient reads and disabled owners, then starts once without windows", DeferredLaunch);
        yield return ("Launch never overrides pause, recovery, display failure or a system transition", LaunchBoundaries);
        yield return ("Short reads retain input while explicit editing and recovery boundaries revoke it", InputBoundaries);
        yield return ("Delayed touch entry cannot consume a newer request or revive cancellation", TouchGenerations);
        yield return ("Production arguments preserve paths and reject lab commands before desktop initialization", ArgumentBoundary);
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Interaction contract violated."); }
    private static void DeferredLaunch()
    {
        var launch = new CanvasLaunchCoordinator(); launch.Request();
        foreach (var state in new[] { new CanvasInteractionState(Reading: true), new(Busy: true), new(Editing: true), new(OwnerBlocked: true), new(DisplayReady: false) })
            Require(launch.Advance(CanvasSessionStatus.Idle, state, true, true, false) == LaunchStartDecision.Wait && launch.Pending);
        Require(launch.Advance(CanvasSessionStatus.Idle, new(), true, false, false) == LaunchStartDecision.Wait);
        Require(launch.Advance(CanvasSessionStatus.Idle, new(), true, true, false) == LaunchStartDecision.Start && !launch.Pending);
        Require(launch.Advance(CanvasSessionStatus.Idle, new(), true, true, false) == LaunchStartDecision.None);
        launch.Request(); launch.Cancel(); Require(!launch.Pending);
    }
    private static void LaunchBoundaries()
    {
        foreach (var status in new[] { CanvasSessionStatus.Running, CanvasSessionStatus.Paused, CanvasSessionStatus.RecoveryRequired })
        {
            var launch = new CanvasLaunchCoordinator(); launch.Request();
            Require(launch.Advance(status, new(), true, true, false) == LaunchStartDecision.Cancel && !launch.Pending);
        }
        foreach (var state in new[] { new CanvasInteractionState(RecoveryRequired: true), new(Recovering: true), new(SystemTransition: true) })
        {
            var launch = new CanvasLaunchCoordinator(); launch.Request();
            Require(launch.Advance(CanvasSessionStatus.Idle, state, true, true, false) == LaunchStartDecision.Cancel);
        }
        var failed = new CanvasLaunchCoordinator(); failed.Request();
        Require(failed.Advance(CanvasSessionStatus.Idle, new(), true, true, true) == LaunchStartDecision.Cancel);
        failed.Request(); Require(failed.Advance(CanvasSessionStatus.Idle, new(), false, true, false) == LaunchStartDecision.InputUnavailable && !failed.Pending);
    }
    private static void InputBoundaries()
    {
        foreach (var state in new[] { new CanvasInteractionState(Reading: true), new(Busy: true) })
            Require(!state.CanUsePanelCommands && state.CanCaptureInput(true) && !state.CanApplyDesktopTransition(false));
        foreach (var state in new[] { new CanvasInteractionState(Editing: true), new(SystemTransition: true), new(OwnerBlocked: true), new(PanelPending: true), new(Recovering: true), new(RecoveryRequired: true), new(DisplayReady: false) })
            Require(!state.CanCaptureInput(true));
        Require(!new CanvasInteractionState().CanCaptureInput(false) && new CanvasInteractionState().CanCaptureInput(true));
        Require(!new CanvasInteractionState().CanApplyDesktopTransition(true));
    }
    private static void TouchGenerations()
    {
        var modes = new NavigationModeCoordinator(); var old = modes.RequestTouch(); modes.CancelTouch(); var current = modes.RequestTouch();
        Require(!modes.CompleteTouch(old, true) && modes.TouchPending);
        Require(modes.CompleteTouch(current, true) && !modes.TouchPending && !modes.CompleteTouch(current, true));
        var cancelled = modes.RequestTouch(); modes.CancelTouch(); Require(!modes.CompleteTouch(cancelled, true));
        var blocked = modes.RequestTouch(); Require(!modes.CompleteTouch(blocked, false) && !modes.TouchPending);
    }
    private static void ArgumentBoundary()
    {
        var options = LaunchArguments.Parse(["--journal", "fixture & space/recovery.json", "--native-overview"]);
        Require(options.Value("--journal") == "fixture & space/recovery.json" && options.Has("--native-overview"));
        string[][] invalid = [["--touchpad-lab"], ["--smoke-check"], ["--scene-check", "file.json"], ["--settings"], ["--settings", "--journal"], ["--runtime-check", "--runtime-check"]];
        foreach (var args in invalid)
        {
            var rejected = false; try { LaunchArguments.Parse(args); } catch (ArgumentException) { rejected = true; }
            Require(rejected);
        }
    }
}
