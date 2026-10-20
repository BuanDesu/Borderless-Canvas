using System.Diagnostics;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class OffscreenChecks
{
    internal static void Run(Action<string, Action> check)
    {
        var layout = new DesktopDisplayCatalog().ReadDisplays();
        var planner = new WindowPlacementPlanner(layout.Primary.WorkArea, layout.Monitors.Select(monitor => monitor.Bounds));
        var notes = new Dictionary<string, object?> { ["monitorCount"] = layout.Monitors.Count,
            ["dpi"] = layout.Primary.EffectiveDpi, ["previewPixelsVerified"] = false,
            ["paintMeasurement"] = "Transparent owned fixture; paint counts are not DWM pixel freshness." };
        using var source = new AnimatedFixture(); source.Open();
        var original = source.Bounds;
        var port = new WindowsRecoveryWindowPort(0);
        using var session = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("offscreen")), port);
        var window = FixtureReadiness.Wait(source.Handle);
        var entry = session.Register(window.Identity);
        var mover = new RegisteredWindowMover();
        WindowPlacementPlan? parked = null;

        check("Registered native parking stays outside every physical display without hiding, minimizing or resizing", () =>
        {
            Require(planner.TryPlan(new(new PointD(1e12, 1e12), new SizeD(source.Width, source.Height)), PointD.Zero, out parked)
                && parked is { IsParked: true }, "No bounded parking plan.");
            var foreground = WindowEventListener.ForegroundWindow;
            var receipt = mover.RequestMove(session, entry.Id, parked!.PhysicalBounds);
            Require(receipt.Accepted, receipt.Detail);
            PumpUntil(() => source.Left == parked.PhysicalBounds.Left && source.Top == parked.PhysicalBounds.Top);
            Require(source.Size == original.Size && source.Visible && source.WindowState == FormWindowState.Normal
                && layout.Monitors.All(monitor => !WindowPlacementPlanner.Intersects(monitor.Bounds, Pixels(source.Bounds))), "Native parking leaked or changed state.");
            Require(WindowEventListener.ForegroundWindow == foreground, "Parking changed foreground.");
            notes["parkedBounds"] = parked.PhysicalBounds;
        });
        check("Timers and paints are measured while parked; continuous offscreen rendering is not assumed", () =>
        {
            var ticks = source.Ticks; var paints = source.Paints;
            PumpFor(TimeSpan.FromMilliseconds(240));
            notes["parkedTimerDelta"] = source.Ticks - ticks; notes["parkedPaintDelta"] = source.Paints - paints;
            Require(source.Ticks > ticks, "Fixture timer stopped.");
        });
        check("DWM preview can query and update a parked native source and releases its thumbnail", () =>
        {
            using var destination = new AnimatedFixture(); destination.Open();
            using var preview = DwmWindowPreview.TryCreate(destination.Handle, source.Handle);
            notes["parkedPreviewAvailable"] = preview is not null;
            Require(preview is not null, "DWM registration unavailable on this desktop; title fallback required.");
            var size = preview.GetSourceSize();
            Require(size is { Width: > 0, Height: > 0 } && preview.Update(new(0, 0, 200, 140)), "DWM query/update failed.");
            notes["previewSourceWidth"] = size.Value.Width; notes["previewSourceHeight"] = size.Value.Height;
            preview.Dispose();
            var rejected = false;
            try { preview.GetSourceSize(); } catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "Disposed preview remained usable.");
        });
        check("Owned popup positions are observed independently of a parked owner", () =>
        {
            using var popup = new AnimatedFixture { Text = "BFW offscreen popup", Bounds = new(120, 120, 240, 160) };
            popup.Show(source); PumpFor(TimeSpan.FromMilliseconds(100));
            var observed = new DesktopWindowCatalog(0).Inspect().Entries.Single(item => item.Observation.Handle == popup.Handle);
            Require(observed.Observation.OwnerHandle == source.Handle, "Popup owner missing.");
            notes["popupBounds"] = Pixels(popup.Bounds);
            notes["popupIntersectsScreen"] = layout.Monitors.Any(monitor => WindowPlacementPlanner.Intersects(monitor.Bounds, Pixels(popup.Bounds)));
            notes["popupPolicy"] = "Pause and restore management when an ordinary owned dialog appears; never leave a modal owner parked.";
        });
        check("Empty-region clipping changes native drawing area and can restore a nonrectangular original region", () =>
        {
            using var fixture = new AnimatedFixture(); fixture.Open();
            using var shape = new Region(new Rectangle(0, 0, 180, 100)); shape.Union(new Rectangle(80, 70, 180, 120));
            fixture.Region = shape.Clone();
            var saved = CreateRectRgn(0, 0, 0, 0); var comparison = CreateRectRgn(0, 0, 0, 0);
            Require(saved != 0 && comparison != 0, "Cannot allocate test regions.");
            var transferred = false;
            try
            {
                Require(GetWindowRgn(fixture.Handle, saved) > 0 && GetWindowRgn(fixture.Handle, comparison) > 0, "Cannot save original region.");
                var empty = CreateRectRgn(0, 0, 0, 0);
                if (SetWindowRgn(fixture.Handle, empty, true) == 0) { DeleteObject(empty); throw new InvalidOperationException("Cannot set clipping region."); }
                var actual = CreateRectRgn(0, 0, 0, 0);
                try { Require(GetWindowRgn(fixture.Handle, actual) == 1, "Clip is not NULLREGION."); }
                finally { DeleteObject(actual); }
                var ticks = fixture.Ticks; var paints = fixture.Paints; PumpFor(TimeSpan.FromMilliseconds(240));
                notes["clippedTimerDelta"] = fixture.Ticks - ticks; notes["clippedPaintDelta"] = fixture.Paints - paints;
                notes["clipVisibleFlag"] = fixture.Visible;
            }
            finally
            {
                transferred = SetWindowRgn(fixture.Handle, saved, true) != 0;
                var restored = CreateRectRgn(0, 0, 0, 0);
                try { Require(transferred && GetWindowRgn(fixture.Handle, restored) > 0 && EqualRgn(restored, comparison), "Original shape was not restored."); }
                finally { DeleteObject(restored); DeleteObject(comparison); if (!transferred) DeleteObject(saved); }
            }
            notes["clipProductionUse"] = false; // Current crash journal does not serialize arbitrary window regions.
        });
        check("Parking restores the original native bounds through the recovery session", () =>
        {
            var report = session.RecoverAll();
            Require(report.IsComplete && source.Bounds == original, "Parked fixture did not recover.");
        });
        check("Native movement refuses an unregistered ID and a changed window size", () =>
        {
            Require(!mover.RequestMove(session, Guid.NewGuid(), Pixels(original)).Accepted, "Unknown entry moved.");
            var registered = session.Register(window.Identity);
            Require(!mover.RequestMove(session, registered.Id, new(100, 100, 200, 200)).Accepted, "Resize was accepted by a translation-only mover.");
        });

        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/offscreen-probe.json"));
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(notes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class AnimatedFixture : Form
    {
        private readonly System.Windows.Forms.Timer animation = new() { Interval = 30 };
        internal int Ticks;
        internal int Paints;
        internal AnimatedFixture()
        {
            Text = "BFW offscreen fixture"; Opacity = 0; Bounds = new(180, 160, 440, 310);
            animation.Tick += (_, _) => { Ticks++; Invalidate(); }; animation.Start();
        }
        protected override bool ShowWithoutActivation => true;
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Paints++; e.Graphics.Clear(Ticks % 2 == 0 ? Color.Teal : Color.Orange); }
        internal void Open()
        {
            Show(); FixtureReadiness.Wait(Handle);
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
    private static PixelRect Pixels(Rectangle rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    private static void PumpUntil(Func<bool> completed)
    {
        var watch = Stopwatch.StartNew();
        while (!completed() && watch.Elapsed < TimeSpan.FromSeconds(3)) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Require(completed(), "Timed out waiting for a native fixture.");
    }
    private static void PumpFor(TimeSpan duration)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < duration) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint region);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualRgn(nint first, nint second);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
