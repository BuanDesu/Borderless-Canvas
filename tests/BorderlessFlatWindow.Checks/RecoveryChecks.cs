using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

internal static class RecoveryChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recovery registration persists pending then marked/armed state before permitting movement", Registration);
        yield return ("Initial journal write failure cannot mark or authorize a window", InitialWriteFailure);
        yield return ("Marker failure rolls back registration without authorizing movement", MarkerFailure);
        yield return ("Arming write failure removes marker and cannot permit movement", ArmedWriteFailure);
        yield return ("Repeat registration preserves the first placement and verifies current ownership", Duplicate);
        yield return ("Previous-session records block management and are not silently restored on Dispose", Inherited);
        yield return ("Unarmed crash records are cleaned without any window placement operation", Unarmed);
        yield return ("Normal Dispose restores fresh registrations and releases the exclusive lease", NormalExit);
        yield return ("Partial recovery retains only failed entries and can be retried", Partial);
        yield return ("Persistence failure during recovery preserves retryable evidence", RecoveryWriteFailure);
        yield return ("Lost ownership disables movement immediately", LostOwnership);
        yield return ("Malformed journal identities, geometry and duplicate handles fail closed", Invalid);
    }
    private static readonly NativeWindowIdentity Identity = new(42, 100, 123456789);
    private static OriginalWindowState Original => new(new(0, 1, -1, -1, -1, -1, 100, 100, 740, 580), new PixelRect(100, 100, 740, 580));
    private sealed class Store : IRecoveryJournalStore
    {
        internal RecoveryJournal? Saved;
        internal int Saves;
        internal int FailAt = -1;
        internal bool Disposed;
        internal readonly List<string> Events = [];
        public RecoveryJournal? Load() => Saved;
        public void Save(RecoveryJournal journal)
        {
            Saves++; if (Saves == FailAt) throw new IOException("Controlled disk failure.");
            Events.Add(journal.Entries.LastOrDefault()?.Armed == true ? "save-armed" : "save-pending-or-empty");
            Saved = journal;
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Windows(Store store) : IRecoveryWindowPort
    {
        internal bool Marked;
        internal bool FailMark;
        internal int Captures;
        internal int Restores;
        internal RecoveryOutcome Outcome = RecoveryOutcome.Restored;
        public OriginalWindowState Capture(NativeWindowIdentity identity) { Captures++; return Original; }
        public void Mark(RecoveryEntry entry, string name)
        {
            Require(store.Saved?.Entries.Single().Armed == false, "Marker set before durable pending record.");
            store.Events.Add("mark");
            Marked = true;
            if (FailMark) throw new InvalidOperationException("Controlled marker failure.");
        }
        public bool Matches(RecoveryEntry entry, string name) => Marked;
        public void RemoveMarker(RecoveryEntry entry, string name) => Marked = false;
        public RecoveryResult Restore(RecoveryEntry entry, string name)
        { Restores++; if (Outcome != RecoveryOutcome.Failed) Marked = false; return new(entry.Id, Outcome, "Controlled native outcome."); }
    }
    private static void Registration()
    {
        var store = new Store(); var windows = new Windows(store); using var session = new RecoverySession(store, windows);
        var entry = session.Register(Identity);
        Require(store.Events.SequenceEqual(new[] { "save-pending-or-empty", "mark", "save-armed" })
            && entry.Armed && session.CanMove(entry.Id), "Write-ahead sequence changed.");
    }
    private static void InitialWriteFailure()
    {
        var store = new Store { FailAt = 1 }; var windows = new Windows(store); using var session = new RecoverySession(store, windows);
        Throws<IOException>(() => session.Register(Identity));
        Require(!windows.Marked && session.Journal.Entries.Count == 0 && store.Saved is null, "Failed initial write changed ownership.");
    }
    private static void MarkerFailure()
    {
        var store = new Store(); var windows = new Windows(store) { FailMark = true }; using var session = new RecoverySession(store, windows);
        Throws<InvalidOperationException>(() => session.Register(Identity));
        Require(!windows.Marked && store.Saved!.Entries.Count == 0 && windows.Restores == 0, "Marker failure retained armed state.");
    }
    private static void ArmedWriteFailure()
    {
        var store = new Store { FailAt = 2 }; var windows = new Windows(store); using var session = new RecoverySession(store, windows);
        Throws<IOException>(() => session.Register(Identity));
        Require(!windows.Marked && session.Journal.Entries.Count == 0 && store.Saved!.Entries.Count == 0, "Failed arming granted ownership.");
    }
    private static void Duplicate()
    {
        var store = new Store(); var windows = new Windows(store); using var session = new RecoverySession(store, windows);
        var first = session.Register(Identity); var again = session.Register(Identity);
        Require(first == again && windows.Captures == 1 && store.Saves == 2, "Original placement overwritten.");
    }
    private static RecoveryJournal Pending(bool armed = true) => RecoveryJournal.Empty().WithEntries([
        new(Guid.NewGuid(), Identity, Original, 123, armed)
    ]);
    private static void Inherited()
    {
        var store = new Store { Saved = Pending() }; var windows = new Windows(store) { Marked = true };
        using (var session = new RecoverySession(store, windows))
        {
            Throws<InvalidOperationException>(() => session.Register(Identity));
            Require(!session.CanMove(store.Saved.Entries[0].Id), "Inherited session authorized movement.");
        }
        Require(windows.Restores == 0 && store.Saved.Entries.Count == 1 && store.Disposed, "Loading pending recovery moved a window.");
    }
    private static void Unarmed()
    {
        var store = new Store { Saved = Pending(false) }; var windows = new Windows(store) { Marked = true };
        using var session = new RecoverySession(store, windows); var result = session.RecoverAll();
        Require(result.IsComplete && windows.Restores == 0 && !windows.Marked
            && result.Results.Single().Outcome == RecoveryOutcome.ReleasedUnarmed, "Unarmed record caused restoration.");
    }
    private static void NormalExit()
    {
        var store = new Store(); var windows = new Windows(store);
        using (var session = new RecoverySession(store, windows)) session.Register(Identity);
        Require(windows.Restores == 1 && store.Saved!.Entries.Count == 0 && store.Disposed, "Normal exit did not restore/release.");
    }
    private static void Partial()
    {
        var original = Pending(); var second = original.Entries[0] with { Id = Guid.NewGuid(), Identity = new(43, 101, 12345) };
        var store = new Store { Saved = original.WithEntries(original.Entries.Append(second)) };
        var port = new SelectivePort(); using var session = new RecoverySession(store, port);
        var partial = session.RecoverAll();
        Require(partial.Remaining == 1 && store.Saved.Entries.Single().Id == second.Id, "Partial recovery discarded failed entry.");
        Require(!session.CanMove(second.Id), "Incomplete recovery still authorized movement.");
        port.FailSecond = false; Require(session.RecoverAll().IsComplete, "Retry did not finish recovery.");
    }
    private sealed class SelectivePort : IRecoveryWindowPort
    {
        internal bool FailSecond = true;
        public OriginalWindowState Capture(NativeWindowIdentity identity) => Original;
        public void Mark(RecoveryEntry entry, string name) { }
        public bool Matches(RecoveryEntry entry, string name) => true;
        public void RemoveMarker(RecoveryEntry entry, string name) { }
        public RecoveryResult Restore(RecoveryEntry entry, string name) => new(entry.Id,
            entry.Identity.Handle == 43 && FailSecond ? RecoveryOutcome.Failed : RecoveryOutcome.Restored, "Controlled result");
    }
    private static void RecoveryWriteFailure()
    {
        var store = new Store { Saved = Pending(), FailAt = 1 }; var windows = new Windows(store) { Marked = true };
        using var session = new RecoverySession(store, windows);
        Throws<IOException>(() => session.RecoverAll());
        Require(session.Journal.Entries.Count == 1 && store.Saved.Entries.Count == 1, "Failed progress write lost durable entry.");
    }
    private static void LostOwnership()
    {
        var store = new Store(); var windows = new Windows(store); using var session = new RecoverySession(store, windows);
        var entry = session.Register(Identity); windows.Marked = false;
        Require(!session.CanMove(entry.Id), "Lost marker still permitted movement.");
        Throws<InvalidOperationException>(() => session.Register(Identity));
    }
    private static void Invalid()
    {
        var pending = Pending(); var first = pending.Entries.Single();
        var bad = new[]
        {
            pending with { SessionId = Guid.Empty }, pending with { Revision = -1 },
            pending.WithEntries([first with { Identity = default }]), pending.WithEntries([first with { Marker = 0 }]),
            pending.WithEntries([first with { Original = Original with { PhysicalBounds = default } }]),
            pending.WithEntries([first with { Original = Original with { Placement = Original.Placement with { ShowCommand = 0 } } }]),
            pending.WithEntries([first, first with { Id = Guid.NewGuid() }])
        };
        foreach (var journal in bad)
        {
            var store = new Store { Saved = journal }; var windows = new Windows(store);
            Throws<InvalidDataException>(() => new RecoverySession(store, windows));
            Require(store.Disposed && windows.Captures == 0 && windows.Restores == 0, "Invalid journal touched windows or retained lease.");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
