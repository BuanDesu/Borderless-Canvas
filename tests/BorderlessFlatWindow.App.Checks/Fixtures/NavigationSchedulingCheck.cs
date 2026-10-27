using System.Diagnostics;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal static void CheckNavigationScheduling(string directory)
    {
        Directory.CreateDirectory(directory);
        using var form = new MainForm(new SchedulingCatalog(), showWithoutActivation: true,
            journalPath: Path.Combine(directory, "recovery.json"), bookmarksPath: Path.Combine(directory, "bookmarks.json"),
            settingsPath: Path.Combine(directory, "settings.json")) { Opacity = 0, ShowInTaskbar = false };
        _ = form.Handle;
        form.management.CheckNavigationScheduling();
        // Only this transparent fixture is shown. Its catalog remains synthetic.
        form.Show(); System.Windows.Forms.Application.DoEvents();
        form.listener?.Dispose(); form.listener = null; form.eventTimer.Stop();
        form.CheckDeferredDiagnostics();
    }

    private void CheckDeferredDiagnostics()
    {
        tabs.SelectedIndex = 2;
        snapshot = new PreviewWindowCatalog().Current;
        RenderSnapshot();
        if (windowList.Items.Count != 4) throw new InvalidOperationException("Visible catalog did not render its synthetic snapshot.");
        var retained = windowList.Items[0];
        RenderSnapshot();
        snapshot = snapshot with { CapturedAt = snapshot.CapturedAt.AddSeconds(1), Entries = snapshot.Entries.ToArray() };
        RenderSnapshot();
        if (!ReferenceEquals(retained, windowList.Items[0])) throw new InvalidOperationException("An unchanged catalog rebuilt list items.");
        windowList.Items[0].Selected = true;
        Hide();
        snapshot = snapshot with { Entries = snapshot.Entries.Take(2).Select(entry => entry with { Observation = entry.Observation with { Title = "Updated synthetic" } }).ToArray() };
        UpdateLiveMap(); RenderSnapshot();
        if (!liveMapDirty || windowList.Items.Count != 4 || !ReferenceEquals(retained, windowList.Items[0]))
            throw new InvalidOperationException("Hidden diagnostics performed presentation work.");
        Show(); System.Windows.Forms.Application.DoEvents();
        if (windowList.Items.Count != 2 || windowList.Items[0].Text != "Updated synthetic" || !windowList.Items[0].Selected)
            throw new InvalidOperationException("Restored catalog missed the latest hidden snapshot.");
        WindowState = FormWindowState.Minimized;
        snapshot = snapshot with { Entries = snapshot.Entries.Select(entry => entry with { Observation = entry.Observation with { Title = "Restored synthetic" } }).ToArray() };
        UpdateLiveMap(); RenderSnapshot();
        if (windowList.Items[0].Text != "Updated synthetic") throw new InvalidOperationException("Minimized catalog rendered eagerly.");
        WindowState = FormWindowState.Normal; System.Windows.Forms.Application.DoEvents();
        if (windowList.Items[0].Text != "Restored synthetic") throw new InvalidOperationException("Restored window missed minimized diagnostic work.");
        // An unselected nested diagnostic tab also defers work and catches up on entry.
        PrepareRuntimeChromeSmoke(); PrepareRuntimePageSmoke(1, 0);
        UpdateLiveMap();
        if (!liveMapDirty) throw new InvalidOperationException("An unselected map synchronized its presentation.");
        PrepareRuntimePageSmoke(1, 2); System.Windows.Forms.Application.DoEvents();
        if (liveMapDirty || liveWorkspace.Windows.Count != 2) throw new InvalidOperationException("Selected nested map did not catch up.");
        PrepareRuntimePageSmoke(0);
        snapshot = snapshot with { Entries = snapshot.Entries.Take(1).ToArray() };
        UpdateLiveMap(); RenderSnapshot();
        if (liveWorkspace.Windows.Count != 2 || windowList.Items.Count != 2) throw new InvalidOperationException("Unselected diagnostics changed their presentation.");
        PrepareRuntimePageSmoke(1, 0); System.Windows.Forms.Application.DoEvents();
        if (windowList.Items.Count != 1) throw new InvalidOperationException("Nested catalog did not catch up.");
        PrepareRuntimePageSmoke(1, 2); System.Windows.Forms.Application.DoEvents();
        if (liveWorkspace.Windows.Count != 1) throw new InvalidOperationException("Nested map retained stale windows.");
        Console.WriteLine("PASS Hidden, restored and nested diagnostics defer presentation, retain unchanged rows and catch up with the latest snapshot");
    }
}

