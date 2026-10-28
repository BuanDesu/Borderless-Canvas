using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal static void CheckWorkspaceIntegration(string directory)
    {
        Directory.CreateDirectory(directory);
        var catalog = new SchedulingCatalog();
        using var form = new MainForm(catalog, showWithoutActivation: true, journalPath: Path.Combine(directory, "recovery.json"),
            bookmarksPath: Path.Combine(directory, "bookmarks.json"), settingsPath: Path.Combine(directory, "settings.json")) { Opacity = 0, ShowInTaskbar = false };
        _ = form.Handle; form.management.CheckWorkspaceIntegration(catalog);
    }
}

internal sealed partial class ManagedCanvasControl
{
    internal void CheckWorkspaceIntegration(SchedulingCatalog fixtureCatalog)
    {
        if (globalInput is not null || session?.IsRunning == true || layout is null) throw new InvalidOperationException("Synthetic fixture required.");
        automaticAdmission.Checked = false; session!.Dispose();
        var port = new WorkspaceUiWindows(this, fixtureCatalog);
        session = new(() => new(new WorkspaceUiStore(), port), port, layout, bookmarks.Collection);
        var primary = layout.Primary.WorkArea;
        port.Add("A", new(new(primary.Left + 100, primary.Top + 100), new(300, 200)));
        port.Add("B", new(new(primary.Right - 20, primary.Top + 120), new(220, 200)));
        port.Add("Outside", new(new(layout.CanvasWorkArea.Right + 5000, primary.Top), new(300, 200)));
        if (!session.Start(fixtureCatalog.Current.Entries, DateTimeOffset.UtcNow)) throw new InvalidOperationException(session.Message);
        Observe(fixtureCatalog.Current, 0);
        var snapshot = CaptureWorkspace(PointD.Zero);
        if (snapshot.Windows.Count != 2 || snapshot.Windows.Any(w => w.Label == "Outside")) throw new InvalidOperationException("Workspace membership included unrelated offscreen windows or omitted partial intersection.");
        var b = fixtureCatalog.Current.Entries.Single(e => e.Observation.Title == "B");
        fixedWindows.Add(b.Observation.Identity!.Value);
        if (CaptureWorkspace(PointD.Zero).Windows.Count != 1) throw new InvalidOperationException("Pinned window entered the saved workspace.");
        fixedWindows.Clear();
        var saved = bookmarks.SaveWorkspace("Synthetic UI", PointD.Zero, snapshot)!;
        workspaceRestorer = new(new WorkspaceUiPort(new WorkspacePort(this), port));
        port.Move("A", new(new(primary.Left + 380, primary.Top + 220), new(420, 300))); Observe(fixtureCatalog.Current, 0);
        OpenWorkspace(saved.Id); OpenWorkspace(saved.Id);
        if (!WorkspaceRestoring || !session.NavigationSuspended || CanCaptureGlobalInput || admissionTimer.Enabled)
            throw new InvalidOperationException("Workspace recovery failed to gate input/admission or lost its in-progress state.");
        for (var i = 0; i < 10 && WorkspaceRestoring; i++) { Observe(fixtureCatalog.Current, 0); AdvanceWorkspaceRestore(); }
        if (WorkspaceRestoring || workspaceRestoreTimer.Enabled || session.NavigationSuspended || port.Starts != 0 || workspaceProgress?.Visible != true)
            throw new InvalidOperationException("Existing-window UI recovery did not settle or created instances.");
        var aMember = saved.Layout!.Windows.Single(m => m.Label == "A");
        if (session.Workspace.Windows.Single(w => w.Title == "A").Bounds != aMember.Bounds) throw new InvalidOperationException("UI restore left the moved/resized window in the wrong layout.");
        port.Remove("A"); Observe(fixtureCatalog.Current, 0); OpenWorkspace(saved.Id);
        for (var i = 0; i < 10 && WorkspaceRestoring; i++) { Observe(fixtureCatalog.Current, 0); AdvanceWorkspaceRestore(); }
        if (port.Starts != 1 || WorkspaceRestoring || bookmarks.Collection.Items.Single().Layout!.Windows.Count != 2)
            throw new InvalidOperationException("UI restore lost the saved member or failed to launch only its missing synthetic target.");
        port.Remove("B"); Observe(fixtureCatalog.Current, 0); port.Slow = true; OpenWorkspace(saved.Id);
        if (port.Starts != 2 || !WorkspaceRestoring) throw new InvalidOperationException("Slow-start UI fixture did not wait.");
        CancelWorkspaceRestore(); var starts = port.Starts;
        for (var i = 0; i < 100; i++) AdvanceWorkspaceRestore();
        if (WorkspaceRestoring || workspaceRestoreTimer.Enabled || session.NavigationSuspended || port.Starts != starts)
            throw new InvalidOperationException("Cancelled UI recovery retained background work.");
        var queued = bookmarks.Add("Queued legacy", new(35, 45))!;
        reading = true; OpenWorkspace(queued.Id);
        if (queuedWorkspace != queued.Id || !workspaceRestoreTimer.Enabled) throw new InvalidOperationException("A transient read lost a workspace switch.");
        reading = false; AdvanceWorkspaceRestore();
        if (queuedWorkspace is not null || workspaceRestoreTimer.Enabled || session.Workspace.Camera.Position != queued.CameraPosition)
            throw new InvalidOperationException("Deferred workspace switch failed to resume and stop.");
        using (var editor = WorkspaceCommands().MemberEditor(saved.Id))
        {
            editor.Opacity = 0; editor.ShowInTaskbar = false; _ = editor.Handle; editor.PerformLayout();
            if (editor.Controls.OfType<ListBox>().Single().Items.Count != 2) throw new InvalidOperationException("Member editor lost saved windows.");
            editor.Show(); System.Windows.Forms.Application.DoEvents();
            var panel = editor.Controls.OfType<TableLayoutPanel>().Single();
            var arguments = panel.Controls.OfType<TextBox>().Single(t => t.Multiline);
            var automatic = panel.Controls.OfType<CheckBox>().Single();
            arguments.Text = "two words\r\nliteral\"quote\r\n& literal";
            panel.Controls.OfType<ChromeButton>().Single(b => b.Text == "保存启动项").PerformClick();
            var edited = bookmarks.Collection.Items.Single(b => b.Id == saved.Id).Layout!.Windows[0];
            if (edited.Id != saved.Layout!.Windows[0].Id || edited.Launch?.Arguments.SequenceEqual(new[] { "two words", "literal\"quote", "& literal" }) != true)
                throw new InvalidOperationException("Actual member editor lost stable identity or literal arguments.");
            automatic.Checked = false;
            panel.Controls.OfType<ChromeButton>().Single(b => b.Text == "保存启动项").PerformClick();
            if (bookmarks.Collection.Items.Single(b => b.Id == saved.Id).Layout!.Windows[0].Launch is not null)
                throw new InvalidOperationException("Actual member editor ignored disabled auto-launch.");
            var liveBefore = fixtureCatalog.Current.Entries.Count;
            panel.Controls.OfType<ChromeButton>().Single(b => b.Text.StartsWith("从快照移除此成员", StringComparison.Ordinal)).PerformClick();
            if (bookmarks.Collection.Items.Single(b => b.Id == saved.Id).Layout!.Windows.Count != 1 || fixtureCatalog.Current.Entries.Count != liveBefore)
                throw new InvalidOperationException("Removing a saved member closed a live application or failed to persist.");
            editor.Close();
        }
        if (bookmarks.SaveWorkspace(saved.Name, saved.CameraPosition, saved.Layout!, saved.Id)?.Id != saved.Id)
            throw new InvalidOperationException("Explicit layout update changed the workspace identity.");
        OpenWorkspace(saved.Id);
        if (!WorkspaceRestoring) throw new InvalidOperationException("Lifecycle boundary fixture failed to await the missing member.");
        var cameraDuringRestore = session.Workspace.Camera.Position;
        QueueTouchInput(new(BorderlessFlatWindow.Application.Navigation.NavigationInputKind.CameraDelta, new(80, 50))); NavigationFrame();
        if (session.Workspace.Camera.Position != cameraDuringRestore || touchInputs.Count != 0 || navigationTimer.Enabled)
            throw new InvalidOperationException("Workspace restore accepted navigation or left its high-frequency pump running.");
        SetDesktopTransitionPending(true);
        if (WorkspaceRestoring || workspaceRestoreTimer.Enabled || session.NavigationSuspended)
            throw new InvalidOperationException("Desktop transition retained a workspace task or timer.");
        SetDesktopTransitionPending(false);
        using (var presenter = new NativeOverviewPresenter([], session.Workspace.Camera, layout.Primary.WorkArea, bookmarks,
            [session.Workspace.Camera.Bounds], animate: false, showWithoutActivation: true))
        {
            presenter.SetWorkspaceActions(WorkspaceCommands());
            var view = typeof(NativeOverviewPresenter).GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(presenter)!;
            var open = (Action<Guid>)view.GetType().GetField("WorkspaceOpenRequested", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
            open(saved.Id);
            if (presenter.ConfirmedWorkspaceId != saved.Id || presenter.ConfirmedCameraPosition is not null)
                throw new InvalidOperationException("Workspace card lost its stable ID and fell back to a camera-only confirmation.");
        }
        var legacy = bookmarks.Add("Legacy", new(20, 30))!; OpenWorkspace(legacy.Id);
        if (WorkspaceRestoring || workspaceRestoreTimer.Enabled || session.Workspace.Camera.Position != legacy.CameraPosition || port.Starts != starts)
            throw new InvalidOperationException("Legacy position workspace invented restoration work.");
        var liveAtDeletion = fixtureCatalog.Current.Entries.Count;
        if (!bookmarks.Remove(saved.Id) || fixtureCatalog.Current.Entries.Count != liveAtDeletion)
            throw new InvalidOperationException("Deleting the workspace closed a live application.");
        Pause(); workspaceProgress?.Close();
        Console.WriteLine("PASS Workspace UI captures visible/partly-visible members, excludes pins, reuses/resizes windows, launches only the missing synthetic member, exposes saved members, cancels cleanly and preserves legacy jumps");
    }
    private sealed class WorkspaceUiPort(IWorkspaceRestorePort real, WorkspaceUiWindows fixture) : IWorkspaceRestorePort
    {
        public bool Available => real.Available;
        public void Launch(WorkspaceLaunch launch)
        {
            fixture.Starts++;
            if (!fixture.Slow) fixture.Add(Path.GetFileNameWithoutExtension(launch.Executable), new(new(400, 220), new(300, 200)));
        }
        public bool RestoreMinimized(WorkspaceCandidate target) => real.RestoreMinimized(target);
        public bool Apply(WorkspaceCandidate target, RectD bounds) => real.Apply(target, bounds);
        public WorkspacePlacement Check(WorkspaceCandidate target) => real.Check(target);
        public void CancelPrepared() => real.CancelPrepared();
    }
    private sealed class WorkspaceUiStore : IRecoveryJournalStore
    {
        private RecoveryJournal? current;
        public RecoveryJournal? Load() => current;
        public void Save(RecoveryJournal value) => current = value;
        public void Dispose() { }
    }
    private sealed class WorkspaceUiWindows(ManagedCanvasControl owner, SchedulingCatalog catalog) : IWindowMotionPort, IRecoveryWindowPort
    {
        private readonly Dictionary<nint, InspectedWindow> windows = [];
        private readonly HashSet<Guid> markers = [];
        private int sequence;
        internal int Starts;
        internal bool Slow;
        private void Publish() => catalog.Snapshot = new(DateTimeOffset.UtcNow, windows.Values.ToArray());
        internal void Add(string name, RectD bounds)
        {
            var index = ++sequence; var handle = (nint)(0x65000000 + index * 16); var process = (uint)(10000 + index);
            var observation = new WindowObservation { Handle = handle, ProcessId = process, ProcessCreationFileTime = index, Title = name, ClassName = "Synthetic.Workspace",
                IsAlive = true, IsVisible = true, IsCloaked = false, IsOnCurrentDesktop = true, MetadataAvailable = true, Bounds = bounds };
            windows.Add(handle, new(Guid.NewGuid(), observation, new(WindowExclusionReason.None)));
            owner.workspaceExecutables[(process, index)] = "C:\\Synthetic\\" + name + ".exe"; Publish();
        }
        internal void Remove(string name) { windows.Remove(windows.Single(w => w.Value.Observation.Title == name).Key); Publish(); }
        internal void Move(string name, RectD bounds)
        {
            var pair = windows.Single(w => w.Value.Observation.Title == name); windows[pair.Key] = pair.Value with { Observation = pair.Value.Observation with { Bounds = bounds } }; Publish();
        }
        public MotionBatchResult RequestBatch(RecoverySession recovery, IReadOnlyList<WindowTranslation> moves)
        {
            foreach (var move in moves)
            {
                if (!recovery.CanMove(move.RecoveryId)) throw new InvalidOperationException("Synthetic UI move lacked durable authorization.");
                var handle = recovery.Journal.Entries.Single(e => e.Id == move.RecoveryId).Identity.Handle;
                var entry = windows[handle]; windows[handle] = entry with { Observation = entry.Observation with { Bounds = move.Target.ToRectD() } };
            }
            Publish(); return new(true, "Synthetic UI batch");
        }
        public bool RequestActivation(RecoverySession recovery, Guid id) => true;
        public OriginalWindowState Capture(NativeWindowIdentity identity)
        {
            var b = windows[identity.Handle].Observation.Bounds!.Value;
            return new(new(0, 1, -1, -1, -1, -1, (int)b.Position.X, (int)b.Position.Y, (int)b.Right, (int)b.Bottom), new((int)b.Position.X, (int)b.Position.Y, (int)b.Right, (int)b.Bottom));
        }
        public void Mark(RecoveryEntry entry, string name) => markers.Add(entry.Id);
        public bool Matches(RecoveryEntry entry, string name) => markers.Contains(entry.Id) && windows.TryGetValue(entry.Identity.Handle, out var current) && current.Observation.Identity == entry.Identity;
        public void RemoveMarker(RecoveryEntry entry, string name) => markers.Remove(entry.Id);
        public RecoveryResult Restore(RecoveryEntry entry, string name)
        {
            if (!Matches(entry, name)) return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "Closed synthetic target");
            var current = windows[entry.Identity.Handle]; windows[entry.Identity.Handle] = current with { Observation = current.Observation with { Bounds = entry.Original.PhysicalBounds.ToRectD() } };
            markers.Remove(entry.Id); Publish(); return new(entry.Id, RecoveryOutcome.Restored, "Synthetic UI original restored");
        }
    }
}
