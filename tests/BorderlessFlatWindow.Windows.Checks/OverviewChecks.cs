using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;

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
        check("Native overview preview uses the actual top-level host and releases on search, disable and close", PreviewLifecycle);
        check("Native overview preview rejects a changed process identity and invalidates a closed source", PreviewIdentity);
        check("Native previews crop workspace slices, animate opacity, and retain parked and duplicate source views", PreviewSlices);
        check("Desktop crop eligibility rejects stale, hidden, moved and layered sources", CropIdentity);
        check("Desktop crop eligibility detects an overlapping window without changing foreground", CropOcclusion);
        check("Batched native preview queries reject hidden and stale sources without cached eligibility", PreviewBatch);
    }
    private static void CropIdentity()
    {
        using var source = new Fixture(); source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = OwnIdentity(source);
        var rect = new PixelRect(source.Left, source.Top, source.Right, source.Bottom);
        Require(!DesktopCropEligibility.IsUnobscured(identity, rect), "Layered source was treated as opaque.");
        Require(!DesktopCropEligibility.IsUnobscured(identity with { ProcessCreationFileTime = identity.ProcessCreationFileTime + 1 }, rect), "Stale source accepted.");
        source.Opacity = 1; source.Left += 20;
        Require(!DesktopCropEligibility.IsUnobscured(identity, rect), "Moved source accepted at old crop.");
        source.Hide(); Require(!DesktopCropEligibility.IsUnobscured(identity, rect), "Hidden source accepted.");
        source.Close(); Require(!DesktopCropEligibility.IsUnobscured(identity, rect), "Destroyed source accepted.");
    }
    private static void CropOcclusion()
    {
        // Fixture deliberately opts into layered-window transparency in its constructor.
        // Use a plain opaque form for the positive eligibility case.
        using var source = new Form { ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = new(80, 80, 240, 160) }; source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = OwnIdentity(source);
        var rect = new PixelRect(source.Left, source.Top, source.Right, source.Bottom);
        var foreground = WindowEventListener.ForegroundWindow;
        var eligibility = DesktopCropEligibility.Evaluate(identity, rect, rect);
        Require(eligibility == DesktopCropStatus.Ready, "Unobscured own source rejected: " + eligibility);
        Require(WindowEventListener.ForegroundWindow == foreground, "Eligibility changed foreground.");
        using var blocker = new Form { ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = new(100, 100, 100, 80) }; blocker.Show();
        using var blockerReady = CaptureReady(blocker.Handle);
        Require(source.Bounds.IntersectsWith(blocker.Bounds), "Fixture placement did not create an overlap.");
        foreground = WindowEventListener.ForegroundWindow;
        Require(!DesktopCropEligibility.IsUnobscured(identity, rect), "Occluding own window ignored.");
        Require(WindowEventListener.ForegroundWindow == foreground, "Eligibility check changed foreground.");
        blocker.Hide(); source.BringToFront(); System.Windows.Forms.Application.DoEvents(); foreground = WindowEventListener.ForegroundWindow;
        eligibility = DesktopCropEligibility.Evaluate(identity, rect, rect);
        Require(eligibility == DesktopCropStatus.Ready, "Hidden blocker retained: " + eligibility);
        Require(WindowEventListener.ForegroundWindow == foreground, "Eligibility check changed foreground after hide.");
    }
    private static NativeWindowIdentity OwnIdentity(Form source)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new(source.Handle, (uint)Environment.ProcessId, process.StartTime.ToFileTimeUtc());
    }
    private static void PreviewLifecycle()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var host = new Fixture(); host.Show();
        using var child = new Form { TopLevel = false }; host.Controls.Add(child); child.Show();
        using var source = new Fixture(); source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = new DesktopWindowCatalog(0).GetWindows().Single(window => window.Handle == source.Handle).Identity;
        var id = Guid.NewGuid(); var card = new PreviewCard(id, identity, new PixelRect(20, 30, 220, 180), null);
        var previews = new OverviewPreviewSession(new DwmOverviewPreviewPort(child.TopLevelControl!.Handle));
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                System.Windows.Forms.Application.DoEvents(); previews.RetryFailed(); previews.Synchronize([card], id, true);
                if (previews.States[id] == PreviewState.Preview) break;
                Thread.Sleep(20);
            } while (watch.Elapsed < TimeSpan.FromSeconds(2));
            Require(previews.States[id] == PreviewState.Preview && previews.ResourceCount == 1, "Embedded host thumbnail failed.");
            previews.Synchronize([card with { Fallback = PreviewState.Unmatched }], null, true);
            Require(previews.ResourceCount == 0, "Search kept native resource.");
            previews.Synchronize([card], id, true); previews.Synchronize([card], id, false);
            Require(previews.ResourceCount == 0 && previews.States[id] == PreviewState.Disabled, "Disable kept native resource.");
            previews.Synchronize([card], id, true); previews.Dispose(); previews.Dispose();
            Require(previews.ResourceCount == 0 && foreground == WindowEventListener.ForegroundWindow, "Resource close changed foreground or leaked.");
        }
        finally { previews.Dispose(); }
    }
    private static void PreviewIdentity()
    {
        using var host = new Fixture(); host.Show(); using var source = new Fixture(); source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = new DesktopWindowCatalog(0).GetWindows().Single(window => window.Handle == source.Handle).Identity;
        var port = new DwmOverviewPreviewPort(host.Handle);
        using var wrong = port.Create(identity with { ProcessCreationFileTime = identity.ProcessCreationFileTime + 1 });
        Require(wrong is null, "Stale identity admitted.");
        using var preview = port.Create(identity);
        Require(preview is not null && preview.IsCurrent && preview.SourceSize is { IsPositive: true }, "Native preview unavailable.");
        source.Close();
        Require(!preview!.IsCurrent && !preview.Update(new(0, 0, 100, 100)), "Closed source remained usable.");
    }
    private static void PreviewBatch()
    {
        using var host = new Fixture(); host.Show(); using var source = new Fixture(); source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = OwnIdentity(source);
        var port = new DwmOverviewPreviewPort(host.Handle);
        using (port.BeginBatch())
        {
            using var first = port.Create(identity); Require(first is not null, "Valid batched source unavailable.");
            using var stale = port.Create(identity with { ProcessCreationFileTime = identity.ProcessCreationFileTime + 1 });
            Require(stale is null, "Batched query cached identity.");
            source.Hide(); using var hidden = port.Create(identity); Require(hidden is null, "Batched query cached visibility.");
            source.Show(); using (port.BeginBatch()) { using var nested = port.Create(identity); Require(nested is not null, "Nested query scope failed."); }
            using var resumed = port.Create(identity); Require(resumed is not null, "Outer query scope was not restored.");
            source.Close(); Require(!first!.IsCurrent, "Batch kept a destroyed source usable.");
        }
        using var destroyed = port.Create(identity); Require(destroyed is null, "Closed source revived after batch release.");
    }
    private static void PreviewSlices()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var host = new Fixture(); host.Show(); using var source = new Fixture(); source.Show();
        using var ready = CaptureReady(source.Handle);
        var identity = new DesktopWindowCatalog(0).GetWindows().Single(window => window.Handle == source.Handle).Identity;
        using var session = new OverviewPreviewSession(new DwmOverviewPreviewPort(host.Handle));
        source.Location = new(30000, 30000); System.Windows.Forms.Application.DoEvents();
        var cards = Enumerable.Range(0, 24).Select(i => new PreviewCard(Guid.NewGuid(), identity, new PixelRect(i * 8, 10, i * 8 + 50, 60), null,
            new RectD(new(.25, .25), new(.5, .5)))).ToArray();
        session.Synchronize(cards, null, true, 0);
        Require(session.ResourceCount == 24 && session.States.Values.All(s => s == PreviewState.Preview), "Parked or additional workspace registrations were filtered.");
        session.Animate(cards, 127); session.Synchronize(cards.Select(c => c with { SourceFraction = null }), null, true);
        Require(session.States.Values.All(s => s == PreviewState.Preview), "Crop reset or opacity transition failed.");
        Require(foreground == WindowEventListener.ForegroundWindow, "Workspace preview activated a source.");
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
