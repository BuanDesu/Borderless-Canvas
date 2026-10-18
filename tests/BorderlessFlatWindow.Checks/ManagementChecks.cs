using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

internal static class ManagementChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Managed native translations preserve world layout and never activate per pan", Pan);
        yield return ("Remote camera views park physical windows without rewriting world positions", Park);
        yield return ("Managed home/back restore camera views rather than gathering windows", Navigation);
        yield return ("Focus activates only after native placement is observed", Focus);
        yield return ("Activation denial keeps the located layout and reports a usable fallback", ActivationDenied);
        yield return ("A new navigation gesture cancels an older pending activation", SupersededFocus);
        yield return ("Shell relocation before parked foreground activation preserves world geometry", ShellActivation);
        yield return ("External drag/resize updates only that window in world space", ExternalDrag);
        yield return ("Intermediate queued movement callbacks cannot corrupt world geometry", Queued);
        yield return ("Stalled movement times out and restores the full session", Timeout);
        yield return ("Partially rejected native batches restore every registered window", PartialFailure);
        yield return ("Owned dialogs including indirect owner chains pause and restore management", Popup);
        yield return ("Closed targets are skipped while surviving managed windows recover", Closed);
        yield return ("Pause/resume preserves the logical layout, camera and bookmarks", Resume);
        yield return ("Beginning management cannot silently recover an inherited journal", Inherited);
        yield return ("Unsupported native state and multiscreen configuration cannot start management", Unsupported);
        yield return ("Display geometry and DPI changes invalidate the active baseline", Displays);
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
        internal readonly List<IReadOnlyList<WindowTranslation>> Pending = [];
        private readonly HashSet<Guid> markers = [];
        private RecoverySession? lastRecovery;
        internal readonly DisplayLayout Layout;
        internal Harness(bool multiple = false)
        {
            Native[42] = Window(42, Rect(100, 140)); Native[43] = Window(43, Rect(900, 140));
            Inspection = new(this, new(999));
            DisplayMonitor[] monitors = multiple
                ? [new("primary", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), 96), new("other", false, new(1920, 0, 3840, 1080), new(1920, 0, 3840, 1040), 144)]
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
            Require(translations.All(item => recovery.CanMove(item.RecoveryId)), "Batch lacks durable ownership.");
            if (RejectPartial) { Apply(translations.Take(1).ToArray()); return new(false, "Controlled partial rejection."); }
            if (Delayed) Pending.Add(translations.ToArray()); else Apply(translations);
            return new(true, "Controlled acceptance.");
        }
        public bool RequestActivation(RecoverySession recovery, Guid id) { Activations++; return AllowActivation; }
        public OriginalWindowState Capture(NativeWindowIdentity identity)
        {
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
            if (!Matches(entry, name)) return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "Missing fixture.");
            Native[entry.Identity.Handle] = Native[entry.Identity.Handle] with { Bounds = entry.Original.PhysicalBounds.ToRectD() };
            markers.Remove(entry.Id); return new(entry.Id, RecoveryOutcome.Restored, "Restored fixture.");
        }
        private sealed class Store(Harness owner) : IRecoveryJournalStore
        {
            public RecoveryJournal? Load() => owner.Saved;
            public void Save(RecoveryJournal journal) => owner.Saved = journal;
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
        Require(!h.Session.IsRunning && h.Native[43].Bounds == Rect(900, 140) && h.Saved!.Entries.Count == 0, "Close did not safely recover survivors.");
    }
    private static void Resume()
    {
        using var h = new Harness(); h.Begin(); h.Session.Pan(new(300, -100), Now); h.Tick(); var before = h.Session.Workspace.Windows.ToArray();
        h.Session.Workspace.AddBookmark("Area"); h.Session.Pause(); h.Begin();
        Require(h.Session.Workspace.Camera.Position == new PointD(300, -100) && h.Session.Workspace.Windows.SequenceEqual(before)
            && h.Session.Workspace.Bookmarks.Count == 1, "Resume forgot logical session.");
    }
    private static void Inherited()
    {
        using var h = new Harness(); var identity = h.Native[42].Identity!.Value;
        h.Saved = RecoveryJournal.Empty().WithEntries([new(Guid.NewGuid(), identity, h.Capture(identity), 123, true)]);
        Require(!h.Session.Start(h.Selected(), Now) && h.Session.Status == CanvasSessionStatus.RecoveryRequired
            && h.Restorations == 0 && h.Saved.Entries.Count == 1 && h.Batches == 0, "Start moved an inherited session.");
    }
    private static void Unsupported()
    {
        using var multi = new Harness(true); Require(!multi.Session.Start(multi.Selected(), Now) && multi.Batches == 0, "Multiscreen started.");
        using var top = new Harness(); top.Native[42] = top.Native[42] with { IsTopMost = true };
        Require(!top.Session.Start(top.Selected(), Now) && top.Saved is null, "Topmost selection registered.");
    }
    private static void Displays()
    {
        using var h = new Harness();
        Require(h.Session.MatchesDisplay(new DisplayLayout(h.Layout.Monitors)), "Same geometry not recognized.");
        Require(!h.Session.MatchesDisplay(new DisplayLayout([new("primary", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1080), 144)])), "DPI change not recognized.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
