using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class BookmarkChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Persistent labels exceed 128 entries and allow duplicate names with stable distinct IDs", Many);
        yield return ("Label rename and deletion cannot move a camera, its history or any window", Independent);
        yield return ("Label search is case insensitive and never renumbers or reorders the collection", Search);
        yield return ("Recreated workspaces share labels without losing positions or IDs", Shared);
        yield return ("Label save failure cannot publish an unsaved addition, rename or deletion", SaveFailure);
        yield return ("Reopening a label library restores persisted identities, names and coordinates", Restart);
        yield return ("Corrupt label loading preserves a previously loaded collection and disables writes", Corrupt);
        yield return ("Unavailable label storage degrades without discarding the last loaded labels", Unavailable);
        yield return ("Invalid label names never reach storage or alter existing entries", Names);
        yield return ("Missing label edits do not save or change another identity", Missing);
        yield return ("Backup and reset preserves the original before publishing a writable empty library", Reset);
        yield return ("Failed label backup or reset cannot clear the currently loaded labels", ResetFailure);
        yield return ("Invalid collection replacement is all-or-nothing", Replace);
        yield return ("Replacing and deleting labels retain the exact coordinates of every surviving entry", Coordinates);
    }
    private sealed class Store : IBookmarkStore
    {
        internal BookmarkDocument? Document;
        internal bool FailLoad, FailSave, FailBackup;
        internal int Saves, Backups, Disposes;
        public BookmarkDocument? Load() => FailLoad ? throw new InvalidDataException("Corrupt fixture") : Document;
        public void Save(BookmarkDocument document) { if (FailSave) throw new IOException("Disk fixture unavailable"); Saves++; Document = document; }
        public string Backup() { if (FailBackup) throw new IOException("Backup fixture unavailable"); Backups++; return "fixture.backup.json"; }
        public void Dispose() => Disposes++;
    }
    private static BookmarkLibrary Open(Store store)
    { var library = new BookmarkLibrary(() => store); Require(library.Reload(), library.Message); return library; }
    private static LocationBookmark Add(BookmarkLibrary library, string name = "工作区", double x = -400, double y = 1200)
        => library.Add(name, new(x, y)) ?? throw new InvalidOperationException(library.Message);
    private static void Many()
    {
        var store = new Store(); using var library = Open(store);
        for (var i = 0; i < 256; i++) Add(library, "相同名称", i * 2300, -i * 1300);
        var last = library.Collection.Items[^1]; var first = library.Collection.Items[0];
        Require(library.Collection.Items.Count == 256 && library.Collection.Items.Select(entry => entry.Id).Distinct().Count() == 256, "Fixed slots or duplicate identities.");
        Require(library.Rename(last.Id, "改名") && library.Remove(first.Id), library.Message);
        Require(library.Collection.Items[^1] == last with { Name = "改名" } && store.Document!.Entries.Count == 255, "IDs or coordinates shifted after edit.");
    }
    private static void Independent()
    {
        var store = new Store(); using var library = Open(store); var entry = Add(library);
        var workspace = new CanvasWorkspace(new(1920, 1080), library.Collection);
        workspace.UpsertWindow(new(Guid.NewGuid(), "Fixture", new(new(100, 200), new(640, 480)))); workspace.NavigateTo(new(900, -700));
        var camera = workspace.Camera; var history = workspace.PreviousView; var windows = workspace.Windows.ToArray();
        library.Rename(entry.Id, "修改"); library.Remove(entry.Id);
        Require(workspace.Camera == camera && workspace.PreviousView == history && workspace.Windows.SequenceEqual(windows), "Label edit moved or hid a window.");
    }
    private static void Search()
    {
        var store = new Store(); using var library = Open(store); Add(library, "Editor"); Add(library, "资料"); Add(library, "editor upstairs");
        var before = library.Collection.Items.ToArray(); var matches = library.Collection.Search("  EDITOR  ");
        Require(matches.Count == 2 && matches[0] == before[0] && matches[1] == before[2]
            && library.Collection.Search("不存在").Count == 0 && library.Collection.Items.SequenceEqual(before), "Search mutated or renumbered labels.");
    }
    private static void Shared()
    {
        var store = new Store(); using var library = Open(store); var a = new CanvasWorkspace(new(1920, 1080), library.Collection);
        var entry = Add(library); var b = new CanvasWorkspace(new(2560, 1368), library.Collection);
        a.ResetWindowsAndNavigation(); Require(a.Bookmarks.Single() == entry && b.Bookmarks.Single() == entry, "New display or reset lost shared labels.");
        library.Rename(entry.Id, "新名"); Require(a.Bookmarks.Single().Name == "新名" && b.Bookmarks.Single().Id == entry.Id, "Workspaces copied stale labels.");
    }
    private static void SaveFailure()
    {
        foreach (var edit in new Action<BookmarkLibrary, LocationBookmark>[] { (library, _) => library.Add("Unsaved", new(9, 9)),
            (library, entry) => library.Rename(entry.Id, "Unsaved"), (library, entry) => library.Remove(entry.Id) })
        {
            var store = new Store(); using var library = Open(store); var entry = Add(library); var original = store.Document;
            store.FailSave = true; edit(library, entry);
            Require(library.Collection.Items.Single() == entry && store.Document == original && !library.CanWrite && library.Message.Contains("未保存"), "Unsaved data published.");
            store.FailSave = false; Require(library.Reload() && library.Collection.Items.Single() == entry, "Could not reload durable state.");
        }
    }
    private static void Restart()
    {
        var store = new Store(); Guid id;
        using (var first = Open(store)) { id = Add(first, "重启保留", -12000, 8500).Id; }
        using var second = Open(store); var entry = second.Collection.Items.Single();
        Require(entry.Id == id && entry.Name == "重启保留" && entry.CameraPosition == new PointD(-12000, 8500), "Restart changed persisted state.");
    }
    private static void Corrupt()
    {
        var store = new Store(); using var library = Open(store); var entry = Add(library); store.FailLoad = true; var saves = store.Saves;
        Require(!library.Reload() && library.CanBackupAndReset && !library.CanWrite && library.Collection.Items.Single() == entry, "Corrupt load cleared valid data.");
        Require(library.Add("Forbidden", PointD.Zero) is null && store.Saves == saves, "Corrupt file was overwritten.");
    }
    private static void Unavailable()
    {
        var store = new Store(); var blocked = false; using var library = new BookmarkLibrary(() => blocked ? throw new IOException("Lease busy") : store);
        Require(library.Reload(), library.Message); var entry = Add(library); blocked = true;
        Require(!library.Reload() && !library.CanBackupAndReset && !library.CanWrite && library.Collection.Items.Single() == entry, "Lease failure discarded labels or allowed reset.");
        blocked = false; Require(library.Reload(), "Retry could not reopen store.");
    }
    private static void Names()
    {
        var store = new Store(); using var library = Open(store); var entry = Add(library); var saves = store.Saves;
        foreach (var name in new[] { "", "   ", "a\nb", new string('长', 257) })
            Require(library.Add(name, PointD.Zero) is null && !library.Rename(entry.Id, name), "Invalid name accepted.");
        Require(store.Saves == saves && library.Collection.Items.Single() == entry, "Invalid name reached storage.");
        var trimmed = Add(library, "  空间标签🪟  "); Require(trimmed.Name == "空间标签🪟", "Unicode name or trim changed.");
    }
    private static void Missing()
    {
        var store = new Store(); using var library = Open(store); var entry = Add(library); var saves = store.Saves;
        Require(!library.Remove(Guid.NewGuid()) && !library.Rename(Guid.NewGuid(), "Absent") && store.Saves == saves && library.Collection.Items.Single() == entry, "Missing ID changed another label.");
    }
    private static void Reset()
    {
        var store = new Store(); using var library = Open(store); Add(library); store.FailLoad = true; library.Reload();
        Require(library.BackupAndReset() && store.Backups == 1 && store.Document!.Entries.Count == 0 && library.Collection.Items.Count == 0
            && library.CanWrite && !library.CanBackupAndReset && library.Message.Contains("fixture.backup.json"), "Reset was not backed up or usable.");
    }
    private static void ResetFailure()
    {
        foreach (var failBackup in new[] { false, true })
        {
            var store = new Store(); using var library = Open(store); var entry = Add(library); store.FailLoad = true; library.Reload();
            store.FailBackup = failBackup; store.FailSave = !failBackup;
            Require(!library.BackupAndReset() && library.Collection.Items.Single() == entry && !library.CanWrite && store.Document!.Entries.Single() == entry,
                "Failed reset discarded current state.");
        }
    }
    private static void Replace()
    {
        var collection = new BookmarkCollection(); var entry = collection.Add("Valid", PointD.Zero);
        foreach (var values in new[] { new[] { entry, entry }, new[] { entry with { Id = Guid.Empty } }, new[] { entry with { Name = " " } } })
        {
            try { collection.Replace(values); throw new Exception("Invalid collection accepted."); } catch (ArgumentException) { }
            Require(collection.Items.Single() == entry, "Invalid replacement partially applied.");
        }
    }
    private static void Coordinates()
    {
        var store = new Store(); using var library = Open(store); var a = Add(library, "重复", -1e12, 1e12); var b = Add(library, "重复", 0.125, -0.25);
        library.Rename(a.Id, "Rename"); library.Remove(a.Id); Require(library.Collection.Items.Single() == b, "Edit changed precise coordinates or surviving identity.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
