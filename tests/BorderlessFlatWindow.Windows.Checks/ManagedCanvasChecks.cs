using System.Diagnostics;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Windows.Recovery;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Windows.Bookmarks;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class ManagedCanvasChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Real managed batch preserves world layout, native size, foreground and shell positions", PanAndShell);
        check("Real parked windows focus by moving the whole camera and recover on pause/resume", FocusAndResume);
        check("A real native window refusing a deferred position triggers bounded recovery", RefusedMove);
        check("A real owned dialog pauses management and restores its parked owner", Popup);
        check("Cross-process stalled UI does not block batch submission and late requests cannot defeat recovery", HungChild);
        check("Persisted label jumps use native camera movement while rename and deletion leave windows unchanged", BookmarkNavigation);
        check("A real late window joins an active camera without moving existing windows and restores on pause", LateWindow);
        check("A late native window refusing its planned position pauses and recovers the full group", LateRefused);
    }
    private sealed class Fixture : Form
    {
        internal volatile bool BlockNext;
        internal bool RejectMoves;
        internal Fixture(string title, int x) { Text = title; Bounds = new(x, 180, 440, 310); StartPosition = FormStartPosition.Manual; Opacity = 0; }
        protected override bool ShowWithoutActivation => true;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x46)
            {
                if (BlockNext) { BlockNext = false; Thread.Sleep(2200); }
                if (RejectMoves)
                {
                    var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                    position.Flags |= 2; Marshal.StructureToPtr(position, message.LParam, false);
                }
            }
            base.WndProc(ref message);
        }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
        { internal nint Window; internal nint InsertAfter; internal int X; internal int Y; internal int Width; internal int Height; internal uint Flags; }
    }
    private sealed class Harness : IDisposable
    {
        internal readonly Fixture A = new("BFW managed A", 180);
        internal readonly Fixture B = new("BFW managed B", 800);
        internal readonly DesktopWindowCatalog Catalog = new(0);
        internal readonly ManagedCanvasSession Session;
        internal readonly Rectangle OriginalA;
        internal readonly Rectangle OriginalB;
        internal readonly string JournalPath;
        internal Harness(BookmarkCollection? bookmarks = null)
        {
            A.Show(); B.Show();
            PumpUntil(() => { Catalog.Inspect(); return Catalog.Current.AdmittedWindows.Count(window => window.Handle == A.Handle || window.Handle == B.Handle) == 2; });
            OriginalA = A.Bounds; OriginalB = B.Bounds;
            JournalPath = StorageChecks.NewPath("managed");
            Session = new(() => new(new JsonRecoveryJournalStore(JournalPath), new WindowsRecoveryWindowPort(0)), new NativeWindowMotionPort(), new DesktopDisplayCatalog().ReadDisplays(), bookmarks);
            Begin();
        }
        internal void Begin()
        {
            var selection = Catalog.Inspect().Entries.Where(entry => entry.Observation.Handle == A.Handle || entry.Observation.Handle == B.Handle).ToArray();
            Require(Session.Start(selection, DateTimeOffset.UtcNow), Session.Message);
            Settle();
        }
        internal void Tick(DateTimeOffset? now = null, nint foreground = 0) =>
            Session.Observe(Catalog.Inspect(), foreground == 0 ? WindowEventListener.ForegroundWindow : foreground, now ?? DateTimeOffset.UtcNow);
        internal void Settle() => PumpUntil(() => { Tick(); return !Session.HasPending; });
        public void Dispose() { Session.Dispose(); A.Dispose(); B.Dispose(); }
    }
    private static Dictionary<nint, RectD?> Shell(DesktopWindowCatalog catalog) => catalog.Inspect().Entries
        .Where(entry => entry.Observation.ClassName is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")
        .ToDictionary(entry => entry.Observation.Handle, entry => entry.Observation.Bounds);
    private static void PanAndShell()
    {
        using var h = new Harness(); var shell = Shell(h.Catalog); var foreground = WindowEventListener.ForegroundWindow;
        var world = h.Session.Workspace.Windows.ToArray();
        Require(h.Session.Pan(new(100, 80), DateTimeOffset.UtcNow), h.Session.Message); h.Settle();
        Require(h.A.Left == h.OriginalA.Left - 100 && h.A.Top == h.OriginalA.Top - 80 && h.B.Left == h.OriginalB.Left - 100
            && h.A.Size == h.OriginalA.Size && h.B.Size == h.OriginalB.Size && h.Session.Workspace.Windows.SequenceEqual(world), "Native batch changed size or world layout.");
        Require(foreground == WindowEventListener.ForegroundWindow, "Pan stole focus.");
        var after = Shell(h.Catalog);
        Require(shell.All(item => after.TryGetValue(item.Key, out var bounds) && bounds == item.Value), "Background/taskbar geometry changed.");
    }
    private static void FocusAndResume()
    {
        using var h = new Harness(); var world = h.Session.Workspace.Windows.ToArray();
        h.Session.Navigate(new(10000, 0), DateTimeOffset.UtcNow); h.Settle();
        Require(h.Session.ParkedCount == 2, "Far camera did not park.");
        // Exercise the same foreground observation path used by taskbar/Alt+Tab activation.
        h.Tick(foreground: h.B.Handle); h.Settle();
        Require(h.Session.ParkedCount == 0 && h.Session.Workspace.Windows.SequenceEqual(world), "Foreground did not focus the whole view.");
        var camera = h.Session.Workspace.Camera.Position;
        h.Session.Pause(); Require(h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB, "Pause did not recover originals.");
        h.Begin(); Require(h.Session.Workspace.Camera.Position == camera && h.Session.Workspace.Windows.SequenceEqual(world), "Resume lost layout.");
    }
    private static void BookmarkNavigation()
    {
        var path = Path.Combine(Path.GetDirectoryName(StorageChecks.NewPath("managed-bookmarks"))!, "bookmarks.json");
        using var labels = new BookmarkLibrary(() => new JsonBookmarkStore(path)); Require(labels.Reload(), labels.Message);
        var home = labels.Add("同名位置", PointD.Zero)!; var far = labels.Add("同名位置", new(6000, -5000))!;
        using var h = new Harness(labels.Collection); var windows = h.Session.Workspace.Windows.ToArray(); var foreground = WindowEventListener.ForegroundWindow;
        Require(h.Session.GoToBookmark(far.Id, DateTimeOffset.UtcNow), h.Session.Message); h.Settle();
        Require(h.Session.ParkedCount == 2, "Label jump did not move the camera to its saved region.");
        var a = h.A.Bounds; var b = h.B.Bounds; var camera = h.Session.Workspace.Camera;
        Require(labels.Rename(far.Id, "重命名后") && labels.Reload(), labels.Message);
        Require(h.A.Bounds == a && h.B.Bounds == b && h.Session.Workspace.Camera == camera, "Rename/reload moved native windows.");
        Require(h.Session.GoToBookmark(home.Id, DateTimeOffset.UtcNow), h.Session.Message); h.Settle();
        Require(h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB && h.Session.Workspace.Windows.SequenceEqual(windows), "Label jump changed world layout.");
        Require(h.Session.Back(DateTimeOffset.UtcNow), "Label jump lost navigation history."); h.Settle();
        a = h.A.Bounds; b = h.B.Bounds; camera = h.Session.Workspace.Camera;
        Require(labels.Remove(far.Id) && !h.Session.GoToBookmark(far.Id, DateTimeOffset.UtcNow), "Removed label still navigated.");
        Require(h.A.Bounds == a && h.B.Bounds == b && h.Session.Workspace.Camera == camera && foreground == WindowEventListener.ForegroundWindow,
            "Deletion moved or activated native windows.");
        h.Session.Pause(); Require(h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB, "Label navigation could not recover.");
    }
    private static void LateWindow()
    {
        using var h = new Harness(); h.Session.Navigate(new(300, -160), DateTimeOffset.UtcNow); h.Settle();
        var label = h.Session.Workspace.AddBookmark("接入前"); var camera = h.Session.Workspace.Camera; var history = h.Session.Workspace.PreviousView;
        var world = h.Session.Workspace.Windows.ToArray(); var a = h.A.Bounds; var b = h.B.Bounds; var foreground = WindowEventListener.ForegroundWindow;
        using var late = new Fixture("BFW runtime late C", 400); late.Show();
        PumpUntil(() => { h.Catalog.Inspect(); return h.Catalog.Current.AdmittedWindows.Any(window => window.Handle == late.Handle); });
        var original = late.Bounds; var entry = h.Catalog.Current.Entries.Single(item => item.Observation.Handle == late.Handle);
        Require(h.Session.AddWindows([entry], DateTimeOffset.UtcNow), h.Session.Message); h.Settle();
        var added = h.Session.Workspace.Windows.Single(window => window.Id == entry.Id);
        Require(h.Session.IsRunning && h.Session.Workspace.Windows.Count == 3 && h.A.Bounds == a && h.B.Bounds == b
            && h.Session.Workspace.Camera == camera && h.Session.Workspace.PreviousView == history && h.Session.Workspace.Bookmarks.Single() == label
            && world.All(old => h.Session.Workspace.Windows.Contains(old)) && late.Size == original.Size
            && world.All(old => !NearbyWindowPlacement.OverlapsWithGap(old.Bounds, added.Bounds))
            && foreground == WindowEventListener.ForegroundWindow, "Late native admission changed prior layout, navigation, size or foreground.");
        using (var journal = System.Text.Json.JsonDocument.Parse(File.ReadAllText(h.JournalPath)))
            Require(journal.RootElement.GetProperty("Entries").GetArrayLength() == 3, "Late ownership was not durable.");
        h.Session.Pause();
        Require(h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB && late.Bounds == original, "Late native originals were not restored.");
    }
    private static void LateRefused()
    {
        using var h = new Harness(); h.Session.Pan(new(200, 100), DateTimeOffset.UtcNow); h.Settle();
        using var late = new Fixture("BFW runtime refusing C", 200) { RejectMoves = true }; late.Show();
        PumpUntil(() => { h.Catalog.Inspect(); return h.Catalog.Current.AdmittedWindows.Any(window => window.Handle == late.Handle); });
        var original = late.Bounds; var now = DateTimeOffset.UtcNow;
        var entry = h.Catalog.Current.Entries.Single(item => item.Observation.Handle == late.Handle);
        Require(h.Session.AddWindows([entry], now), h.Session.Message); h.Tick(now.AddMilliseconds(1700));
        Require(!h.Session.IsRunning && h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB && late.Bounds == original,
            "Refused late movement left a partially moved group.");
        late.RejectMoves = false;
    }
    private static void RefusedMove()
    {
        using var h = new Harness(); h.B.RejectMoves = true;
        var now = DateTimeOffset.UtcNow;
        h.Session.Pan(new(300, 0), now); h.Tick(now.AddMilliseconds(1700));
        Require(!h.Session.IsRunning && h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB, "Refused movement left a partial layout.");
        h.B.RejectMoves = false;
    }
    private static void Popup()
    {
        using var h = new Harness(); h.Session.Navigate(new(10000, 0), DateTimeOffset.UtcNow); h.Settle();
        using var popup = new Fixture("BFW managed dialog", 400); popup.Show(h.A);
        PumpUntil(() => { h.Tick(); return !h.Session.IsRunning; });
        Require(h.A.Bounds == h.OriginalA && h.B.Bounds == h.OriginalB, "Popup owner stayed parked.");
    }
    private static void HungChild()
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BorderlessFlatWindow.Windows.Checks.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.ArgumentList.Add("--serve-management-fixtures");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start managed fixtures.");
        try
        {
            var line = ReadLine(child); var fields = line.Split(' ');
            Require(fields.Length == 3 && fields[0] == "READY", "Invalid child response: " + line);
            nint[] handles = [checked((nint)long.Parse(fields[1])), checked((nint)long.Parse(fields[2]))];
            var catalog = new DesktopWindowCatalog(0);
            PumpUntil(() => catalog.Inspect().AdmittedWindows.Count(window => handles.Contains(window.Handle)) == 2);
            var path = StorageChecks.NewPath("hung-managed");
            using var session = new ManagedCanvasSession(() => new(new JsonRecoveryJournalStore(path), new WindowsRecoveryWindowPort(0)),
                new NativeWindowMotionPort(), new DesktopDisplayCatalog().ReadDisplays());
            var selection = catalog.Current.Entries.Where(entry => handles.Contains(entry.Observation.Handle)).ToArray();
            Require(session.Start(selection, DateTimeOffset.UtcNow), session.Message);
            PumpUntil(() => { session.Observe(catalog.Inspect(), 0, DateTimeOffset.UtcNow); return !session.HasPending; });
            var originals = catalog.Current.Entries.Where(entry => handles.Contains(entry.Observation.Handle)).ToDictionary(entry => entry.Observation.Handle, entry => entry.Observation.Bounds);
            child.StandardInput.WriteLine("BLOCKNEXT"); child.StandardInput.Flush(); Require(ReadLine(child) == "ARMED", "Child did not arm stall.");
            var now = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew();
            session.Pan(new(300, 0), now);
            Require(watch.Elapsed < TimeSpan.FromMilliseconds(700), "Deferred batch blocked on the other process UI.");
            session.Observe(catalog.Inspect(), 0, now.AddMilliseconds(1700));
            Require(!session.IsRunning, "Stalled UI remained managed.");
            PumpFor(TimeSpan.FromMilliseconds(2400));
            using (var recovery = new RecoverySession(new JsonRecoveryJournalStore(path), new WindowsRecoveryWindowPort(0)))
                Require(recovery.RecoverAll().IsComplete, "Responsive child could not finish pending recovery.");
            var after = catalog.Inspect();
            Require(after.Entries.Where(entry => handles.Contains(entry.Observation.Handle))
                .All(entry => originals[entry.Observation.Handle] == entry.Observation.Bounds), "A late async move defeated recovery.");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
    }
    internal static int Serve()
    {
        ApplicationConfiguration.Initialize();
        using var a = new Fixture("BFW child managed A", 180); using var b = new Fixture("BFW child managed B", 800);
        a.Show(); b.Show();
        Console.WriteLine($"READY {a.Handle.ToInt64()} {b.Handle.ToInt64()}"); Console.Out.Flush();
        var commands = new Thread(() =>
        {
            while (Console.ReadLine() is { } command)
                if (command == "BLOCKNEXT") { a.BlockNext = true; Console.WriteLine("ARMED"); Console.Out.Flush(); }
        }) { IsBackground = true };
        commands.Start(); System.Windows.Forms.Application.Run(new ApplicationContext(a)); return 0;
    }
    private static string ReadLine(Process child)
    {
        var line = child.StandardOutput.ReadLineAsync(); PumpUntil(() => line.IsCompleted);
        return line.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Child ended: " + child.StandardError.ReadToEnd());
    }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(6)) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Require(done(), "Managed fixture timed out.");
    }
    private static void PumpFor(TimeSpan duration)
    { var watch = Stopwatch.StartNew(); while (watch.Elapsed < duration) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
