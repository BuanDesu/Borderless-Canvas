using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Bookmarks;

/// <summary>Shared position labels, independent of windows, camera history and display topology.</summary>
public sealed class BookmarkCollection
{
    public const int MaxNameLength = 256;
    private readonly Dictionary<Guid, LocationBookmark> entries = [];
    public IReadOnlyList<LocationBookmark> Items => entries.Values.ToArray();
    public IReadOnlyList<LocationBookmark> Search(string query) => entries.Values
        .Where(entry => entry.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var value = name.Trim();
        if (value.Length > MaxNameLength || value.Any(char.IsControl)) throw new ArgumentException("标签名称限 1–256 个字符，不能包含控制字符。", nameof(name));
        return value;
    }
    public static void Validate(LocationBookmark entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id == Guid.Empty || NormalizeName(entry.Name) != entry.Name) throw new ArgumentException("标签的 ID 或名称无效。");
    }
    public void Replace(IEnumerable<LocationBookmark> source)
    {
        var next = source.ToArray(); foreach (var entry in next) Validate(entry);
        if (next.Select(entry => entry.Id).Distinct().Count() != next.Length) throw new ArgumentException("标签 ID 重复。");
        entries.Clear(); foreach (var entry in next) entries.Add(entry.Id, entry);
    }
    public LocationBookmark Add(string name, PointD position)
    {
        var entry = new LocationBookmark(Guid.NewGuid(), NormalizeName(name), position); entries.Add(entry.Id, entry); return entry;
    }
    public bool Rename(Guid id, string name)
    {
        var value = NormalizeName(name);
        if (!entries.TryGetValue(id, out var entry)) return false;
        entries[id] = entry with { Name = value }; return true;
    }
    public bool Remove(Guid id) => entries.Remove(id);
}

public sealed record BookmarkDocument(int Version, long Revision, IReadOnlyList<LocationBookmark> Entries)
{
    public const int CurrentVersion = 1;
    public void Validate()
    {
        if (Version != CurrentVersion || Revision < 0 || Entries is null) throw new ArgumentException("标签文件版本、修订号或条目无效。");
        new BookmarkCollection().Replace(Entries);
    }
}

public interface IBookmarkStore : IDisposable
{
    BookmarkDocument? Load();
    void Save(BookmarkDocument document);
    string Backup();
}