internal sealed partial class ManagedCanvasControl
{
    internal void CheckNavigationScheduling()
    {
        if (globalInput is not null || session?.IsRunning == true) throw new InvalidOperationException("Isolated fixture required.");
        automaticAdmission.Checked = false;
        session!.Start([], DateTimeOffset.UtcNow);
        lastSnapshot = new(DateTimeOffset.UtcNow, []);
        RefreshState();
        var refreshes = 0;
        void Refreshed() => refreshes++;
        StatusChanged += Refreshed;
        for (var i = 0; i < 100; i++) NavigationFrame();
        StatusChanged -= Refreshed;
        if (refreshes != 0 || navigationTimer.Enabled) throw new InvalidOperationException("Unchanged frames rebuilt UI or kept polling.");

        var platform = new SchedulingPlatform();
        globalInput = new(CreateInputWake(), inputOptions, null, platform);
        ConfigurePointerScreens(); RefreshState(); globalInput.SynchronizeRoutingCheck(); NavigationFrame();
        inputHealthTimer.Start();
        foreach (var touch in new[] { false, true })
        {
            if (touch)
            {
                touchNavigation = new();
                if (!globalInput.SetTouchNavigation(true)) throw new InvalidOperationException("Synthetic touch mode failed.");
            }
            globalInput.SynchronizeRoutingCheck(); NavigationFrame();
            for (var i = 0; i < 12; i++)
            {
                platform.Time += 250; MaintainInputHealth(); globalInput.TimerRoutingCheck(); NavigationFrame();
                if (!globalInput.State.Available || navigationTimer.Enabled) throw new InvalidOperationException("Idle input timed out or kept the high-frequency pump alive.");
            }
            var camera = session.Workspace.Camera.Position;
            var wakes = deliveredInputWakes;
            if (!globalInput.MouseRoutingCheck(0x20A, new(200, 200), 120)) throw new InvalidOperationException("First idle wheel was not routed.");
            System.Windows.Forms.Application.DoEvents();
            if (deliveredInputWakes <= wakes || !navigationTimer.Enabled) throw new InvalidOperationException("First idle input did not wake the UI.");
            NavigationFrame();
            if (session.Workspace.Camera.Position == camera || !navigationTimer.Enabled) throw new InvalidOperationException("First idle input was lost or wheel settling stopped early.");
            gestures!.Tick(DateTimeOffset.UtcNow.AddMilliseconds(300)); NavigationFrame();
            if (navigationTimer.Enabled || gestures.HasGesture) throw new InvalidOperationException("Wheel gesture did not settle and stop.");
            if (touch) CloseTouchNavigation(discard: true);
        }
        // A held direct gesture must retain its origin without requiring idle frames.
        QueueTouchInput(new(NavigationInputKind.PanStart, new(100, 100))); NavigationFrame();
        if (!gestures!.HasGesture || navigationTimer.Enabled) throw new InvalidOperationException("A stationary contact polled or lost its gesture.");
        var origin = session.Workspace.Camera.Position;
        QueueTouchInput(new(NavigationInputKind.PanMove, new(80, 70))); NavigationFrame();
        QueueTouchInput(new(NavigationInputKind.PanMove, new(60, 40))); NavigationFrame();
        QueueTouchInput(new(NavigationInputKind.PanEnd, new(50, 30))); NavigationFrame();
        if (gestures.HasGesture || navigationTimer.Enabled || session.Workspace.Camera.Position != origin + new PointD(50, 70)
            || session.Workspace.PreviousView != origin) throw new InvalidOperationException("Continuous pan/end changed displacement or history.");

        // Notifications arriving during a short read remain unconsumed and retry.
        globalInput.SynchronizeRoutingCheck(); NavigationFrame();
        reading = true; globalInput.MouseRoutingCheck(0x20A, new(200, 200), 120);
        System.Windows.Forms.Application.DoEvents(); NavigationFrame();
        if (!globalInput.HasPendingUiNotification) throw new InvalidOperationException("A busy UI acknowledged undrained input.");
        navigationTimer.Stop(); platform.Time += 100; globalInput.TimerRoutingCheck();
        System.Windows.Forms.Application.DoEvents();
        if (!navigationTimer.Enabled) throw new InvalidOperationException("A stopped timer lost notification retry.");
        reading = false; NavigationFrame(); gestures.End(DateTimeOffset.UtcNow); NavigationFrame();
        if (globalInput.HasPendingUiNotification) throw new InvalidOperationException("Resumed input failed to drain.");

        // Timer animation continues through its last frame; HUD uses its own deadline.
        session.NavigateWorkspace(new(700, 400), DateTimeOffset.UtcNow.AddMilliseconds(-100), 0);
        NavigationFrame();
        if (!navigationTimer.Enabled || !session.HasCameraTransition) throw new InvalidOperationException("Camera transition stopped early.");
        session.AdvanceCameraTransition(DateTimeOffset.UtcNow.AddMilliseconds(200)); NavigationFrame();
        if (navigationTimer.Enabled || session.HasCameraTransition || session.Workspace.Camera.Position != new PointD(700, 400))
            throw new InvalidOperationException("Final camera transition failed to settle.");
        lastNavigationFeedback = Stopwatch.GetTimestamp() - Stopwatch.Frequency; UpdateNavigationFeedback();
        if (hud?.Visible == true || feedbackTimer.Enabled) throw new InvalidOperationException("HUD outlived its deadline.");

        refreshes = 0; StatusChanged += Refreshed;
        QueueTouchInput(new(NavigationInputKind.Overview, PointD.Zero)); NavigationFrame();
        StatusChanged -= Refreshed;
        if (!HasOverviewWork || refreshes != 1) throw new InvalidOperationException("An input-driven overview request failed to refresh command/tray eligibility.");
        CancelOverview(); globalInput.SynchronizeRoutingCheck();
        System.Windows.Forms.Application.DoEvents(); NavigationFrame();

        // A genuinely blocked UI must still trip the real worker's watchdog.
        if (!globalInput.SetTouchNavigation(true)) throw new InvalidOperationException("Watchdog fixture could not enter touch.");
        globalInput.Drain(); platform.Time += 1801; globalInput.TimerRoutingCheck();
        if (globalInput.State.Available || globalInput.State.Touch) throw new InvalidOperationException("UI watchdog failed open.");
        NavigationFrame();
        if (session.IsRunning) throw new InvalidOperationException("Watchdog fault did not pause and recover.");
        globalInput.Dispose(); globalInput = null; inputHealthTimer.Stop();
        CheckPendingScheduling();
        Console.WriteLine("PASS Idle desktop/touch wakes, stationary and continuous gestures, quiet wheel completion, busy-read retries, camera/HUD deadlines and the real watchdog retain their semantics");
    }

