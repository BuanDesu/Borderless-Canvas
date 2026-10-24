using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    internal void CheckOwnerInputWake()
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true } host || globalInput is not null || session?.IsRunning == true)
            throw new InvalidOperationException("Owner wake checks require an inactive fixture.");
        Visible = false; if (IsHandleCreated) DestroyHandle();
        _ = host.Handle;
        if (IsHandleCreated) throw new InvalidOperationException("Hidden fixture child unexpectedly acquired a handle.");
        var before = deliveredInputWakes;
        Task.Run(CreateInputWake()).GetAwaiter().GetResult();
        System.Windows.Forms.Application.DoEvents();
        if (deliveredInputWakes != before + 1) throw new InvalidOperationException("An uncreated canvas child lost its owner's input wake.");
        Visible = true;
        Console.WriteLine("PASS Input wakes reach the owner while the canvas child has no HWND, without a settings round trip");
    }
    internal void CheckInputAvailabilityBoundaries()
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true } || layout is null || GlobalInputBlocked)
            throw new InvalidOperationException("Input availability checks require an idle fixture with valid displays.");
        try
        {
            reading = true; RefreshState();
            if (CanUsePanelCommands || GlobalInputBlocked) throw new InvalidOperationException("A short catalog read revoked global input eligibility.");
            reading = false;
            foreach (var boundary in new[] { "settings", "arrangement", "system", "panel", "recovery", "pending" })
            {
                editingSettings = boundary == "settings"; editingArrangement = boundary == "arrangement";
                desktopTransitionPending = boundary == "system"; panelActionPending = boundary == "panel"; externalRecoveryRunning = boundary == "recovery";
                recoveryAttention = boundary == "pending";
                RefreshState();
                if (!GlobalInputBlocked) throw new InvalidOperationException("A real input boundary did not revoke eligibility: " + boundary);
            }
        }
        finally
        {
            reading = editingSettings = editingArrangement = desktopTransitionPending = panelActionPending = externalRecoveryRunning = recoveryAttention = false;
            RefreshState();
        }
        if (GlobalInputBlocked) throw new InvalidOperationException("Completing a boundary did not restore input eligibility.");
        Console.WriteLine("PASS Global input eligibility survives pumped catalog reads while explicit editing, panel, recovery and system boundaries gate and restore it");
    }
    internal bool RecoveryActionForCheck => recoveryAttention && start.Text == "恢复" && start.AccessibleName == "恢复窗口" && !CanCaptureGlobalInput;
    internal void CheckTouchNavigationBoundaries()
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true } || session?.IsRunning == true || touchNavigation is not null)
            throw new InvalidOperationException("Touch lifecycle checks require an inactive fixture.");
        foreach (var boundary in new[] { "system", "panel", "recovery", "pause" })
        {
            var host = new TouchNavigationHost(); touchNavigation = host;
            QueueTouchInput(new(BorderlessFlatWindow.Application.Navigation.NavigationInputKind.PanStart, new(100, 100)));
            QueueTouchInput(new(BorderlessFlatWindow.Application.Navigation.NavigationInputKind.PanMove, new(200, 200)));
            if (!GlobalInputBlocked || !CaptureRuntimeInformation().Contains("触控导航"))
                throw new InvalidOperationException("Touch navigation failed to gate global mouse routing or identify itself.");
            if (boundary == "system") SetDesktopTransitionPending(true);
            else if (boundary == "panel") SetPanelActionPending(true);
            else if (boundary == "recovery") SetExternalRecoveryRunning(true);
            else Pause();
            if (touchNavigation is not null || !host.IsClosed || touchInputs.Count != 0 || touchInputOverflow)
                throw new InvalidOperationException("A touch surface or deferred contact survived its safety boundary: " + boundary);
            SetDesktopTransitionPending(false); SetPanelActionPending(false); SetExternalRecoveryRunning(false);
        }
        Console.WriteLine("PASS Touch navigation gates global routing and releases invisible fixtures and queued contacts on panel, recovery, pause and system boundaries");
    }
}
