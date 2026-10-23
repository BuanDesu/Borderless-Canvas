using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

internal static partial class ManagementChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Managed native translations preserve world layout and never activate per pan", Pan);
        yield return ("Remote camera views park physical windows without rewriting world positions", Park);
        yield return ("Managed home/back restore camera views rather than gathering windows", Navigation);
        yield return ("Focus activates only after native placement is observed", Focus);
        yield return ("Activation denial keeps the located layout and reports a usable fallback", ActivationDenied);
        yield return ("A new navigation gesture cancels an older pending activation", SupersededFocus);
        yield return ("Overview suspension freezes native navigation and foreground-follow until confirmation", OverviewSuspension);
        yield return ("Shell relocation before parked foreground activation preserves world geometry", ShellActivation);
        yield return ("External drag/resize updates only that window in world space", ExternalDrag);
        yield return ("Intermediate queued movement callbacks cannot corrupt world geometry", Queued);
        yield return ("Stalled movement times out and restores the full session", Timeout);
        yield return ("Partially rejected native batches restore every registered window", PartialFailure);
        yield return ("Owned dialogs including indirect owner chains pause and restore management", Popup);
        yield return ("Closed targets are skipped while surviving managed windows recover", Closed);
        yield return ("Closing a group of managed targets removes every paused-layout ghost", ClosedGroup);
        yield return ("A hide-before-destroy event sequence removes paused ghosts without moving survivors", HiddenThenClosed);
        yield return ("Managed label jumps translate the whole camera and preserve window layout and navigation history", BookmarkJump);
        yield return ("Runtime admission preserves the existing world, native placement, camera, history and labels", RuntimeAdd);
        yield return ("A late batch registers and moves only new windows into separate empty regions", RuntimeBatch);
        yield return ("Duplicate, existing and unsupported late selections cannot register or move anything", RuntimeInvalid);
        yield return ("Pending, suspended and paused sessions cannot admit late windows", RuntimeDeferred);
        yield return ("A failed late registration recovers durable records without submitting a new native batch", RuntimeRegisterFailure);
        yield return ("Invalid-data journal writes fail safely during runtime admission", RuntimeStorageFailure);
        yield return ("Partial late movement restores originals and rolls back only new logical entries", RuntimePartial);
        yield return ("Closing reentrantly during late registration cannot move new windows or leak recovery ownership", RuntimeDispose);
        yield return ("Pause/resume preserves the logical layout, camera and bookmarks", Resume);
        yield return ("Beginning management cannot silently recover an inherited journal", Inherited);
        yield return ("Repeated pause preserves failed recovery state and all retained evidence", FailedRecoveryPause);
        yield return ("External recovery requires a complete report and a newly verified empty journal", ExternalRecovery);
        yield return ("Unstarted pause and stale external completion cannot change active management", InactivePause);
        yield return ("Unsupported native state and mixed DPI cannot start management", Unsupported);
        yield return ("Same DPI multiscreen pans, primary-screen focus and recovery preserve world layout", MultipleScreens);
        yield return ("Rapid window cycling advances even before foreground callbacks arrive", Cycle);
        yield return ("Display geometry and DPI changes invalidate the active baseline", Displays);
        foreach (var item in DisplayTransitionCases()) yield return item;
        foreach (var item in ArrangementCases()) yield return item;
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.FromHours(8));
    private static RectD Rect(double x, double y, double width = 640, double height = 480) => new(new PointD(x, y), new SizeD(width, height));
    private sealed class Harness : IWindowObservationSource, IWindowMotionPort, IRecoveryWindowPort, IDisposable
    {
        internal readonly Dictionary<nint, WindowObservation> Native = [];
        internal readonly WindowInspectionService Inspection;
        internal readonly ManagedCanvasSession Session;
        internal RecoveryJournal? Saved;
        internal bool Delayed;
        internal bool RejectPartial;
        internal bool AllowActivation = true;
        internal int Batches;
        internal int Activations;
        internal int Restorations;
        internal nint? CaptureFailure;
        internal Action<nint>? DuringCapture;
        internal Action? DuringBatch;
        internal int InvalidDataSaves;
        internal bool FailRestore, FailLoad;
        internal IReadOnlyList<WindowTranslation> LastBatch = [];
        internal readonly List<IReadOnlyList<WindowTranslation>> Pending = [];
        private readonly HashSet<Guid> markers = [];
        private RecoverySession? lastRecovery;
        internal readonly DisplayLayout Layout;
        internal Harness(bool multiple = false, bool sameDpi = false)
        {
            Native[42] = Window(42, Rect(100, 140)); Native[43] = Window(43, Rect(900, 140));
            Inspection = new(this, new(999));
            DisplayMonitor[] monitors = multiple
                ? [new("primary", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), 96), new("other", false, new(-1920, -200, 0, 880), new(-1920, -200, 0, 840), sameDpi ? 96u : 144u)]
                : [new("primary", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), 96)];
            Layout = new(monitors);
            Session = new(() => lastRecovery = new(new Store(this), this), this, Layout);
        }
        internal IReadOnlyList<InspectedWindow> Selected() => Inspection.Inspect().Entries.Where(entry => entry.Admission.IsAdmitted).ToArray();
        internal void Begin() { Require(Session.Start(Selected(), Now), Session.Message); Tick(); }
        internal void Tick(int milliseconds = 0, nint foreground = 0) => Session.Observe(Inspection.Inspect(), foreground, Now.AddMilliseconds(milliseconds));
        internal void Apply(IReadOnlyList<WindowTranslation> batch)
        {
            foreach (var move in batch)
            {
                var entry = lastRecovery!.Journal.Entries.Single(entry => entry.Id == move.RecoveryId);
                Native[entry.Identity.Handle] = Native[entry.Identity.Handle] with { Bounds = move.Target.ToRectD() };
            }
        }
        public IReadOnlyList<WindowObservation> ReadWindows() => Native.Values.ToArray();
        public MotionBatchResult RequestBatch(RecoverySession recovery, IReadOnlyList<WindowTranslation> translations)
        {
            Batches++;
            LastBatch = translations.ToArray();
            Require(translations.All(item => recovery.CanMove(item.RecoveryId)), "Batch lacks durable ownership.");
            DuringBatch?.Invoke();
            if (RejectPartial) { Apply(translations.Take(1).ToArray()); return new(false, "Controlled partial rejection."); }
            if (Delayed) Pending.Add(translations.ToArray()); else Apply(translations);
            return new(true, "Controlled acceptance.");
        }
        public bool RequestActivation(RecoverySession recovery, Guid id) { Activations++; return AllowActivation; }
        public OriginalWindowState Capture(NativeWindowIdentity identity)
        {
            DuringCapture?.Invoke(identity.Handle);
            if (CaptureFailure == identity.Handle) throw new InvalidOperationException("Late fixture refused capture.");
            var bounds = Native[identity.Handle].Bounds!.Value;
            return new(new(0, 1, -1, -1, -1, -1, (int)bounds.Position.X, (int)bounds.Position.Y, (int)bounds.Right, (int)bounds.Bottom),
                new((int)bounds.Position.X, (int)bounds.Position.Y, (int)bounds.Right, (int)bounds.Bottom));
        }
        public void Mark(RecoveryEntry entry, string name) => markers.Add(entry.Id);
        public bool Matches(RecoveryEntry entry, string name) => markers.Contains(entry.Id)
            && Native.TryGetValue(entry.Identity.Handle, out var window) && window.Identity == entry.Identity;
        public void RemoveMarker(RecoveryEntry entry, string name) => markers.Remove(entry.Id);
        public RecoveryResult Restore(RecoveryEntry entry, string name)
        {
            Restorations++;
            if (FailRestore) return new(entry.Id, RecoveryOutcome.Failed, "Controlled restore failure.");
            if (!Matches(entry, name)) return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "Missing fixture.");
            Native[entry.Identity.Handle] = Native[entry.Identity.Handle] with { Bounds = entry.Original.PhysicalBounds.ToRectD() };
            markers.Remove(entry.Id); return new(entry.Id, RecoveryOutcome.Restored, "Restored fixture.");
        }
        private sealed class Store(Harness owner) : IRecoveryJournalStore
        {
            public RecoveryJournal? Load() => owner.FailLoad ? throw new IOException("Controlled read failure.") : owner.Saved;
            public void Save(RecoveryJournal journal)
            {
                if (owner.InvalidDataSaves > 0) { owner.InvalidDataSaves--; throw new InvalidDataException("Controlled invalid-data save failure."); }
                owner.Saved = journal;
            }
            public void Dispose() { }
        }
        public void Dispose() => Session.Dispose();
    }
    private static WindowObservation Window(nint handle, RectD bounds) => new()
    {
        Handle = handle, ProcessId = (uint)(100 + handle), ProcessCreationFileTime = 123456789,
        Title = "Fixture " + handle, ClassName = "Fixture", Bounds = bounds, IsAlive = true,
        MetadataAvailable = true, IsVisible = true, IsCloaked = false, IsOnCurrentDesktop = true
    };
    private static void Pan()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray();
        Require(h.Session.Pan(new(120, 80), Now), "Pan failed."); h.Tick();
        Require(h.Session.Workspace.Windows.SequenceEqual(before), "Native echo changed world layout.");
        Require(h.Native[42].Bounds!.Value.Position == new PointD(-20, 60)
            && h.Native[43].Bounds!.Value.Position == new PointD(780, 60) && h.Activations == 0, "Pan resized, activated or changed relative displacement.");
    }
    private static void Park()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray();
        h.Session.Navigate(new(1e12, -1e12), Now); h.Tick();
        Require(h.Session.IsRunning && h.Session.ParkedCount == 2 && h.Session.Workspace.Windows.SequenceEqual(before), "Parking contaminated world positions.");
        Require(h.Native[42].Bounds == h.Native[43].Bounds, "Fixture parking assumption changed.");
    }
    private static void Navigation()
    {
        using var h = new Harness(); h.Begin(); var bounds = h.Session.Workspace.Windows.ToArray();
        h.Session.Navigate(new(300, 0), Now); h.Tick(); h.Session.Home(Now); h.Tick();
        Require(h.Session.Back(Now), "Back failed."); h.Tick();
        Require(h.Session.Workspace.Camera.Position == new PointD(300, 0) && h.Session.Workspace.Windows.SequenceEqual(bounds), "Navigation gathered windows.");
    }
    private static void Focus()
    {
        using var h = new Harness(); h.Begin(); var id = h.Session.Workspace.Windows.Last().Id;
        h.Delayed = true; Require(h.Session.Focus(id, Now), "Focus failed.");
        Require(h.Activations == 0 && h.Session.HasPending, "Focus activated before placement confirmation.");
        h.Apply(h.Pending.Last()); h.Tick(); Require(h.Activations == 1 && !h.Session.HasPending, "Confirmed focus did not activate.");
    }
    private static void ActivationDenied()
    {
        using var h = new Harness { AllowActivation = false }; h.Begin(); h.Session.Focus(h.Session.Workspace.Windows[0].Id, Now); h.Tick();
        Require(h.Session.IsRunning && h.Activations == 1 && h.Session.Message.Contains("未允许"), "Activation denial lost usable layout or claimed success.");
    }
    private static void SupersededFocus()
    {
        using var h = new Harness(); h.Begin(); h.Delayed = true;
        h.Session.Focus(h.Session.Workspace.Windows[0].Id, Now);
        h.Session.Pan(new(120, 0), Now);
        h.Apply(h.Pending.Last()); h.Tick();
        Require(h.Session.IsRunning && !h.Session.HasPending && h.Activations == 0, "Superseded focus stole activation during pan.");
    }
    private static void OverviewSuspension()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(8000, 0), Now); h.Tick();
        var camera = h.Session.Workspace.Camera.Position; var batches = h.Batches;
        h.Session.SuspendNavigation(true);
        Require(!h.Session.Pan(new(120, 0), Now) && !h.Session.Home(Now) && !h.Session.Focus(h.Session.Workspace.Windows[0].Id, Now), "Overview allowed working navigation.");
        h.Tick(100, foreground: 42);
        Require(h.Session.IsRunning && h.Session.Workspace.Camera.Position == camera && h.Batches == batches && h.Activations == 0,
            "Foreground-follow changed the overview's starting view.");
        h.Session.SuspendNavigation(false); Require(h.Session.Home(Now), "Closing overview did not release suspension.");
    }
    private static void ExternalDrag()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(100, 0), Now); h.Tick(); var before = h.Session.Workspace.Windows[0]; var batches = h.Batches;
        h.Native[42] = h.Native[42] with { Bounds = Rect(200, 140, 680, 500) }; h.Tick();
        var after = h.Session.Workspace.Windows.Single(window => window.Id == before.Id);
        Require(after.Bounds.Position == new PointD(300, 100) && after.Bounds.Size == new SizeD(680, 500)
            && h.Batches == batches, "External edit failed to reflect world bounds or moved unrelated windows.");
    }
    private static void ShellActivation()
    {
        using var h = new Harness(); h.Begin(); var before = h.Session.Workspace.Windows.ToArray();
        h.Session.Navigate(new(8000, 0), Now); h.Tick();
        h.Native[42] = h.Native[42] with { Bounds = Rect(20, 80) };
        h.Delayed = true; h.Tick(100, foreground: 42);
        Require(h.Session.IsRunning && h.Session.HasPending && h.Session.Workspace.Windows.SequenceEqual(before),
            "Shell relocation was mistaken for an external world edit or unexpected movement.");
        h.Apply(h.Pending.Last()); h.Tick(150, foreground: 42);
        Require(h.Session.IsRunning && !h.Session.HasPending && h.Session.Workspace.Windows.SequenceEqual(before)
            && h.Activations == 0, "Foreground tracking changed layout or unnecessarily activated the target again.");
    }
    private static void Queued()
    {
        using var h = new Harness(); h.Begin(); var original = h.Session.Workspace.Windows.ToArray(); h.Delayed = true;
        h.Session.Navigate(new(100, 0), Now); h.Session.Navigate(new(200, 0), Now);
        h.Apply(h.Pending.First()); h.Tick(50); Require(h.Session.HasPending && h.Session.IsRunning, "Intermediate expected callback rejected.");
        h.Apply(h.Pending.Last()); h.Tick(100);
        Require(!h.Session.HasPending && h.Session.Workspace.Windows.SequenceEqual(original), "Queued callbacks changed world layout.");
    }
    private static void Timeout()
    {
        using var h = new Harness(); h.Begin(); h.Delayed = true; h.Session.Pan(new(400, 0), Now); h.Tick(1600);
        Require(h.Session.Status == CanvasSessionStatus.Paused && h.Restorations == 2 && h.Native[42].Bounds == Rect(100, 140), "Stall did not recover.");
    }
    private static void PartialFailure()
    {
        using var h = new Harness(); h.Begin(); h.RejectPartial = true;
        Require(!h.Session.Pan(new(120, 0), Now), "Rejected partial batch returned success.");
        Require(!h.Session.IsRunning && h.Restorations == 2 && h.Native[42].Bounds == Rect(100, 140)
            && h.Native[43].Bounds == Rect(900, 140), "Partial movement was not rolled back through recovery.");
    }
    private static void Popup()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(8000, 0), Now); h.Tick();
        h.Native[50] = Window(50, Rect(0, 0)) with { OwnerHandle = 42, IsVisible = false, Title = "" };
        h.Native[51] = Window(51, Rect(100, 100)) with { OwnerHandle = 50, Title = "Dialog" }; h.Tick();
        Require(!h.Session.IsRunning && h.Restorations == 2, "Modal owner remained parked.");
    }
    private static void Closed()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(300, 0), Now); h.Tick(); h.Native.Remove(42); h.Tick();
        Require(!h.Session.IsRunning && h.Native[43].Bounds == Rect(900, 140) && h.Saved!.Entries.Count == 0
            && h.Session.Workspace.Windows.Count == 1 && h.Session.ManagedHandles.SequenceEqual(new nint[] { 43 }), "Close did not safely remove the target and recover survivors.");
    }
    private static void Resume()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(300, -100), Now); h.Tick(); var before = h.Session.Workspace.Windows.ToArray();
        h.Session.Workspace.AddBookmark("Area"); h.Session.Pause(); h.Begin();
        Require(h.Session.Workspace.Camera.Position == new PointD(300, -100) && h.Session.Workspace.Windows.SequenceEqual(before)
            && h.Session.Workspace.Bookmarks.Count == 1, "Resume forgot logical session.");
    }
    private static void ClosedGroup()
    {
        using var h = new Harness(); h.Begin(); h.Native.Clear(); h.Tick();
        Require(!h.Session.IsRunning && h.Session.Workspace.Windows.Count == 0 && h.Session.ManagedHandles.Count == 0
            && h.Saved!.Entries.Count == 0, "Closed group left stale windows or recovery records.");
    }
    private static void HiddenThenClosed()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(500, 600), Now); h.Tick();
        h.Native[42] = h.Native[42] with { IsVisible = false }; h.Tick();
        Require(h.Session.Status == CanvasSessionStatus.Paused && h.Session.Workspace.Windows.Count == 2,
            "Hidden live window should pause management but retain its world position.");
        var survivor = h.Session.Workspace.Windows.Single(window => window.Title == "Fixture 43");
        var camera = h.Session.Workspace.Camera; var batches = h.Batches; var restores = h.Restorations;
        h.Native.Remove(42); h.Tick();
        Require(h.Session.Workspace.Windows.SequenceEqual(new[] { survivor }) && h.Session.Workspace.Camera == camera
            && h.Batches == batches && h.Restorations == restores, "Paused destroy left a ghost or moved another window.");
    }
    private static void BookmarkJump()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(-3000, 2200), Now); h.Tick();
        var entry = h.Session.Workspace.AddBookmark("同名位置"); var windows = h.Session.Workspace.Windows.ToArray();
        h.Session.Home(Now); h.Tick();
        Require(h.Session.GoToBookmark(entry.Id, Now), "Managed label jump refused."); h.Tick();
        Require(h.Session.Workspace.Camera.Position == entry.CameraPosition && h.Session.Workspace.Windows.SequenceEqual(windows)
            && h.Session.Back(Now), "Label jump changed layout or lost history."); h.Tick();
        Require(h.Session.Workspace.Camera.Position == PointD.Zero && !h.Session.GoToBookmark(Guid.NewGuid(), Now), "Back or missing label changed the view.");
        h.Session.Pause(); Require(!h.Session.GoToBookmark(entry.Id, Now), "Paused label jump moved native windows.");
    }
    private static InspectedWindow[] Late(Harness h, params int[] handles) => h.Selected().Where(entry => handles.Contains((int)entry.Observation.Handle)).ToArray();
    private static void RuntimeAdd()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(400, -200), Now); h.Tick();
        var bookmark = h.Session.Workspace.AddBookmark("位置"); var camera = h.Session.Workspace.Camera; var history = h.Session.Workspace.PreviousView;
        var before = h.Session.Workspace.Windows.ToArray(); var a = h.Native[42].Bounds; var b = h.Native[43].Bounds;
        h.Native[44] = Window(44, Rect(120, 160)); Require(h.Session.AddWindows(Late(h, 44), Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Windows.Where(window => before.Any(old => old.Id == window.Id)).SequenceEqual(before)
            && h.Native[42].Bounds == a && h.Native[43].Bounds == b && h.Session.Workspace.Camera == camera
            && h.Session.Workspace.PreviousView == history && h.Session.Workspace.Bookmarks.Single() == bookmark && h.Saved!.Entries.Count == 3
            && h.LastBatch.Count == 1 && h.Activations == 0, "Late admission rebuilt, moved or renavigated existing layout.");
    }
    private static void RuntimeBatch()
    {
        using var h = new Harness(); h.Begin(); h.Session.Navigate(new(3000, -2400), Now); h.Tick(); var original = h.Session.Workspace.Windows.ToArray();
        h.Native[44] = Window(44, Rect(40, 70, 440, 310)); h.Native[45] = Window(45, Rect(60, 90, 500, 360));
        Require(h.Session.AddWindows(Late(h, 44, 45), Now) && h.Saved!.Entries.Count == 4 && h.LastBatch.Count == 2, h.Session.Message); h.Tick();
        var added = h.Session.Workspace.Windows.Where(window => !original.Any(old => old.Id == window.Id)).ToArray();
        Require(added.Length == 2 && !BorderlessFlatWindow.Core.Canvas.NearbyWindowPlacement.OverlapsWithGap(added[0].Bounds, added[1].Bounds)
            && added.All(window => original.All(old => !BorderlessFlatWindow.Core.Canvas.NearbyWindowPlacement.OverlapsWithGap(window.Bounds, old.Bounds)))
            && added.All(window => BorderlessFlatWindow.Core.Canvas.NearbyWindowPlacement.FitsInside(window.Bounds, h.Session.Workspace.Camera.Bounds)), "New batch overlapped or ignored current camera.");
        h.Session.Pause(); Require(h.Native[44].Bounds == Rect(40, 70, 440, 310) && h.Native[45].Bounds == Rect(60, 90, 500, 360) && h.Saved!.Entries.Count == 0, "Late originals were not restored.");
    }
    private static void RuntimeInvalid()
    {
        using var h = new Harness(); h.Begin(); h.Native[44] = Window(44, Rect(10, 20)); var entry = Late(h, 44).Single(); var batches = h.Batches;
        foreach (var selection in new[] { new[] { entry, entry }, new[] { entry, entry with { Id = Guid.NewGuid() } },
            h.Selected().Where(item => item.Observation.Handle == 42).ToArray(), new[] { entry with { Observation = entry.Observation with { IsMaximized = true } } } })
            Require(!h.Session.AddWindows(selection, Now) && h.Session.IsRunning && h.Batches == batches && h.Saved!.Entries.Count == 2, "Invalid selection had side effects.");
    }
    private static void RuntimeDeferred()
    {
        using var h = new Harness(); h.Begin(); h.Native[44] = Window(44, Rect(10, 20)); var entry = Late(h, 44); h.Delayed = true;
        h.Session.Pan(new(200, 100), Now); Require(!h.Session.AddWindows(entry, Now) && h.Saved!.Entries.Count == 2, "Pending move allowed admission.");
        h.Apply(h.Pending.Single()); h.Tick(); h.Session.SuspendNavigation(true);
        Require(!h.Session.AddWindows(entry, Now), "Overview suspension allowed admission."); h.Session.SuspendNavigation(false); h.Session.Pause();
        Require(!h.Session.AddWindows(entry, Now), "Paused session allowed admission.");
    }
    private static void RuntimeRegisterFailure()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(200, 100), Now); h.Tick(); var before = h.Session.Workspace.Windows.ToArray(); var batches = h.Batches;
        h.Native[44] = Window(44, Rect(10, 20)); h.Native[45] = Window(45, Rect(50, 60)); h.CaptureFailure = 45;
        Require(!h.Session.AddWindows(Late(h, 44, 45), Now) && !h.Session.IsRunning && h.Batches == batches && h.Saved!.Entries.Count == 0
            && h.Session.Workspace.Windows.SequenceEqual(before) && h.Native[44].Bounds == Rect(10, 20), "Failed capture moved or published new windows.");
    }
    private static void RuntimeStorageFailure()
    {
        using var h = new Harness(); h.Begin(); h.Native[44] = Window(44, Rect(10, 20)); h.InvalidDataSaves = 1;
        Require(!h.Session.AddWindows(Late(h, 44), Now) && !h.Session.IsRunning && h.Saved!.Entries.Count == 0 && h.Native[44].Bounds == Rect(10, 20),
            "InvalidDataException escaped or an unregistered window moved.");
    }
    private static void RuntimePartial()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(200, 100), Now); h.Tick(); var before = h.Session.Workspace.Windows.ToArray(); var camera = h.Session.Workspace.Camera;
        h.Native[44] = Window(44, Rect(10, 20)); h.Native[45] = Window(45, Rect(50, 60)); h.RejectPartial = true;
        Require(!h.Session.AddWindows(Late(h, 44, 45), Now) && !h.Session.IsRunning && h.Saved!.Entries.Count == 0
            && h.Session.Workspace.Windows.SequenceEqual(before) && h.Session.ManagedIds.Count == 2 && h.Session.Workspace.Camera == camera
            && h.Native[42].Bounds == Rect(100, 140) && h.Native[44].Bounds == Rect(10, 20) && h.Native[45].Bounds == Rect(50, 60), "Partial late batch could not recover and roll back.");
    }
    private static void RuntimeDispose()
    {
        using var h = new Harness(); h.Begin(); var batches = h.Batches; h.Native[44] = Window(44, Rect(10, 20));
        h.DuringCapture = handle => { if (handle == 44) h.Session.Dispose(); };
        Require(!h.Session.AddWindows(Late(h, 44), Now) && h.Batches == batches && h.Saved!.Entries.Count == 0 && h.Native[44].Bounds == Rect(10, 20),
            "Reentrant close leaked ownership or submitted movement after exit.");
    }
    private static void Inherited()
    {
        using var h = new Harness(); var identity = h.Native[42].Identity!.Value;
        h.Saved = RecoveryJournal.Empty().WithEntries([new(Guid.NewGuid(), identity, h.Capture(identity), 123, true)]);
        Require(!h.Session.Start(h.Selected(), Now) && h.Session.Status == CanvasSessionStatus.RecoveryRequired
            && h.Restorations == 0 && h.Saved.Entries.Count == 1 && h.Batches == 0, "Start moved an inherited session.");
        var message = h.Session.Message;
        h.Session.Pause(); h.Session.Pause();
        Require(h.Session.Status == CanvasSessionStatus.RecoveryRequired && h.Session.Message == message
            && h.Restorations == 0 && h.Saved.Entries.Count == 1, "Repeated pause concealed inherited recovery.");
    }
    private static void FailedRecoveryPause()
    {
        using var h = new Harness(); h.Begin(); h.FailRestore = true; h.Session.Pause();
        var records = h.Saved; var restores = h.Restorations; var message = h.Session.Message;
        Require(h.Session.Status == CanvasSessionStatus.RecoveryRequired && records!.Entries.Count == 2, "Controlled restore did not fail.");
        h.Session.Pause(); h.Session.Pause();
        Require(h.Session.Status == CanvasSessionStatus.RecoveryRequired && h.Session.Message == message && ReferenceEquals(h.Saved, records)
            && h.Restorations == restores, "Repeated pause discarded pending recovery evidence or claimed success.");
    }
    private static void ExternalRecovery()
    {
        using var h = new Harness(); h.Begin(); h.FailRestore = true; h.Session.Pause();
        var complete = new RecoveryReport([new(Guid.NewGuid(), RecoveryOutcome.Restored, "Synthetic report")], 0);
        Require(!h.Session.RecheckAfterExternalRecovery(complete) && h.Session.Status == CanvasSessionStatus.RecoveryRequired, "A stale success report cleared a pending journal.");
        h.Saved = h.Saved!.WithEntries([]); h.FailLoad = true;
        Require(!h.Session.RecheckAfterExternalRecovery(complete) && h.Session.Status == CanvasSessionStatus.RecoveryRequired, "Unreadable recovery was accepted.");
        h.FailLoad = false;
        Require(!h.Session.RecheckAfterExternalRecovery(complete with { Remaining = 1 })
            && !h.Session.RecheckAfterExternalRecovery(new([new(Guid.NewGuid(), RecoveryOutcome.Failed, "Failed")], 0)), "Incomplete report was accepted.");
        Require(h.Session.RecheckAfterExternalRecovery(complete) && h.Session.Status == CanvasSessionStatus.Paused && !h.Session.IsRunning, "Verified recovery did not remain paused.");
    }
    private static void InactivePause()
    {
        using var h = new Harness(); h.Session.Pause();
        Require(h.Session.Status == CanvasSessionStatus.Idle && h.Saved is null && h.Restorations == 0, "Unstarted pause claimed recovery.");
        h.Begin(); var journal = h.Saved; var camera = h.Session.Workspace.Camera;
        Require(!h.Session.RecheckAfterExternalRecovery(new([], 0)) && h.Session.IsRunning && ReferenceEquals(journal, h.Saved)
            && h.Session.Workspace.Camera == camera, "Stale recovery completion changed active management.");
    }
    private static void Unsupported()
    {
        using var multi = new Harness(true); Require(!multi.Session.Start(multi.Selected(), Now) && multi.Batches == 0, "Mixed DPI started.");
        using var top = new Harness(); top.Native[42] = top.Native[42] with { IsTopMost = true };
        Require(!top.Session.Start(top.Selected(), Now) && top.Saved is null, "Topmost selection registered.");
    }
    private static void MultipleScreens()
    {
        using var h = new Harness(true, true);
        h.Native[43] = Window(43, Rect(-1700, -100));
        var originals = h.Native.ToDictionary(p => p.Key, p => p.Value.Bounds);
        h.Begin(); var world = h.Session.Workspace.Windows.ToArray();
        Require(h.Session.ParkedCount == 0 && h.Layout.SupportsCanvas, "Secondary screen was parked at start.");
        Require(h.Session.Pan(new(240, -80), Now), h.Session.Message); h.Tick();
        Require(h.Session.Workspace.Windows.SequenceEqual(world) && h.Native[43].Bounds!.Value.Position == new PointD(-1940, -20), "Secondary pan changed logical layout.");
        var id = h.Inspection.Current.Entries.Single(e => e.Observation.Handle == 43).Id!.Value;
        Require(h.Session.Focus(id, Now), h.Session.Message); h.Tick();
        Require(h.Native[43].Bounds!.Value.Center == h.Layout.Primary.WorkArea.ToRectD().Center, "Focus landed in a bezel or secondary screen.");
        h.Session.Navigate(new(100000, -100000), Now); h.Tick();
        Require(h.Session.ParkedCount == 2 && h.Native.Values.All(w => h.Layout.Monitors.All(m => !BorderlessFlatWindow.Core.Canvas.WindowPlacementPlanner.Intersects(
            new((int)w.Bounds!.Value.Position.X, (int)w.Bounds.Value.Position.Y, (int)w.Bounds.Value.Right, (int)w.Bounds.Value.Bottom), m.Bounds))), "Parking touched a physical screen.");
        h.Session.Pause(); Require(h.Native.All(p => p.Value.Bounds == originals[p.Key]) && h.Saved!.Entries.Count == 0, "Multiscreen originals failed recovery.");
    }
    private static void Cycle()
    {
        using var h = new Harness(); h.Begin(); var ids = h.Session.Workspace.Windows.Select(w => w.Id).ToArray(); h.Delayed = true;
        Require(h.Session.CycleWindow(1, Now), h.Session.Message); var first = h.Session.Workspace.Camera.Position;
        Require(h.Session.CycleWindow(1, Now) && h.Session.Workspace.Camera.Position != first, "Repeated cycle selected the same pending target.");
        Require(h.Session.CycleWindow(1, Now) && h.Session.Workspace.Camera.Position == first, "Cycle failed to wrap.");
        h.Session.SuspendNavigation(true); Require(!h.Session.CycleWindow(-1, Now), "Overview allowed background cycling.");
    }
    private static void Displays()
    {
        using var h = new Harness();
        Require(h.Session.MatchesDisplay(new DisplayLayout(h.Layout.Monitors)), "Same geometry not recognized.");
        Require(!h.Session.MatchesDisplay(new DisplayLayout([new("primary", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), 144)])), "DPI change not recognized.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