    private void CheckPendingScheduling()
    {
        session!.Dispose();
        var port = new SchedulingWindows(layout!.CanvasWorkArea);
        session = new(() => new(new SchedulingStore(), port), port, layout!);
        lastSnapshot = port.Current;
        if (!session.Start(port.Current.Entries, DateTimeOffset.UtcNow)) throw new InvalidOperationException(session.Message);
        session.Observe(port.Current, 0, DateTimeOffset.UtcNow); RefreshState();
        QueueTouchInput(new(NavigationInputKind.CameraDelta, new(25, 30))); NavigationFrame();
        gestures!.End(DateTimeOffset.UtcNow); NavigationFrame();
        if (!session.HasPending || !verify.Enabled || navigationTimer.Enabled) throw new InvalidOperationException("Pending confirmation lost its independent verifier.");
        var fixtureCatalog = (SchedulingCatalog)catalog;
        port.ApplyEcho(); fixtureCatalog.Snapshot = port.Current;
        VerifyPending();
        if (session.HasPending || verify.Enabled || session.ManagedCount != 1) throw new InvalidOperationException("Pending echo was not confirmed independently.");
        QueueTouchInput(new(NavigationInputKind.CameraDelta, new(10, 10))); NavigationFrame();
        gestures.End(DateTimeOffset.UtcNow); NavigationFrame();
        if (!session.HasPending || !verify.Enabled) throw new InvalidOperationException("Second movement lost verification.");
        port.Closed = true; fixtureCatalog.Snapshot = new(DateTimeOffset.UtcNow, []);
        VerifyPending();
        if (session.HasPending || verify.Enabled || session.ManagedCount != 0) throw new InvalidOperationException("Independent pending verification did not reconcile closure.");
        Pause();
        Console.WriteLine("PASS Pending moves keep the 40ms verifier after high-frequency work stops, confirm synthetic echoes and reconcile closure");
    }

