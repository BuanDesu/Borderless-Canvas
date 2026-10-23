using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    internal void CheckInputAvailabilityBoundaries()
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true } || layout is null || GlobalInputBlocked)
            throw new InvalidOperationException("Input availability checks require an idle fixture with valid displays.");
        try
        {
            reading = true; RefreshState();
            if (CanUsePanelCommands || GlobalInputBlocked) throw new InvalidOperationException("A short catalog read revoked global input eligibility.");
            reading = false;
            foreach (var boundary in new[] { "settings", "arrangement", "system", "panel", "recovery" })
            {
                editingSettings = boundary == "settings"; editingArrangement = boundary == "arrangement";
                desktopTransitionPending = boundary == "system"; panelActionPending = boundary == "panel"; externalRecoveryRunning = boundary == "recovery";
                RefreshState();
                if (!GlobalInputBlocked) throw new InvalidOperationException("A real input boundary did not revoke eligibility: " + boundary);
            }
        }
        finally
        {
            reading = editingSettings = editingArrangement = desktopTransitionPending = panelActionPending = externalRecoveryRunning = false;
            RefreshState();
        }
        if (GlobalInputBlocked) throw new InvalidOperationException("Completing a boundary did not restore input eligibility.");
        Console.WriteLine("PASS Global input eligibility survives pumped catalog reads while explicit editing, panel, recovery and system boundaries gate and restore it");
    }
}
