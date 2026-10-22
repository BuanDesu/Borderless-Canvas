using System.Text.Json;
using System.Text.Json.Nodes;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows.Preferences;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class PreferencesStorageChecks
{
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[] {
            ("Preferences persist only approved choices and reopen without runtime or window data", RoundTrip),
            ("Malformed, incomplete, future, duplicate and invalid preference fields preserve original bytes", InvalidFiles),
            ("Concurrent preference instances load saved choices with one cooperating writer", Lease),
            ("Preference replacement failure preserves the previous document and cleans the temporary", ReplaceFailure),
            ("External preference edits cannot be silently overwritten", ExternalEdit),
            ("Oversized preferences are rejected before parsing or overwriting", Oversized),
            ("Invalid preference writes never replace the saved document", InvalidWrite),
            ("Preference paths cannot alias recovery or bookmarks through case or relative segments", SeparatePaths) };
        var failures = 0;
        foreach (var (name, run) in cases)
        { try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
        Console.WriteLine($"{cases.Length - failures}/{cases.Length} preference storage checks passed."); return failures == 0 ? 0 : 1;
    }
    private static string NewPath() => StorageChecks.NewPath("preferences");
    private static UserPreferences Custom => new(new(false, false, true, 330), ManualNavigationShortcut.ControlAltN, false, false, false);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void RoundTrip()
    {
        var path = NewPath(); using (var store = new JsonPreferencesStore(path))
        {
            Require(store.Load() is null && !File.Exists(path) && store.CanWrite, "Missing preferences were created during load.");
            store.Save(Custom);
        }
        using var reopened = new JsonPreferencesStore(path); Require(reopened.Load() == Custom && reopened.CanWrite, "Preferences did not survive reopening.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var keys = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Require(keys.SequenceEqual(new[] { "Version", "VerticalWheel", "HorizontalWheel", "TwoButtonDrag", "WheelDistance", "Shortcut", "AutomaticAdmission", "FollowWallpaper", "AnimateOverview" }.Order()), "Unexpected runtime/identity fields were persisted.");
    }
    private static void InvalidFiles()
    {
        var path = NewPath(); string valid;
        using (var seed = new JsonPreferencesStore(path)) { seed.Load(); seed.Save(Custom); valid = File.ReadAllText(path); }
        var samples = new List<string> { "{", "[]", "null", "{\"Version\":1," + valid.TrimStart()[1..] };
        foreach (var mutation in new Action<JsonNode>[] { n => n["Version"] = 2, n => n.AsObject().Remove("VerticalWheel"), n => n["AnimateOverview"] = null,
            n => n["Shortcut"] = 7, n => n["WheelDistance"] = 20, n => n["WheelDistance"] = 601,
            n => n["Unknown"] = true, n => n["AutomaticAdmission"] = "true" })
        { var node = JsonNode.Parse(valid)!; mutation(node); samples.Add(node.ToJsonString()); }
        foreach (var sample in samples)
        {
            File.WriteAllText(path, sample); using var store = new JsonPreferencesStore(path);
            Throws<InvalidDataException>(() => store.Load()); Require(!store.CanWrite, "Invalid load retained write capability.");
            Throws<IOException>(() => store.Save(Custom)); Require(File.ReadAllText(path) == sample, "Invalid preferences were overwritten.");
        }
    }
    private static void Lease()
    {
        var path = NewPath(); using var first = new JsonPreferencesStore(path); first.Load(); first.Save(Custom);
        using (var reader = new JsonPreferencesStore(path))
        { Require(reader.Load() == Custom && !reader.CanWrite, "Second instance could not read or acquired a writer."); Throws<IOException>(() => reader.Save(UserPreferences.Default)); }
        first.Dispose(); using var next = new JsonPreferencesStore(path); next.Load(); next.Save(UserPreferences.Default);
        Require(next.Load() == UserPreferences.Default, "Writer lease was not released.");
    }
    private static void ReplaceFailure()
    {
        var path = NewPath(); using var store = new JsonPreferencesStore(path); store.Load(); store.Save(Custom); var before = File.ReadAllBytes(path);
        using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var rejected = false; try { store.Save(UserPreferences.Default); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rejected = true; }
            Require(rejected && File.ReadAllBytes(path).SequenceEqual(before), "Blocked replacement changed the document.");
        }
        Require(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp").Any(), "A failed temporary remains.");
    }
    private static void ExternalEdit()
    {
        var path = NewPath(); using var store = new JsonPreferencesStore(path); store.Load(); store.Save(Custom);
        var changed = JsonNode.Parse(File.ReadAllText(path))!; changed["WheelDistance"] = 600; File.WriteAllText(path, changed.ToJsonString()); var bytes = File.ReadAllBytes(path);
        Throws<IOException>(() => store.Save(UserPreferences.Default)); Require(File.ReadAllBytes(path).SequenceEqual(bytes), "External edit was overwritten.");
        store.Load(); store.Save(UserPreferences.Default); Require(store.Load() == UserPreferences.Default, "Explicit reload could not save.");
    }
    private static void Oversized()
    {
        var path = NewPath(); var bytes = new byte[JsonPreferencesStore.MaxBytes + 1]; File.WriteAllBytes(path, bytes);
        using var store = new JsonPreferencesStore(path); Throws<InvalidDataException>(() => store.Load());
        Throws<IOException>(() => store.Save(Custom)); Require(new FileInfo(path).Length == bytes.Length, "Oversized settings were replaced.");
    }
    private static void InvalidWrite()
    {
        var path = NewPath(); using var store = new JsonPreferencesStore(path); store.Load(); store.Save(Custom); var bytes = File.ReadAllBytes(path);
        Throws<ArgumentException>(() => store.Save(Custom with { Shortcut = (ManualNavigationShortcut)99 }));
        Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Invalid value replaced settings.");
    }
    private static void SeparatePaths()
    {
        var path = NewPath(); var sibling = Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));
        Throws<ArgumentException>(() => JsonPreferencesStore.EnsureSeparateDataFiles(path, sibling, path + ".bookmarks"));
        Throws<ArgumentException>(() => JsonPreferencesStore.EnsureSeparateDataFiles(path, path + ".recovery", path.ToUpperInvariant()));
        Require(!File.Exists(path), "Path validation created data.");
    }
}