    private sealed class SchedulingPlatform : INavigationInputPlatform
    {
        internal long Time = 10000;
        public long TickCount => Interlocked.Read(ref Time);
        public nint Foreground => 0;
        public bool MenuActive => false;
        public bool Down(int key) => false;
        public bool IsDesktop(PointD point) => true;
        public bool GetPointer(out PointD point) { point = new(200, 200); return true; }
        public PixelRect VirtualScreen => new(0, 0, 1920, 1080);
        public PixelRect PrimaryScreen => VirtualScreen;
        public bool ReadClip(out PixelRect rectangle) { rectangle = VirtualScreen; return true; }
        public bool WriteClip(PixelRect? rectangle) => throw new InvalidOperationException("Fixture must not clip the cursor.");
        public bool SetPointer(PointD point) => throw new InvalidOperationException("Fixture must not move the cursor.");
    }
    private sealed class SchedulingStore : IRecoveryJournalStore
    {
        private RecoveryJournal? journal;
        public RecoveryJournal? Load() => journal;
        public void Save(RecoveryJournal value) => journal = value;
        public void Dispose() { }
    }
    private sealed class SchedulingWindows(PixelRect area) : IRecoveryWindowPort, IWindowMotionPort
    {
        internal WindowCatalogSnapshot Current = CreateSnapshot(area);
        private static WindowCatalogSnapshot CreateSnapshot(PixelRect area)
        {
            var snapshot = new PreviewWindowCatalog().Current; var entry = snapshot.Entries[0];
            return snapshot with { Entries = [entry with { Observation = entry.Observation with { Bounds = new(area.Position + new PointD(100, 100), new(300, 200)) } }] };
        }
        internal bool Closed;
        private PixelRect? desired;
        internal void ApplyEcho()
        {
            var entry = Current.Entries[0];
            Current = Current with { Entries = [entry with { Observation = entry.Observation with { Bounds = desired!.Value.ToRectD() } }] };
        }
        private readonly HashSet<Guid> markers = [];
        public OriginalWindowState Capture(NativeWindowIdentity identity)
        {
            var bounds = Current.Entries[0].Observation.Bounds!.Value;
            return new(new(0, 1, -1, -1, -1, -1, (int)bounds.Position.X, (int)bounds.Position.Y, (int)bounds.Right, (int)bounds.Bottom),
                new((int)bounds.Position.X, (int)bounds.Position.Y, (int)bounds.Right, (int)bounds.Bottom));
        }
        public void Mark(RecoveryEntry entry, string name) => markers.Add(entry.Id);
        public bool Matches(RecoveryEntry entry, string name) => !Closed && markers.Contains(entry.Id);
        public void RemoveMarker(RecoveryEntry entry, string name) => markers.Remove(entry.Id);
        public RecoveryResult Restore(RecoveryEntry entry, string name) { markers.Remove(entry.Id); return new(entry.Id, RecoveryOutcome.Restored, "Synthetic restore"); }
        public MotionBatchResult RequestBatch(RecoverySession recovery, IReadOnlyList<WindowTranslation> moves)
        {
            if (moves.Any(move => !recovery.CanMove(move.RecoveryId))) throw new InvalidOperationException("Synthetic move lacked durable ownership.");
            desired = moves.LastOrDefault()?.Target;
            return new(true, "Synthetic delayed echo");
        }
        public bool RequestActivation(RecoverySession recovery, Guid id) => false;
    }
}

internal sealed class SchedulingCatalog : ITrackedDesktopWindowCatalog
{
    internal WindowCatalogSnapshot Snapshot = new(DateTimeOffset.UtcNow, []);
    public WindowCatalogSnapshot Current => Snapshot;
    public WindowCatalogSnapshot Inspect() => Snapshot;
    public IReadOnlyList<DesktopWindowInfo> GetWindows() => Snapshot.AdmittedWindows;
    public void RefreshWindow(nint handle) { }
    public void ForgetWindow(nint handle) { }
}
