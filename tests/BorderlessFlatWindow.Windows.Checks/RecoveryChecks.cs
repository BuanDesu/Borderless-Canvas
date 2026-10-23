using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Windows.Checks;

internal static partial class RecoveryChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Normal session exit restores a fixture and clears its durable record and marker", NormalExit);
        check("Recovery returns a minimized managed fixture to its original normal state", Minimized);
        check("Closed windows are skipped by native recovery", Closed);
        check("Missing instance markers prevent restoration even with the same HWND/PID/creation time", MissingMarker);
        check("Native recovery skips changed PID and creation time without moving the target", WrongIdentity);
        check("An unarmed crash record releases its marker without restoring placement", Unarmed);
        check("A killed manager releases its lease; standalone inspect and recovery handle its surviving fixture", CrashRecovery);
        RunDisplayChanges(check);
    }
    private class Fixture : Form
    {
        internal Fixture() { Text = "BFW recovery fixture"; Opacity = 0; Bounds = new(160, 150, 450, 320); }
        protected override bool ShowWithoutActivation => true;
        internal void Open()
        {
            Show();
            System.Windows.Forms.Application.DoEvents();
            FixtureReadiness.Wait(Handle);
        }
    }
    private static NativeWindowIdentity Identity(Fixture fixture) => FixtureReadiness.Wait(fixture.Handle).Identity;
    private static void NormalExit()
    {
        using var fixture = new Fixture(); fixture.Open(); var before = fixture.Bounds;
        var path = StorageChecks.NewPath("native-normal"); var port = new WindowsRecoveryWindowPort(0);
        RecoveryEntry entry;
        using (var session = new RecoverySession(new JsonRecoveryJournalStore(path), port))
        {
            entry = session.Register(Identity(fixture));
            Require(session.CanMove(entry.Id) && session.Journal.Entries.Single().Armed, "Not durably armed.");
            fixture.Location = new(680, 430);
        }
        Require(fixture.Bounds == before && !port.Matches(entry, MarkerName(path)), "Normal exit did not restore placement/ownership.");
        using var store = new JsonRecoveryJournalStore(path); Require(store.Load()!.Entries.Count == 0, "Recovery entry survived success.");
    }
    private static string MarkerName(string path) { using var store = new JsonRecoveryJournalStore(path); return store.Load()!.MarkerName; }
    private static void Closed()
    {
        using var fixture = new Fixture(); fixture.Open();
        using var session = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("closed")), new WindowsRecoveryWindowPort(0));
        session.Register(Identity(fixture)); fixture.Close();
        var report = session.RecoverAll();
        Require(report.IsComplete && report.Results.Single().Outcome == RecoveryOutcome.SkippedClosedOrReused, "Closed window not skipped.");
    }
    private static void Minimized()
    {
        using var fixture = new Fixture(); fixture.Open(); var before = fixture.Bounds;
        using var session = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("minimized")), new WindowsRecoveryWindowPort(0));
        session.Register(Identity(fixture)); fixture.WindowState = FormWindowState.Minimized;
        var report = session.RecoverAll();
        Require(report.IsComplete && report.Results.Single().Outcome == RecoveryOutcome.Restored
            && fixture.WindowState == FormWindowState.Normal && fixture.Bounds == before, "Original normal show state not restored.");
    }
    private static void MissingMarker()
    {
        using var fixture = new Fixture(); fixture.Open(); var port = new WindowsRecoveryWindowPort(0);
        using var session = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("marker-missing")), port);
        var entry = session.Register(Identity(fixture)); port.RemoveMarker(entry, session.Journal.MarkerName);
        fixture.Location = new(680, 430); var changed = fixture.Bounds;
        var report = session.RecoverAll();
        Require(report.Results.Single().Outcome == RecoveryOutcome.SkippedClosedOrReused && fixture.Bounds == changed, "Markerless instance was moved.");
    }
    private static void WrongIdentity()
    {
        using var fixture = new Fixture(); fixture.Open(); var port = new WindowsRecoveryWindowPort(0);
        using var session = new RecoverySession(new JsonRecoveryJournalStore(StorageChecks.NewPath("wrong-id")), port);
        var entry = session.Register(Identity(fixture)); fixture.Location = new(680, 430); var changed = fixture.Bounds;
        foreach (var identity in new[] { entry.Identity with { ProcessId = entry.Identity.ProcessId + 1 },
            entry.Identity with { ProcessCreationFileTime = entry.Identity.ProcessCreationFileTime + 1 } })
            Require(port.Restore(entry with { Identity = identity }, session.Journal.MarkerName).Outcome == RecoveryOutcome.SkippedClosedOrReused
                && fixture.Bounds == changed, "Changed identity was moved.");
    }
    private static void Unarmed()
    {
        using var fixture = new Fixture(); fixture.Open(); var port = new WindowsRecoveryWindowPort(0);
        var identity = Identity(fixture); var path = StorageChecks.NewPath("unarmed");
        var entry = new RecoveryEntry(Guid.NewGuid(), identity, port.Capture(identity), 123456, false);
        var journal = RecoveryJournal.Empty().WithEntries([entry]);
        using (var store = new JsonRecoveryJournalStore(path)) { store.Save(journal); port.Mark(entry, journal.MarkerName); }
        fixture.Location = new(680, 430); var changed = fixture.Bounds;
        using var session = new RecoverySession(new JsonRecoveryJournalStore(path), port);
        var result = session.RecoverAll();
        Require(result.Results.Single().Outcome == RecoveryOutcome.ReleasedUnarmed && fixture.Bounds == changed
            && !port.Matches(entry, journal.MarkerName), "Unarmed record restored a window.");
    }
    private static void CrashRecovery()
    {
        using var fixture = new Fixture(); fixture.Open(); var before = fixture.Bounds;
        var identity = Identity(fixture); var path = StorageChecks.NewPath("crash");
        var self = Path.Combine(AppContext.BaseDirectory, "BorderlessFlatWindow.Windows.Checks.exe");
        using var manager = Start(self, "--capture-fixture", identity.Handle.ToInt64().ToString(), identity.ProcessId.ToString(), path);
        try
        {
            var ready = manager.StandardOutput.ReadLineAsync();
            PumpUntil(() => ready.IsCompleted, TimeSpan.FromSeconds(8));
            Require(ready.GetAwaiter().GetResult() == "READY", "Fixture manager failed: " + ReadErrorIfExited(manager));
            fixture.Location = new(680, 430); var changed = fixture.Bounds;
            var blocked = RunCli(path, inspect: true);
            Require(blocked.Exit == 1 && fixture.Bounds == changed, "Recovery did not respect live manager's lease.");
            manager.Kill(); manager.WaitForExit(); // This exact child process was created above for the crash test.
            using (var store = new JsonRecoveryJournalStore(path))
            {
                var journal = store.Load()!;
                Require(journal.Entries.Single().Armed && new WindowsRecoveryWindowPort(0).Matches(journal.Entries[0], journal.MarkerName),
                    "Crash lost recovery marker or armed journal.");
            }
            var inspection = RunCli(path, inspect: true);
            Require(inspection.Exit == 0 && JsonDocument.Parse(inspection.Output).RootElement.GetProperty("pending").GetInt32() == 1
                && fixture.Bounds == changed, "Inspection moved the fixture or lost its record.");
            var recovered = RunCli(path, inspect: false);
            Require(recovered.Exit == 0, $"Recovery CLI failed: {recovered.Output} {recovered.Error}");
            using var report = JsonDocument.Parse(recovered.Output);
            Require(report.RootElement.GetProperty("Remaining").GetInt32() == 0 && fixture.Bounds == before,
                "Independent recovery did not restore the surviving fixture.");
        }
        finally { if (!manager.HasExited) { manager.Kill(); manager.WaitForExit(); } }
    }
    private static string ReadErrorIfExited(Process process) => process.HasExited ? process.StandardError.ReadToEnd() : "not ready";
    private static (int Exit, string Output, string Error) RunCli(string path, bool inspect)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var executable = Path.Combine(root, "src", "BorderlessFlatWindow.Recovery", "bin", configuration,
            "net10.0-windows", "BorderlessFlatWindow.Recovery.exe");
        using var process = inspect ? Start(executable, "--journal", path, "--inspect") : Start(executable, "--journal", path);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        PumpUntil(() => process.HasExited, TimeSpan.FromSeconds(8));
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
    private static Process Start(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot start recovery test process.");
    }
    internal static int CaptureFixture(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("Invalid fixture arguments.");
            var handle = checked((nint)long.Parse(args[1])); var pid = uint.Parse(args[2]);
            var window = new DesktopWindowCatalog(0).Inspect().Entries.Single(entry => entry.Observation.Handle == handle).Observation;
            if (window.ProcessId != pid || window.Title != "BFW recovery fixture" || window.Identity is null)
                throw new InvalidOperationException("Only an explicitly identified recovery fixture may be marked.");
            using var session = new RecoverySession(new JsonRecoveryJournalStore(args[3]), new WindowsRecoveryWindowPort(0));
            session.Register(window.Identity.Value);
            Console.WriteLine("READY"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite); // Parent intentionally kills this known test child.
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
    private static void PumpUntil(Func<bool> completed, TimeSpan timeout, Func<string>? failure = null)
    {
        var watch = Stopwatch.StartNew();
        while (!completed() && watch.Elapsed < timeout) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Require(completed(), failure?.Invoke() ?? "Recovery subprocess timed out.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
