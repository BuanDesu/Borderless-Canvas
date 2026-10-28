using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Bookmarks;
using BorderlessFlatWindow.Windows.Interop;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class WorkspaceNativeChecks
{
    internal static int Run(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            try { WorkspaceLauncher.Start(new(Path.Combine(output, "missing.exe"), [])); throw new InvalidOperationException("Missing launch was accepted."); }
            catch (FileNotFoundException) { }
            var invalid = Path.Combine(output, "invalid.exe"); File.WriteAllText(invalid, "Synthetic non-executable fixture.");
            try { WorkspaceLauncher.Start(new(invalid, [])); throw new InvalidOperationException("Invalid executable was accepted."); }
            catch (System.ComponentModel.Win32Exception) { }
            Console.WriteLine("PASS Missing and invalid owned launch paths fail without creating an application");
            using var h = new Harness(output);
            var arguments = new[] { "two words", "literal\"quote", "& literal", "中文", "C:\\Synthetic\\document with spaces.txt" };
            h.Start("A", arguments); h.Start("B", []); h.Wait(() => h.Snapshot().Entries.Count == 2);
            Require(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(output, "A.args.json")))!.SequenceEqual(arguments), "Structured launch arguments changed.");
            h.CreateSession(startExisting: true);
            var sessionId = Guid.NewGuid();
            var snapshot = h.Capture(sessionId);
            var bookmarkPath = Path.Combine(output, "bookmarks.json"); Guid id;
            using (var library = new BookmarkLibrary(() => new JsonBookmarkStore(bookmarkPath)))
            { library.Reload(); id = library.SaveWorkspace("Synthetic A/B", PointD.Zero, snapshot)!.Id; }
            var saved = new LocationBookmark(id, "Synthetic A/B", PointD.Zero, snapshot);
            var original = h.Session!.Workspace.Windows.ToArray();
            h.MoveOwn("A", new(340, 260, 760, 570)); h.MoveOwn("B", new(820, 290, 1250, 610));
            h.Wait(() => { h.Observe(); return h.Session.Workspace.Windows.Any(w => original.All(o => o.Bounds != w.Bounds)); });
            h.Restore(saved, sessionId);
            Require(h.Starts == 2, "Live A/B restore created new processes."); h.VerifyLayout(saved);
            Console.WriteLine("PASS Own cross-process A/B reuse restores saved position and dimensions with no new instances; ArgumentList remains literal");

            h.Close("A"); h.Observe(); h.Restore(saved, sessionId);
            Require(h.Starts == 3, "Closing one member did not start exactly one missing application."); h.VerifyLayout(saved);
            Console.WriteLine("PASS One closed own application is relaunched once while the surviving application is reused");

            h.Close("A"); h.Close("B"); h.Observe(); h.Session.Dispose();
            using (var library = new BookmarkLibrary(() => new JsonBookmarkStore(bookmarkPath)))
            { Require(library.Reload(), "Saved workspace could not reopen."); saved = library.Collection.Items.Single(); }
            h.CreateSession(startExisting: false); h.Restore(saved, Guid.NewGuid());
            Require(h.Starts == 5, "Restarted manager did not launch exactly the two missing applications."); h.VerifyLayout(saved);
            Console.WriteLine("PASS Both closed own applications and a recreated manager restore from the persisted workspace without HWND/PID persistence");

            h.CheckCancelledLaunch(saved);
            h.Restore(saved, Guid.NewGuid()); h.VerifyLayout(saved);

            // Register the minimized state with a fresh session, then restore it nonactivatingly.
            h.Session.Pause(); h.Session.Dispose(); h.Minimize("A"); h.CreateSession(startExisting: false);
            h.Restore(saved, Guid.NewGuid()); h.VerifyLayout(saved); h.Session.Pause();
            h.Wait(() => h.Snapshot().Entries.Any(e => e.Observation.IsMinimized));
            using var journal = new JsonRecoveryJournalStore(h.JournalPath);
            Require((journal.Load()?.Entries.Count ?? 0) == 0, "Native workspace recovery retained verified records.");
            Console.WriteLine("PASS Minimized own window restores through durable registration; pause restores original minimize state and clears the journal");
            h.Close("A"); h.Close("B"); h.Observe(); h.Session.Dispose();
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(WorkspaceNativeChecks).Assembly.Location);
            start.ArgumentList.Add("--workspace-restart-child"); start.ArgumentList.Add(output);
            using (var child = Process.Start(start) ?? throw new InvalidOperationException("Own restart verifier did not start."))
            {
                var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                Require(child.WaitForExit(40000), "Own restart verifier timed out; inspect its bounded fixtures before retrying.");
                Require(child.ExitCode == 0, "Cross-process manager restart failed: " + stderr.GetAwaiter().GetResult());
                Console.Write(stdout.GetAwaiter().GetResult());
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, ownApplications = 2, launches = h.Starts,
                desktopEnumeration = false, userApplications = false, savedMembers = saved.Layout!.Windows.Count }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Workspace native check: " + error.Message); return 1; }
    }
    internal static int RestartChild(string output)
    {
        try
        {
            using var h = new Harness(output, reuseSources: true);
            using var library = new BookmarkLibrary(() => new JsonBookmarkStore(Path.Combine(output, "bookmarks.json")));
            Require(library.Reload(), "Restarted process could not read its saved workspace.");
            var saved = library.Collection.Items.Single();
            h.CreateSession(startExisting: false); h.Restore(saved, Guid.NewGuid()); h.VerifyLayout(saved);
            Require(h.Starts == 2 && saved.Layout!.Windows.Count == 2, "Restarted process did not launch exactly two saved members.");
            Console.WriteLine("PASS A separate verifier process reloads the durable workspace, starts exactly two owned applications and restores their saved native layouts");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Own restarted verifier: " + error.Message); return 1; }
    }
    private sealed class Harness : IWindowObservationSource, IWorkspaceRestorePort, IDisposable
    {
        private readonly string directory;
        private readonly Dictionary<string, Process> processes = [];
        private WindowInspectionService inspection;
        private readonly DisplayLayout layout = new DesktopDisplayCatalog().ReadDisplays();
        internal ManagedCanvasSession? Session;
        internal int Starts;
        internal string JournalPath = "";
        private int generation;
        internal Harness(string directory, bool reuseSources = false)
        {
            this.directory = directory; inspection = new(this, new(0));
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var source = Path.Combine(root, "tests/BorderlessFlatWindow.WorkspaceSource/bin", configuration, "net10.0-windows");
            void CopyOrVerify(string from, string to)
            {
                if (!reuseSources) { File.Copy(from, to, false); return; }
                using var original = File.OpenRead(from); using var retained = File.OpenRead(to);
                Require(System.Security.Cryptography.SHA256.HashData(original).SequenceEqual(System.Security.Cryptography.SHA256.HashData(retained)), "Restart fixture payload changed.");
            }
            foreach (var extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                CopyOrVerify(Path.Combine(source, "BorderlessFlatWindow.WorkspaceSource" + extension), Path.Combine(directory, "BorderlessFlatWindow.WorkspaceSource" + extension));
            foreach (var name in new[] { "A", "B" }) CopyOrVerify(Path.Combine(source, "BorderlessFlatWindow.WorkspaceSource.exe"), Path.Combine(directory, name + ".exe"));
        }
        internal void Start(string name, IReadOnlyList<string> args)
        {
            if (processes.TryGetValue(name, out var existing) && !existing.HasExited) throw new InvalidOperationException("Fixture duplicate process start.");
            existing?.Dispose();
            var pid = WorkspaceLauncher.Start(new(Path.Combine(directory, name + ".exe"), args));
            processes[name] = Process.GetProcessById(pid); Starts++;
        }
        public void Launch(WorkspaceLaunch launch)
        {
            var name = Path.GetFileNameWithoutExtension(launch.Executable);
            if (name is not ("A" or "B") || !string.Equals(launch.Executable, Path.Combine(directory, name + ".exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture refused a non-owned executable.");
            Start(name, launch.Arguments);
        }
        private nint Handle(string name)
        {
            if (!processes.TryGetValue(name, out var process) || process.HasExited) return 0;
            var ready = Path.Combine(directory, name + ".ready");
            if (!File.Exists(ready)) return 0;
            var text = File.ReadAllText(ready).Split(':');
            if (text.Length != 2 || !int.TryParse(text[0], out var pid) || pid != process.Id || !long.TryParse(text[1], out var value)) return 0;
            var handle = (nint)value;
            NativeMethods.GetWindowThreadProcessId(handle, out var nativeProcess);
            return nativeProcess == (uint)process.Id && NativeWindowIdentityGuard.IsCurrent(new(handle, nativeProcess, process.StartTime.ToUniversalTime().ToFileTimeUtc())) ? handle : 0;
        }
        public IReadOnlyList<WindowObservation> ReadWindows()
        {
            var result = new List<WindowObservation>(); var source = new WindowsWindowObservationSource();
            foreach (var name in new[] { "A", "B" }) if (Handle(name) is var handle && handle != 0) result.Add(source.ReadWindow(handle));
            return result;
        }
        internal WindowCatalogSnapshot Snapshot() => inspection.Inspect();
        internal void Observe() => Session?.Observe(Snapshot(), 0, DateTimeOffset.UtcNow);
        internal void CreateSession(bool startExisting)
        {
            Require(layout.SupportsCanvas, "Native fixture requires uniform known DPI.");
            inspection = new(this, new(0)); JournalPath = Path.Combine(directory, "recovery-" + ++generation + ".json");
            Session = new(() => new(new JsonRecoveryJournalStore(JournalPath), new WindowsRecoveryWindowPort(0)), new NativeWindowMotionPort(), layout);
            Require(Session.Start(startExisting ? Snapshot().Entries : [], DateTimeOffset.UtcNow), Session.Message);
            Wait(() => { Observe(); return !Session.HasPending; });
        }
        internal WorkspaceSnapshot Capture(Guid session) => new(session, layout.Primary.EffectiveDpi!.Value, Array.AsReadOnly(Session!.Workspace.Windows.Select(w =>
        {
            var entry = Snapshot().Entries.Single(e => e.Id == w.Id); var exe = NativeWindowIdentityGuard.Executable(entry.Observation.Identity!.Value)!;
            return new WorkspaceMember(Guid.NewGuid(), w.Id, w.Title, exe, entry.Observation.ClassName, w.Bounds, WorkspaceLauncher.Default(exe));
        }).ToArray()));
        private WorkspaceCandidate[] Candidates() => Snapshot().Entries.Where(e => e.Id is not null && e.Observation.Identity is not null).Select(e => new WorkspaceCandidate(e.Id!.Value,
            e.Observation.Identity!.Value, NativeWindowIdentityGuard.Executable(e.Observation.Identity.Value), e.Observation.ClassName, e.Observation.IsMinimized,
            e.Admission.IsAdmitted || e.Admission.Reason == WindowExclusionReason.Minimized)).ToArray();
        internal void Restore(LocationBookmark bookmark, Guid session)
        {
            Require(Session!.NavigateWorkspace(bookmark.CameraPosition, DateTimeOffset.UtcNow, 0, animate: false), "Camera handoff failed.");
            Session.BeginWorkspaceRestore(); var task = new WorkspaceRestorer(this);
            task.Begin(bookmark, session, DateTimeOffset.UtcNow, member => Session.ProjectWorkspaceMember(bookmark, member));
            Wait(() => { Observe(); task.Advance(Candidates(), DateTimeOffset.UtcNow); return !task.Active; }, 35000);
            Session.EndWorkspaceRestore();
            Require(task.Results.All(r => r.Success), "Not all own workspace members restored: " + string.Join("; ", task.Results.Select(r => r.State)));
        }
        private InspectedWindow Entry(WorkspaceCandidate candidate) => Snapshot().Entries.Single(e => e.Id == candidate.Id && e.Observation.Identity == candidate.Identity);
        public bool RestoreMinimized(WorkspaceCandidate candidate) => Session!.RestoreWorkspaceMinimized(Entry(candidate));
        public bool Apply(WorkspaceCandidate candidate, RectD bounds) => Session!.ApplyWorkspaceMember(Entry(candidate), bounds, DateTimeOffset.UtcNow);
        public WorkspacePlacement Check(WorkspaceCandidate candidate) => Session!.CheckWorkspaceMember(candidate.Id, candidate.Identity);
        public void CancelPrepared() => Session?.CancelWorkspacePrepared();
        internal void CheckCancelledLaunch(LocationBookmark saved)
        {
            Close("A"); Observe();
            var starts = Starts;
            Session!.BeginWorkspaceRestore(); var task = new WorkspaceRestorer(this);
            task.Begin(saved, Guid.NewGuid(), DateTimeOffset.UtcNow, member => Session.ProjectWorkspaceMember(saved, member));
            task.Advance(Candidates(), DateTimeOffset.UtcNow);
            Require(Starts == starts + 1, "Cancellation fixture did not start its missing owned application once.");
            task.Cancel(); Session.EndWorkspaceRestore();
            Wait(() => Snapshot().Entries.Count == 2);
            var handle = Handle("A"); var original = Snapshot().Entries.Single(e => e.Observation.Handle == handle).Observation.Bounds;
            for (var i = 0; i < 12; i++) { Observe(); task.Advance(Candidates(), DateTimeOffset.UtcNow); System.Windows.Forms.Application.DoEvents(); }
            Require(!processes["A"].HasExited && Starts == starts + 1 && !task.Active
                && Snapshot().Entries.Single(e => e.Observation.Handle == handle).Observation.Bounds == original,
                "Cancellation killed, duplicated or moved a late owned window.");
            Console.WriteLine("PASS Cancelling after an owned launch keeps the late application alive, ignores later readiness and never submits its saved layout");
        }
        internal void VerifyLayout(LocationBookmark saved)
        {
            var observed = Snapshot();
            foreach (var member in saved.Layout!.Windows)
            {
                var entry = observed.Entries.Single(e => string.Equals(NativeWindowIdentityGuard.Executable(e.Observation.Identity!.Value), member.Executable, StringComparison.OrdinalIgnoreCase));
                var expected = new RectD(member.Bounds.Position + layout.CanvasWorkArea.Position - saved.CameraPosition, member.Bounds.Size);
                Require(entry.Observation.Bounds == expected && !entry.Observation.IsMinimized, "Own native layout differs from saved bounds.");
            }
        }
        internal void MoveOwn(string name, PixelRect target)
        {
            var entry = Snapshot().Entries.Single(e => e.Observation.Handle == Handle(name));
            Require(NativeWindowIdentityGuard.IsCurrent(entry.Observation.Identity!.Value), "Own fixture identity changed.");
            Require(NativeMethods.SetWindowPos(entry.Observation.Handle, 0, target.Left, target.Top, target.Width, target.Height, 0x4000 | 0x0010 | 0x0004), "Own synthetic external move failed.");
        }
        internal void Minimize(string name)
        {
            var handle = Handle(name); Require(handle != 0 && NativeMethods.ShowWindowAsync(handle, 7), "Own minimize failed.");
            Wait(() => Snapshot().Entries.Any(e => e.Observation.Handle == handle && e.Observation.IsMinimized));
        }
        internal void Close(string name)
        {
            var handle = Handle(name); if (handle != 0) PostClose(handle, 0x0010, 0, 0);
            if (processes.TryGetValue(name, out var process)) Wait(() => process.HasExited);
        }
        internal void Wait(Func<bool> condition, int milliseconds = 8000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < milliseconds) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
            Require(condition(), "Own fixture readiness/placement timed out.");
        }
        public void Dispose()
        {
            Session?.Dispose();
            foreach (var pair in processes) { try { Close(pair.Key); } finally { pair.Value.Dispose(); } }
        }
    }
    [DllImport("user32.dll", EntryPoint = "PostMessageW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostClose(nint window, uint message, nuint wParam, nint lParam);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
