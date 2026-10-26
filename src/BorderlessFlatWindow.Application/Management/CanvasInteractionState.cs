namespace BorderlessFlatWindow.Application.Management;

/// <summary>One platform-independent definition of panel, input and lifecycle boundaries.</summary>
public sealed record CanvasInteractionState(
    bool Reading = false, bool Busy = false, bool Editing = false, bool SystemTransition = false,
    bool OwnerBlocked = false, bool PanelPending = false, bool Recovering = false,
    bool RecoveryRequired = false, bool DisplayReady = true)
{
    public bool CanUsePanelCommands => !Reading && !Busy && !Editing && !SystemTransition && !OwnerBlocked;
    public bool GlobalInputBlocked => Editing || SystemTransition || OwnerBlocked || PanelPending || Recovering || RecoveryRequired || !DisplayReady;
    public bool CanCaptureInput(bool running) => running && !GlobalInputBlocked;
    public bool CanApplyDesktopTransition(bool overviewPending) => !Reading && !Busy && !Editing && !overviewPending && !Recovering;
}

public enum LaunchStartDecision { None, Wait, Start, Cancel, InputUnavailable }

/// <summary>A launch request survives transient UI work, but never overrides a pause or recovery.</summary>
public sealed class CanvasLaunchCoordinator
{
    public bool Pending { get; private set; }
    public void Request() => Pending = true;
    public void Cancel() => Pending = false;
    public LaunchStartDecision Advance(CanvasSessionStatus status, CanvasInteractionState state, bool inputReady, bool sessionReady, bool displayFailed)
    {
        if (!Pending) return LaunchStartDecision.None;
        if (status != CanvasSessionStatus.Idle || state.RecoveryRequired || state.Recovering || state.SystemTransition || displayFailed)
        { Pending = false; return LaunchStartDecision.Cancel; }
        if (!inputReady) { Pending = false; return LaunchStartDecision.InputUnavailable; }
        if (!sessionReady || !state.DisplayReady || !state.CanUsePanelCommands) return LaunchStartDecision.Wait;
        Pending = false; return LaunchStartDecision.Start;
    }
}
