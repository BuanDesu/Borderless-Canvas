namespace BorderlessFlatWindow.Windows.Checks;

internal static class OverviewChecks
{
    private sealed class Fixture : Form
    {
        internal Fixture() { Text = "BFW overview focus fixture"; Bounds = new(200, 200, 440, 310); Opacity = 0; }
        protected override bool ShowWithoutActivation => true;
    }
    internal static void Run(Action<string, Action> check)
    {
        check("Overview focus bookmarks validate visibility and cannot outlive their native window", Lifetime);
        check("Independent overview focus markers release without changing foreground or each other", IndependentMarkers);
    }
    private static void Lifetime()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var fixture = new Fixture(); fixture.Show();
        using var bookmark = CaptureReady(fixture.Handle);
        Require(bookmark.CanRestore, "New focus bookmark did not match its window.");
        fixture.Hide(); Require(!bookmark.CanRestore, "Hidden target could restore focus.");
        fixture.Show(); System.Windows.Forms.Application.DoEvents(); Require(bookmark.CanRestore, "Showing the same window lost the marker.");
        fixture.Close(); Require(!bookmark.CanRestore && !bookmark.TryRestore(), "Closed target could restore focus.");
        Require(foreground == WindowEventListener.ForegroundWindow, "Focus guard checks changed the foreground.");
    }
    private static void IndependentMarkers()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var fixture = new Fixture(); fixture.Show();
        using var first = CaptureReady(fixture.Handle);
        using var second = CaptureReady(fixture.Handle);
        first.Dispose(); Require(!first.CanRestore && second.CanRestore, "One bookmark removed another's identity marker.");
        second.Dispose(); Require(!second.CanRestore && foreground == WindowEventListener.ForegroundWindow, "Bookmark release changed focus or remained live.");
    }
    private static ForegroundWindowBookmark CaptureReady(nint handle)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            System.Windows.Forms.Application.DoEvents();
            if (ForegroundWindowBookmark.Capture(handle) is { } bookmark) return bookmark;
            Thread.Sleep(10);
        } while (watch.Elapsed < TimeSpan.FromSeconds(2));
        var catalog = new DesktopWindowCatalog(0); catalog.RefreshWindow(handle);
        var entry = catalog.Current.Entries.Single(item => item.Observation.Handle == handle);
        throw new InvalidOperationException($"Cannot mark own fixture: {entry.Admission.Reason}, visible={entry.Observation.IsVisible}, desktop={entry.Observation.IsOnCurrentDesktop}, cloak={entry.Observation.IsCloaked}, identity={entry.Observation.Identity is not null}.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
