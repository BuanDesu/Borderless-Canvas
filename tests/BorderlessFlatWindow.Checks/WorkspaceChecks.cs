using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class WorkspaceChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T00:00:00Z");
    private static readonly Guid Session = Guid.NewGuid();
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Workspace layouts persist independently of live windows and retain IDs across rename/update/delete", Persistence);
        yield return ("Failed workspace save never publishes or overwrites the last durable layout", SaveFailure);
        yield return ("Workspace save freezes member and argument collections", Freeze);
        yield return ("Workspace launches require a structured absolute executable and bounded independent arguments", Validation);
        yield return ("Existing workspace A/B windows are reused without launching or changing saved geometry", Existing);
        yield return ("A missing workspace member launches once and joins without duplicating its surviving peer", Missing);
        yield return ("A restarted manager restores two missing applications from the durable snapshot", Restart);
        yield return ("Repeated workspace opens and rapid switches coalesce an outstanding launch", Coalesce);
        yield return ("Cancelled workspace recovery cannot move late windows or keep advancing while idle", Cancel);
        yield return ("Launch failures and slow readiness terminate with actionable partial results", Failures);
        yield return ("Same-application multiwindow ambiguity never guesses or launches duplicate instances", Ambiguous);
        yield return ("Explicit member selection distinguishes multiple windows of the same application", Explicit);
        yield return ("Workspace identity reuse or closure during placement cannot redirect the operation", Reused);
        yield return ("Minimized workspace windows are restored once before layout submission", Minimized);
        yield return ("Unverifiable, pinned and wrong-class windows are never moved or duplicated", Unsupported);
        yield return ("Document arguments cannot silently identify an already-running different document", DocumentContext);
        yield return ("Reentrant cancellation during a workspace launch or placement cannot continue the old task", ReentrantCancel);
        yield return ("Transient startup windows wait for eligibility rather than failing or starting duplicates", Readiness);
        yield return ("A failed management session cannot keep launching the remaining workspace applications", StoppedSession);
        yield return ("An explicit workspace selection never falls back to a surviving sibling or starts a replacement", LostExplicitSelection);
        yield return ("Workspace title changes do not redirect exact identity and expired tasks ignore late windows", TitlesAndLateWindows);
        yield return ("Reentrant workspace switching cannot let an older launch complete the newer request", ReentrantSwitch);
        yield return ("A partial launch failure preserves successful peers and the durable snapshot", PartialFailure);
    }
    private static WorkspaceMember Member(string name, string? exe = null) => new(Guid.NewGuid(), Guid.NewGuid(), name, exe ?? $"C:\\Synthetic\\{name}.exe", "FixtureWindow",
        new(new(name == "A" ? 100 : 800, 150), new(520, 400)), new(exe ?? $"C:\\Synthetic\\{name}.exe", []));
    private static LocationBookmark Bookmark(params WorkspaceMember[] members) => new(Guid.NewGuid(), "Synthetic workspace", new(-1000, 200), new(Session, 96, members));
    private static WorkspaceCandidate Candidate(WorkspaceMember member, int handle = 10) => new(member.SourceWindow, new(handle, (uint)(1000 + handle), 1), member.Executable, member.WindowClass);
    private sealed class Port : IWorkspaceRestorePort
    {
        internal int Launches, Restores, Checks, Cancels;
        internal bool ThrowLaunch, Reject;
        internal Exception? LaunchError;
        internal WorkspacePlacement Placement = WorkspacePlacement.Confirmed;
        internal readonly Dictionary<Guid, RectD> Applied = [];
        internal Action? DuringLaunch, DuringApply;
        public bool Available { get; set; } = true;
        public void Launch(WorkspaceLaunch launch) { Launches++; DuringLaunch?.Invoke(); if (LaunchError is not null) throw LaunchError; if (ThrowLaunch) throw new IOException("Synthetic failure"); }
        public bool RestoreMinimized(WorkspaceCandidate candidate) { Restores++; return true; }
        public bool Apply(WorkspaceCandidate candidate, RectD bounds) { DuringApply?.Invoke(); if (Reject) return false; Applied[candidate.Id] = bounds; return true; }
        public WorkspacePlacement Check(WorkspaceCandidate candidate) { Checks++; return Placement; }
        public void CancelPrepared() => Cancels++;
    }
    private static WorkspaceRestorer Start(Port port, LocationBookmark bookmark, Guid? session = null)
    { var result = new WorkspaceRestorer(port); result.Begin(bookmark, session ?? Session, Now, m => m.Bounds); return result; }
    private static void Settle(WorkspaceRestorer restorer, params WorkspaceCandidate[] candidates)
    { restorer.Advance(candidates, Now.AddSeconds(1)); restorer.Advance(candidates, Now.AddSeconds(2)); }
    private static void Existing()
    {
        var a = Member("A"); var b = Member("B"); var data = Bookmark(a, b); var port = new Port(); var task = Start(port, data);
        Settle(task, Candidate(a), Candidate(b, 20));
        Require(!task.Active && task.Results.All(r => r.Success) && port.Launches == 0 && port.Applied[a.SourceWindow] == a.Bounds && port.Applied[b.SourceWindow] == b.Bounds, "Existing windows were duplicated or misplaced.");
        var cross = Start(new Port(), Bookmark(a), Guid.NewGuid()); Settle(cross, Candidate(a) with { Id = Guid.NewGuid() });
        Require(cross.Results.Single().Success, "One verified application did not survive manager restart.");
    }
    private static void Missing()
    {
        var a = Member("A"); var b = Member("B"); var port = new Port(); var task = Start(port, Bookmark(a, b));
        for (var i = 0; i < 10; i++) task.Advance([Candidate(b, 20)], Now.AddMilliseconds(i * 100));
        Require(port.Launches == 1 && port.Applied.Count == 1, "Missing target launched repeatedly or peer was lost.");
        Settle(task, Candidate(a) with { Id = Guid.NewGuid(), Identity = new(30, 3000, 2) }, Candidate(b, 20));
        Require(task.Results.All(r => r.Success) && port.Launches == 1 && port.Applied.Count == 2, "Late target did not join exactly once.");
    }
    private static void Restart()
    {
        var a = Member("A"); var b = Member("B"); var saved = Bookmark(a, b); var port = new Port(); var task = Start(port, saved, Guid.NewGuid());
        task.Advance([], Now); Require(port.Launches == 2, "Persisted missing applications did not launch.");
        Settle(task, Candidate(a) with { Id = Guid.NewGuid() }, Candidate(b, 20) with { Id = Guid.NewGuid() });
        Require(task.Results.All(r => r.Success) && saved.Layout!.Windows.Count == 2, "Restart lost snapshot or failed to restore.");
    }
    private static void Coalesce()
    {
        var member = Member("A"); var data = Bookmark(member); var port = new Port(); var task = Start(port, data); task.Advance([], Now);
        Require(!task.Begin(data, Session, Now.AddSeconds(1), m => m.Bounds) && port.Launches == 1, "Repeated click restarted the operation.");
        task.Begin(data with { Id = Guid.NewGuid() }, Session, Now.AddSeconds(1), m => m.Bounds); task.Advance([], Now.AddSeconds(2));
        Require(port.Launches == 1, "Rapid switch duplicated an outstanding launch.");
    }
    private static void Cancel()
    {
        var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a)); task.Advance([], Now); task.Cancel(); var before = port.Cancels;
        for (var i = 0; i < 10000; i++) task.Advance([Candidate(a)], Now.AddSeconds(3));
        Require(!task.Active && port.Applied.Count == 0 && port.Launches == 1 && port.Cancels == before, "Idle/cancelled task kept doing work.");
    }
    private static void Failures()
    {
        var a = Member("A"); var port = new Port { ThrowLaunch = true }; var task = Start(port, Bookmark(a));
        task.Advance([], Now); task.Advance([], Now.AddSeconds(1)); Require(!task.Active && !task.Results[0].Success && port.Launches == 1, "Failed launch retried.");
        var slow = Start(new Port(), Bookmark(a)); slow.Advance([], Now); slow.Advance([], Now.AddSeconds(31));
        Require(!slow.Active && slow.Results[0].State.Contains("超时"), "Slow launch was unbounded.");
        var rejected = Start(new Port { Reject = true }, Bookmark(a)); Settle(rejected, Candidate(a)); Require(!rejected.Results[0].Success, "Rejected placement reported success.");
    }
    private static void Ambiguous()
    {
        var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a), Guid.NewGuid());
        Settle(task, Candidate(a), Candidate(a, 20) with { Id = Guid.NewGuid() });
        Require(!task.Results[0].Success && port.Launches == 0 && port.Applied.Count == 0, "Ambiguous documents were guessed.");
        var b = Member("B", a.Executable); var missing = Start(port, Bookmark(a, b), Guid.NewGuid()); missing.Advance([], Now);
        Require(!missing.Active && port.Launches == 0, "Missing unidentifiable multiwindow context was invented.");
    }
    private static void Explicit()
    {
        var a = Member("A"); var b = Member("B", a.Executable); var one = Candidate(a); var two = Candidate(b, 20); var data = Bookmark(a, b); var port = new Port(); var task = new WorkspaceRestorer(port);
        task.Begin(data, Guid.NewGuid(), Now, m => m.Bounds, new Dictionary<Guid, Guid> { [a.Id] = one.Id, [b.Id] = two.Id }); Settle(task, one, two);
        Require(task.Results.All(r => r.Success) && port.Applied.Count == 2 && port.Launches == 0, "Explicit distinct windows were not respected.");
    }
    private static void Reused()
    {
        var a = Member("A"); var original = Candidate(a); var port = new Port { Placement = WorkspacePlacement.Pending }; var task = Start(port, Bookmark(a));
        task.Advance([original], Now); task.Advance([original with { Identity = new(original.Identity.Handle, 9000, 99) }], Now.AddSeconds(1));
        Require(!task.Results[0].Success && port.Checks == 0 && port.Applied.Count == 1, "Handle reuse redirected a pending operation.");
    }
    private static void Minimized()
    {
        var a = Member("A"); var candidate = Candidate(a) with { Minimized = true }; var port = new Port(); var task = Start(port, Bookmark(a));
        for (var i = 0; i < 5; i++) task.Advance([candidate], Now.AddMilliseconds(100 * i));
        Require(port.Restores == 1 && port.Applied.Count == 0 && port.Launches == 0, "Minimized target duplicated or moved before readiness.");
        Settle(task, candidate with { Minimized = false }); Require(task.Results[0].Success, "Restored target never placed.");
    }
    private static void Unsupported()
    {
        var a = Member("A");
        foreach (var candidate in new[] { Candidate(a) with { Eligible = false }, Candidate(a) with { Executable = null, Id = Guid.NewGuid() }, Candidate(a) with { WindowClass = "Different", Id = Guid.NewGuid() } })
        {
            var port = new Port(); var task = Start(port, Bookmark(a), Guid.NewGuid()); Settle(task, candidate);
            Require(port.Applied.Count == 0 && port.Launches == 0 && !task.Results[0].Success, "Unsafe candidate was moved or duplicated.");
        }
    }
    private static void DocumentContext()
    {
        var a = Member("A"); a = a with { Launch = a.Launch! with { Arguments = ["C:\\Synthetic\\Document A.txt"] } };
        var port = new Port(); var task = Start(port, Bookmark(a), Guid.NewGuid()); Settle(task, Candidate(a));
        Require(port.Applied.Count == 0 && port.Launches == 0, "Arguments falsely proved existing document identity.");
    }
    private static void ReentrantCancel()
    {
        foreach (var launch in new[] { false, true })
        {
            var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a, Member("B")));
            if (launch) port.DuringLaunch = task.Cancel; else port.DuringApply = task.Cancel;
            task.Advance(launch ? [] : [Candidate(a)], Now);
            Require(!task.Active && task.Results.All(r => r.State.Contains("取消")) && port.Launches <= 1, "Old generation continued after cancellation.");
        }
    }
    private static void Readiness()
    {
        var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a), Guid.NewGuid());
        var waiting = Candidate(a) with { Eligible = false, Retryable = true, WindowClass = "Loading" };
        task.Advance([waiting], Now); task.Advance([waiting], Now.AddSeconds(5));
        Require(task.Active && port.Applied.Count == 0 && port.Launches == 0, "Existing startup window failed early or caused a duplicate launch.");
        task.Advance([Candidate(a)], Now.AddSeconds(6)); task.Advance([Candidate(a)], Now.AddSeconds(7));
        Require(task.Results.Single().Success, "Ready window did not resume.");
    }
    private static void StoppedSession()
    {
        var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a, Member("B")));
        port.DuringApply = () => port.Available = false;
        task.Advance([Candidate(a)], Now);
        Require(!task.Active && port.Launches == 0 && task.Results.All(r => !r.Success), "Stopped session launched or confirmed remaining applications.");
    }
    private static void LostExplicitSelection()
    {
        foreach (var survivor in new[] { false, true })
        {
            var a = Member("A"); var chosen = Candidate(a); var sibling = Candidate(a, 20) with { Id = Guid.NewGuid() };
            var port = new Port(); var task = new WorkspaceRestorer(port);
            task.Begin(Bookmark(a), Guid.NewGuid(), Now, m => m.Bounds, new Dictionary<Guid, Guid> { [a.Id] = chosen.Id });
            Settle(task, survivor ? [sibling] : []);
            Require(port.Applied.Count == 0 && port.Launches == 0 && !task.Active && !task.Results.Single().Success,
                "A closed explicitly selected window was silently rebound or relaunched.");
        }
    }
    private static void TitlesAndLateWindows()
    {
        var a = Member("A"); var port = new Port(); var task = Start(port, Bookmark(a));
        Settle(task, Candidate(a) with { Label = "Changed document title" });
        Require(task.Results.Single().Success && port.Launches == 0, "Title change invalidated exact catalog identity.");
        var latePort = new Port(); var late = Start(latePort, Bookmark(a)); late.Advance([], Now);
        late.Advance([Candidate(a)], Now.AddSeconds(31));
        var cancelled = latePort.Cancels;
        for (var i = 0; i < 20; i++) late.Advance([Candidate(a)], Now.AddSeconds(32));
        Require(!late.Active && !late.Results.Single().Success && latePort.Applied.Count == 0 && latePort.Launches == 1 && latePort.Cancels == cancelled,
            "An expired restore moved a late window or resumed polling.");
    }
    private static void ReentrantSwitch()
    {
        var a = Member("A"); var b = Member("B"); var port = new Port(); var task = Start(port, Bookmark(a)); var next = Bookmark(b);
        port.DuringLaunch = () => { port.DuringLaunch = null; task.Begin(next, Session, Now, m => m.Bounds); };
        task.Advance([], Now);
        Require(task.Active && task.WorkspaceId == next.Id && task.Results.Single().State == "等待匹配", "Old launch overwrote the new request.");
        Settle(task, Candidate(b));
        Require(task.Results.Single().Success && port.Applied.Count == 1 && !port.Applied.ContainsKey(a.SourceWindow) && port.Launches == 1,
            "Reentrant switch revived an old target or duplicated launch.");
    }
    private static void PartialFailure()
    {
        foreach (var error in new Exception[] { new FileNotFoundException("Synthetic removed application"), new UnauthorizedAccessException("Synthetic access denied"),
            new System.ComponentModel.Win32Exception(5), new ArgumentException("Synthetic invalid launch") })
        {
            var a = Member("A"); var b = Member("B"); var saved = Bookmark(a, b); var port = new Port { LaunchError = error }; var task = Start(port, saved);
            Settle(task, Candidate(b, 20));
            Require(!task.Active && task.Results.Count(r => r.Success) == 1 && port.Launches == 1 && port.Applied.Count == 1
                && saved.Layout!.Windows.Count == 2, "A failed launch discarded the durable snapshot or successful peer.");
        }
    }
    private sealed class Store : IBookmarkStore
    {
        internal BookmarkDocument? Document; internal bool Fail;
        public BookmarkDocument? Load() => Document;
        public void Save(BookmarkDocument document) { if (Fail) throw new IOException("Synthetic disk failure"); Document = document; }
        public string Backup() => "synthetic.backup";
        public void Dispose() { }
    }
    private static void Persistence()
    {
        var store = new Store(); Guid id;
        using (var first = new BookmarkLibrary(() => store))
        { first.Reload(); id = first.SaveWorkspace("Saved", PointD.Zero, Bookmark(Member("A"), Member("B")).Layout!)!.Id; first.Rename(id, "Renamed"); }
        using var reopened = new BookmarkLibrary(() => store); reopened.Reload(); var saved = reopened.Collection.Items.Single();
        Require(saved.Id == id && saved.Name == "Renamed" && saved.Layout!.Windows.Count == 2, "Restart or rename lost members.");
        Require(reopened.SaveWorkspace(saved.Name, new(20, 30), saved.Layout!, id)!.Id == id, "Explicit update changed stable ID.");
        reopened.Remove(id); Require(store.Document!.Entries.Count == 0, "Deletion failed.");
    }
    private static void SaveFailure()
    {
        var store = new Store(); using var library = new BookmarkLibrary(() => store); library.Reload();
        var saved = library.SaveWorkspace("Saved", PointD.Zero, Bookmark(Member("A")).Layout!)!; store.Fail = true;
        Require(library.SaveWorkspace("Unsaved", PointD.Zero, Bookmark(Member("B")).Layout!, saved.Id) is null && library.Collection.Items.Single() == saved, "Unsaved snapshot published.");
    }
    private static void Freeze()
    {
        var args = new[] { "original" }; var member = Member("A") with { Launch = new("C:\\Synthetic\\A.exe", args) }; var members = new[] { member };
        var store = new Store(); using var library = new BookmarkLibrary(() => store); library.Reload(); var saved = library.SaveWorkspace("Saved", PointD.Zero, new(Session, 96, members))!;
        args[0] = "changed"; members[0] = Member("B");
        Require(saved.Layout!.Windows[0].Label == "A" && saved.Layout.Windows[0].Launch!.Arguments.Single() == "original", "Mutation changed durable data.");
    }
    private static void Validation()
    {
        new WorkspaceLaunch("C:\\Synthetic\\A.exe", ["a b", "literal\"quote", "& not a shell command"]).Validate();
        foreach (var path in new[] { "relative.exe", "cmd /c test", "C:\\Synthetic\\A.cmd", "\"C:\\Synthetic\\A.exe\"", "C:\\Synthetic\\A.exe\n" })
            Require(!WorkspaceLaunch.IsExecutablePath(path), "Unstructured command accepted.");
        Throws(() => new WorkspaceLaunch("C:\\Synthetic\\A.exe", ["bad\0argument"]).Validate());
        Throws(() => new WorkspaceSnapshot(Session, 0, []).Validate());
    }
    private static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected rejection."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
