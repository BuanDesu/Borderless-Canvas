using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Bookmarks;

/// <summary>Publishes changes only after durable storage succeeds. Loading cannot move a camera.</summary>
public sealed class BookmarkLibrary(Func<IBookmarkStore> createStore) : IDisposable
{
    private IBookmarkStore? store;
    private long revision;
    private bool disposed;
    public BookmarkCollection Collection { get; } = new();
    public bool CanWrite { get; private set; }
    public bool CanBackupAndReset { get; private set; }
    public string Message { get; private set; } = "标签尚未读取。";
    public bool Reload()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CanWrite = false; CanBackupAndReset = false; store?.Dispose(); store = null;
        try
        {
            store = createStore(); var document = store.Load(); document?.Validate();
            Collection.Replace(document?.Entries ?? []); revision = document?.Revision ?? 0;
            CanWrite = true; Message = $"已读取 {Collection.Items.Count} 个位置标签。"; return true;
        }
        catch (Exception exception) when (Expected(exception))
        {
            CanBackupAndReset = store is not null && exception is InvalidDataException;
            Message = $"标签只读；原文件保留。{exception.Message} 可重新读取"
                + (CanBackupAndReset ? "，或备份原文件并新建。" : "。请检查文件占用或读写权限。");
            return false;
        }
    }
    public LocationBookmark? Add(string name, PointD position)
    {
        try
        {
            var entry = new LocationBookmark(Guid.NewGuid(), BookmarkCollection.NormalizeName(name), position);
            return Commit(Collection.Items.Append(entry), "标签已保存。") ? entry : null;
        }
        catch (ArgumentException exception) { Message = exception.Message; return null; }
    }
    public bool Rename(Guid id, string name)
    {
        try
        {
            var value = BookmarkCollection.NormalizeName(name);
            if (!Collection.Items.Any(entry => entry.Id == id)) { Message = "标签已不存在。"; return false; }
            return Commit(Collection.Items.Select(entry => entry.Id == id ? entry with { Name = value } : entry), "名称已保存；位置不变。");
        }
        catch (ArgumentException exception) { Message = exception.Message; return false; }
    }
    public bool Remove(Guid id)
    {
        if (!Collection.Items.Any(entry => entry.Id == id)) { Message = "标签已不存在。"; return false; }
        return Commit(Collection.Items.Where(entry => entry.Id != id), "标签已删除；窗口和视角保持不变。");
    }
    public LocationBookmark? SaveWorkspace(string name, PointD position, WorkspaceSnapshot snapshot, Guid? replace = null)
    {
        try
        {
            snapshot.Validate();
            snapshot = snapshot with { Windows = Array.AsReadOnly(snapshot.Windows.Select(m => m with
                { Launch = m.Launch is null ? null : m.Launch with { Arguments = Array.AsReadOnly(m.Launch.Arguments.ToArray()) } }).ToArray()) };
            if (replace is { } id && !Collection.Items.Any(e => e.Id == id)) { Message = "工作区已不存在。"; return null; }
            var entry = new LocationBookmark(replace ?? Guid.NewGuid(), BookmarkCollection.NormalizeName(name), position, snapshot);
            var next = replace is { } existing ? Collection.Items.Select(e => e.Id == existing ? entry : e) : Collection.Items.Append(entry);
            return Commit(next, $"已保存 {snapshot.Windows.Count} 个窗口的布局；关闭软件不会删除快照。") ? entry : null;
        }
        catch (ArgumentException error) { Message = error.Message; return null; }
    }
    public bool UpdateWorkspaceMembers(Guid id, IReadOnlyList<WorkspaceMember> members)
    {
        var entry = Collection.Items.FirstOrDefault(e => e.Id == id);
        if (entry?.Layout is null) { Message = "请先保存窗口布局。"; return false; }
        return SaveWorkspace(entry.Name, entry.CameraPosition, entry.Layout with { Windows = Array.AsReadOnly(members.ToArray()) }, id) is not null;
    }
    private bool Commit(IEnumerable<LocationBookmark> entries, string success)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanWrite || store is null) return false;
        try
        {
            var next = new BookmarkDocument(BookmarkDocument.CurrentVersion, checked(revision + 1), Array.AsReadOnly(entries.ToArray()));
            next.Validate(); store.Save(next); Collection.Replace(next.Entries); revision = next.Revision;
            Message = success; return true;
        }
        catch (Exception exception) when (Expected(exception))
        { CanWrite = false; Message = $"未保存，已保留上一次标签。{exception.Message} 请重新读取后重试。"; return false; }
    }
    public bool BackupAndReset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanBackupAndReset || store is null) return false;
        try
        {
            var backup = store.Backup();
            var document = new BookmarkDocument(BookmarkDocument.CurrentVersion, 0, Array.AsReadOnly(Array.Empty<LocationBookmark>()));
            store.Save(document); Collection.Replace([]); revision = 0; CanWrite = true; CanBackupAndReset = false;
            Message = $"已新建空标签库；原文件备份：{backup}"; return true;
        }
        catch (Exception exception) when (Expected(exception)) { Message = $"未新建标签库；原文件保留。{exception.Message}"; return false; }
    }
    private static bool Expected(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException;
    public void Dispose() { if (disposed) return; disposed = true; store?.Dispose(); store = null; CanWrite = false; }
}
