using System.Text.Json.Nodes;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Windows.Bookmarks;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class BookmarkStorageChecks
{
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Bookmark JSON round-trips 256 duplicate-name labels with precise positions and stable IDs", RoundTrip),
            ("Concurrent bookmark writers degrade and can retry after the owning process releases its lease", Lease),
            ("Malformed, future, duplicate and incomplete bookmark data is preserved without partial loading", Malformed),
            ("Failed bookmark replacement preserves old bytes and removes its temporary file", ReplaceFailure),
            ("Corrupt bookmark reset creates an exact backup before writing a new usable document", BackupReset),
            ("Oversized bookmark data is rejected but can be backed up and reset without loading all entries", Oversized),
            ("External label changes cannot be silently overwritten by a stale library", ExternalEdit),
            ("Invalid bookmark writes never alter the current complete file", InvalidWrite)
        };
        var failures = 0;
        foreach (var (name, run) in cases)
        {
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
        }
        Console.WriteLine($"{cases.Length - failures}/{cases.Length} bookmark storage checks passed.");
        return failures == 0 ? 0 : 1;
    }
    private static string NewPath(string label) => Path.Combine(Path.GetDirectoryName(StorageChecks.NewPath("bookmarks-" + label))!, "bookmarks.json");
    private static BookmarkDocument Document(int count = 1) => new(BookmarkDocument.CurrentVersion, 12, Array.AsReadOnly(Enumerable.Range(0, count)
        .Select(i => new LocationBookmark(Guid.NewGuid(), "同名区域🪟", new(-i * 1500 - 0.125, i * 800 + 0.25))).ToArray()));
    private static void RoundTrip()
    {
        var path = NewPath("roundtrip"); var document = Document(256);
        using (var store = new JsonBookmarkStore(path)) { Require(store.Load() is null, "New file was not empty."); store.Save(document); }
        using var reopened = new JsonBookmarkStore(path); var loaded = reopened.Load()!;
        Require(loaded.Version == 1 && loaded.Revision == 12 && loaded.Entries.SequenceEqual(document.Entries), "JSON changed labels or order.");
    }
    private static void Lease()
    {
        var path = NewPath("lease"); var owner = new JsonBookmarkStore(path); owner.Load(); owner.Save(Document());
        using var second = new BookmarkLibrary(() => new JsonBookmarkStore(path));
        try { Require(!second.Reload() && !second.CanWrite && !second.CanBackupAndReset, "Concurrent writer acquired the file."); }
        finally { owner.Dispose(); }
        Require(second.Reload() && second.Collection.Items.Count == 1 && second.CanWrite, "Could not reload after lease release.");
    }
    private static void Malformed()
    {
        var path = NewPath("malformed"); using var store = new JsonBookmarkStore(path); store.Load(); store.Save(Document());
        var valid = File.ReadAllText(path); var samples = new List<string> { "", "{partial", "{}", "null" };
        foreach (var mutate in new Action<JsonNode>[] { node => node["Version"] = 999, node => node["Revision"] = -1,
            node => node["Revision"] = null, node => node["Entries"] = null, node => node["Unexpected"] = true,
            node => node["Entries"]![0] = null, node => node["Entries"]![0]!["Id"] = Guid.Empty,
            node => node["Entries"]![0]!["Name"] = " ", node => node["Entries"]![0]!["X"] = null,
            node => node["Entries"]![0]!["Y"] = null,
            node => ((JsonArray)node["Entries"]!).Add(node["Entries"]![0]!.DeepClone()) })
        { var changed = JsonNode.Parse(valid)!; mutate(changed); samples.Add(changed.ToJsonString()); }
        foreach (var value in samples)
        {
            File.WriteAllText(path, value); Throws<InvalidDataException>(() => store.Load());
            Require(File.ReadAllText(path) == value, "Invalid input was rewritten.");
        }
    }
    private static void ReplaceFailure()
    {
        var path = NewPath("replace"); using var store = new JsonBookmarkStore(path); store.Load(); store.Save(Document()); var original = File.ReadAllBytes(path);
        using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var rejected = false;
            try { store.Save(Document(2)); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { rejected = true; }
            Require(rejected, "Destination refusing replacement was overwritten.");
        }
        Require(File.ReadAllBytes(path).SequenceEqual(original) && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0,
            "Failed save damaged old file or leaked temporary.");
    }
    private static void BackupReset()
    {
        var path = NewPath("reset"); const string corrupt = "{broken-but-must-survive"; File.WriteAllText(path, corrupt);
        using var library = new BookmarkLibrary(() => new JsonBookmarkStore(path));
        Require(!library.Reload() && library.CanBackupAndReset && library.Collection.Items.Count == 0 && File.ReadAllText(path) == corrupt, "Corruption did not degrade safely.");
        Require(library.BackupAndReset(), library.Message);
        var backup = Directory.GetFiles(Path.GetDirectoryName(path)!, "bookmarks.json.backup-*.json").Single();
        Require(File.ReadAllText(backup) == corrupt && library.CanWrite && library.Add("新建后", new(10, -20)) is not null, "Backup or new writable library missing.");
        Require(library.Reload() && library.Collection.Items.Single().Name == "新建后", "Reset data could not reopen.");
    }
    private static void Oversized()
    {
        var path = NewPath("oversized"); using (var file = new FileStream(path, FileMode.Create)) file.SetLength(JsonBookmarkStore.MaxBytes + 1);
        using var library = new BookmarkLibrary(() => new JsonBookmarkStore(path));
        Require(!library.Reload() && library.CanBackupAndReset && new FileInfo(path).Length == JsonBookmarkStore.MaxBytes + 1, "Oversize input was loaded or altered.");
        Require(library.BackupAndReset() && library.Reload(), library.Message);
        var backup = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*.json").Single();
        Require(new FileInfo(backup).Length == JsonBookmarkStore.MaxBytes + 1 && library.Collection.Items.Count == 0, "Oversize backup was truncated.");
    }
    private static void ExternalEdit()
    {
        var path = NewPath("external"); using var library = new BookmarkLibrary(() => new JsonBookmarkStore(path)); Require(library.Reload(), library.Message);
        var entry = library.Add("Original", new(1, 2))!; var node = JsonNode.Parse(File.ReadAllText(path))!;
        node["Entries"]![0]!["Name"] = "External"; var external = node.ToJsonString(); File.WriteAllText(path, external);
        Require(!library.Rename(entry.Id, "Stale overwrite") && library.Collection.Items.Single().Name == "Original"
            && File.ReadAllText(path) == external && !library.CanWrite, "Stale library overwrote outside changes.");
        Require(library.Reload() && library.Collection.Items.Single().Name == "External" && library.CanWrite, "External changes could not be reloaded.");
    }
    private static void InvalidWrite()
    {
        var path = NewPath("invalidwrite"); using var store = new JsonBookmarkStore(path); store.Load(); var document = Document(); store.Save(document);
        var bytes = File.ReadAllBytes(path);
        foreach (var invalid in new[] { document with { Version = 99 }, document with { Revision = -1 },
            document with { Entries = [document.Entries[0], document.Entries[0]] }, document with { Entries = [document.Entries[0] with { Name = "a\nb" }] } })
        { Throws<ArgumentException>(() => store.Save(invalid)); Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Invalid write changed old bytes."); }
    }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
