using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Recovery;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows.Checks;

internal static partial class RecoveryChecks
{
    internal static void RunDisplayChanges(Action<string, Action> check)
    {
        check("Recovery verifies an explicit accessible placement when the original is outside every current display", () => OffscreenOriginal(false));
        check("Refused fallback preserves original evidence and marker until an exact native retry succeeds", () => OffscreenOriginal(true));
        check("A partly visible original keeps its exact placement instead of being normalized", PartlyVisibleOriginal);
    }
    private static void PartlyVisibleOriginal()
    {
        using var fixture = new Fixture(); fixture.Open();
        var area = Screen.PrimaryScreen!.WorkingArea;
        fixture.Location = new(area.Left - fixture.Width / 2, area.Top + 80);
        var before = fixture.Bounds; var port = new WindowsRecoveryWindowPort(0);
        using var recovery = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("partly-visible")), port);
        recovery.Register(Identity(fixture)); fixture.Location = new(area.Left + 100, area.Top + 180);
        var report = recovery.RecoverAll();
        Require(report.IsComplete && fixture.Bounds == before && report.Results.Single().Detail == "Original placement and show state verified.",
            "Partly visible original was unnecessarily clamped or could not be restored exactly.");
    }
    private sealed class RejectingFixture : Fixture
    {
        internal bool RejectMoves;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x46 && RejectMoves)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                position.Flags |= 2; Marshal.StructureToPtr(position, message.LParam, false);
            }
            base.WndProc(ref message);
        }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
        { internal nint Window; internal nint InsertAfter; internal int X; internal int Y; internal int Width; internal int Height; internal uint Flags; }
    }
    private static void OffscreenOriginal(bool refuseFirst)
    {
        using var fixture = new RejectingFixture(); fixture.Open();
        var port = new WindowsRecoveryWindowPort(0); var identity = Identity(fixture);
        var captured = port.Capture(identity); var width = captured.PhysicalBounds.Width; var height = captured.PhysicalBounds.Height;
        var displays = new DesktopDisplayCatalog().ReadDisplays();
        var x = checked(displays.Monitors.Max(m => m.Bounds.Right) + 100000);
        var y = checked(displays.Monitors.Max(m => m.Bounds.Bottom) + 100000);
        // Synthetic historical placement represents a removed monitor; no display setting is changed.
        var original = new OriginalWindowState(captured.Placement with { Left = x, Top = y, Right = checked(x + width), Bottom = checked(y + height) },
            new PixelRect(x, y, checked(x + width), checked(y + height)));
        var entry = new RecoveryEntry(Guid.NewGuid(), identity, original, 24681357, true);
        var journal = RecoveryJournal.Empty().WithEntries([entry]); var path = StorageChecks.NewPath("removed-display");
        using (var store = new JsonRecoveryJournalStore(path)) store.Save(journal);
        port.Mark(entry, journal.MarkerName); var foreground = WindowEventListener.ForegroundWindow;
        using var recovery = new RecoverySession(new JsonRecoveryJournalStore(path), port);
        if (refuseFirst)
        {
            fixture.RejectMoves = true; var before = fixture.Bounds;
            var refused = recovery.RecoverAll();
            Require(!refused.IsComplete && refused.Remaining == 1 && refused.Results.Single().Outcome == RecoveryOutcome.Failed
                && recovery.Journal.Entries.Single().Original == original && fixture.Bounds == before && port.Matches(entry, journal.MarkerName),
                "Refused fallback was accepted or destroyed original recovery evidence.");
            fixture.RejectMoves = false;
        }
        var report = recovery.RecoverAll();
        var bounds = fixture.Bounds;
        Require(report.IsComplete && report.Results.Single().Outcome == RecoveryOutcome.Restored,
            "Accessible fallback remained pending: " + report.Results.Single().Detail);
        Require(bounds.Width == width && bounds.Height == height && displays.Monitors.Any(m =>
            bounds.Right > m.WorkArea.Left && bounds.Left < m.WorkArea.Right && bounds.Bottom > m.WorkArea.Top && bounds.Top < m.WorkArea.Bottom),
            "Adjusted recovery changed native dimensions or remained outside usable screens.");
        Require(!port.Matches(entry, journal.MarkerName) && WindowEventListener.ForegroundWindow == foreground,
            "Adjusted recovery leaked ownership or activated a window.");
    }
}
