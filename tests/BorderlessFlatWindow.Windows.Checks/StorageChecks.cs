using System.Text.Json.Nodes;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class StorageChecks
{
    internal static int Run()
    {
        var failed = 0;
        var cases = new (string Name, Action Run)[]
        {
            ("Recovery file round-trips negative physical and workspace coordinates", RoundTrip),
            ("Recovery lease prevents a concurrent manager or recovery writer", Lease),
            ("Malformed, missing and future-version recovery data is preserved and rejected", Malformed),
            ("Atomic replacement failure leaves the previous journal intact and removes its temporary", ReplaceFailure),
            ("Oversized recovery files are rejected before deserialization", Oversized),
            ("Repeated atomic saves expose only complete revisions and no window titles", Revisions),
            ("Independent recovery reports malformed data with a controlled exit and preserves its original bytes", RecoveryCommandInvalidData)
        };
        foreach (var (name, action) in cases)
        {
            try { action(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
        }
        Console.WriteLine($"{cases.Length - failed}/{cases.Length} recovery storage checks passed.");
        return failed == 0 ? 0 : 1;
    }
    internal static string NewPath(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/recovery-checks"));
        var directory = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "recovery.json");
    }
    private static RecoveryJournal Journal() => RecoveryJournal.Empty().WithEntries([
        new(Guid.NewGuid(), new NativeWindowIdentity(42, 100, 123456789),
            new(new(0, 1, -1, -1, -1, -1, -1920, -900, -1280, -420), new PixelRect(-1920, -860, -1280, -380)), 123, true)
    ]);
    private static void RoundTrip()
    {
        var path = NewPath("roundtrip"); var journal = Journal();
        using (var store = new JsonRecoveryJournalStore(path)) { Require(store.Load() is null, "New store was not empty."); store.Save(journal); }
        using var reopened = new JsonRecoveryJournalStore(path); var actual = reopened.Load()!;
        Require(actual.SessionId == journal.SessionId && actual.Revision == journal.Revision
            && actual.Entries.SequenceEqual(journal.Entries), "Persistence changed recovery metadata.");
    }
    private static void Lease()
    {
        var path = NewPath("lease");
        using (var first = new JsonRecoveryJournalStore(path))
            Throws<IOException>(() => { using var second = new JsonRecoveryJournalStore(path); });
        using var after = new JsonRecoveryJournalStore(path); after.Save(Journal());
    }
    private static void Malformed()
    {
        var path = NewPath("invalid");
        using var store = new JsonRecoveryJournalStore(path); store.Save(Journal()); var valid = File.ReadAllText(path);
        var future = JsonNode.Parse(valid)!; future["Version"] = 999;
        var nullEntry = JsonNode.Parse(valid)!; nullEntry["Entries"]![0] = null;
        var missingIdentity = JsonNode.Parse(valid)!; missingIdentity["Entries"]![0]!["Created"] = 0;
        var unknown = JsonNode.Parse(valid)!; unknown["Unexpected"] = true;
        foreach (var text in new[] { "", "{partial", "{}", future.ToJsonString(), nullEntry.ToJsonString(), missingIdentity.ToJsonString(), unknown.ToJsonString() })
        {
            File.WriteAllText(path, text); Throws<InvalidDataException>(() => store.Load());
            Require(File.ReadAllText(path) == text, "Corrupt journal overwritten.");
        }
    }
    private static void ReplaceFailure()
    {
        var path = NewPath("replace"); using var store = new JsonRecoveryJournalStore(path);
        var original = Journal(); store.Save(original); var bytes = File.ReadAllBytes(path);
        using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var rejected = false;
            try { store.Save(original.WithEntries([])); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { rejected = true; }
            Require(rejected, "Locked destination was replaced.");
        }
        Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Failed replace damaged old journal.");
        Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "Temporary leaked.");
    }
    private static void Oversized()
    {
        var path = NewPath("oversized"); using var store = new JsonRecoveryJournalStore(path);
        using (var file = new FileStream(path, FileMode.Create)) file.SetLength(8 * 1024 * 1024 + 1);
        Throws<InvalidDataException>(() => store.Load());
    }
    private static void Revisions()
    {
        var path = NewPath("revisions"); using var store = new JsonRecoveryJournalStore(path); var journal = Journal();
        for (var i = 0; i < 12; i++)
        {
            store.Save(journal); Require(store.Load()!.Revision == journal.Revision, "Revision was incomplete.");
            journal = journal.WithEntries(journal.Entries);
        }
        var text = File.ReadAllText(path);
        Require(!text.Contains("Title", StringComparison.OrdinalIgnoreCase) && !text.Contains("ClassName", StringComparison.OrdinalIgnoreCase), "Journal stored user titles.");
    }
    private static void RecoveryCommandInvalidData()
    {
        var path = NewPath("cli-invalid"); const string malformed = "{preserve-this-invalid-recovery-fixture"; File.WriteAllText(path, malformed);
        var configuration = typeof(StorageChecks).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(root, "src", "BorderlessFlatWindow.Recovery", "bin", configuration, "net10.0-windows", "BorderlessFlatWindow.Recovery.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--journal"); start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Recovery command did not start.");
        if (!process.WaitForExit(5000)) { process.Kill(); throw new InvalidOperationException("Recovery command did not exit after malformed data."); }
        Require(process.ExitCode == 1 && process.StandardError.ReadToEnd().Contains("Recovery stopped:") && File.ReadAllText(path) == malformed,
            "Malformed data escaped the command boundary or changed the source file.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
