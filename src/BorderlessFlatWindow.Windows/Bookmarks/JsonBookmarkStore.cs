using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows.Bookmarks;

/// <summary>Exclusive cooperating writer, bounded reads, change detection and same-directory atomic writes.</summary>
public sealed class JsonBookmarkStore : IBookmarkStore
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BorderlessFlatWindow", "bookmarks.json");
    public string FilePath { get; }
    private readonly FileStream lease;
    private byte[]? signature;
    private bool loaded, disposed;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public JsonBookmarkStore(string path)
    {
        FilePath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        lease = new(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private byte[]? Read()
    {
        using var stream = OpenCurrent();
        if (stream is null) return null;
        if (stream.Length > MaxBytes) throw new InvalidDataException("标签文件超过 16 MiB 读取限制。");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int count;
        while ((count = stream.Read(bytes)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw new InvalidDataException("标签文件超过 16 MiB 读取限制。");
            buffer.Write(bytes, 0, count);
        }
        return buffer.ToArray();
    }
    public BookmarkDocument? Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        signature = ReadSignature(); loaded = true;
        var bytes = Read();
        if (bytes is null ? signature is not null : signature is null || !signature.SequenceEqual(SHA256.HashData(bytes)))
            throw new IOException("读取时标签文件发生变化，请重试。");
        if (bytes is null) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<DocumentDto>(bytes, Options) ?? throw new InvalidDataException("标签文件为空。");
            if (dto.Version != BookmarkDocument.CurrentVersion || dto.Revision is null || dto.Entries is null) throw new InvalidDataException("标签文件版本未知或字段缺失。");
            var entries = dto.Entries.Select(entry => new LocationBookmark(entry.Id ?? Guid.Empty, entry.Name!, new PointD(entry.X!.Value, entry.Y!.Value))).ToArray();
            var document = new BookmarkDocument(dto.Version.Value, dto.Revision.Value, Array.AsReadOnly(entries));
            document.Validate(); return document;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new InvalidDataException("标签文件损坏；原文件已保留。", exception); }
    }
    private void EnsureUnchanged()
    {
        if (!loaded) throw new InvalidOperationException("必须先读取标签文件。");
        var current = ReadSignature();
        if (signature is null ? current is not null : current is null || !signature.SequenceEqual(current))
            throw new IOException("标签文件已由外部修改，请重新读取。");
    }
    private byte[]? ReadSignature()
    {
        using var stream = OpenCurrent();
        if (stream is null) return null;
        return SHA256.HashData(stream); // Constant-memory check also permits backing up an oversized file.
    }
    private FileStream? OpenCurrent()
    {
        try { return new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; } // Access denied is an error, never an empty library.
    }
    public void Save(BookmarkDocument document)
    {
        ObjectDisposedException.ThrowIf(disposed, this); document.Validate(); EnsureUnchanged();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new DocumentDto(document.Version, document.Revision,
            document.Entries.Select(entry => new EntryDto(entry.Id, entry.Name, entry.CameraPosition.X, entry.CameraPosition.Y)).ToArray()), Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("标签数据超过 16 MiB 存储限制。");
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            EnsureUnchanged(); File.Move(temporary, FilePath, overwrite: true); signature = SHA256.HashData(bytes);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string Backup()
    {
        ObjectDisposedException.ThrowIf(disposed, this); EnsureUnchanged();
        var backup = FilePath + $".backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json";
        File.Copy(FilePath, backup, overwrite: false); return backup;
    }
    public void Dispose() { if (disposed) return; disposed = true; lease.Dispose(); }
    private sealed record DocumentDto(int? Version, long? Revision, EntryDto[]? Entries);
    private sealed record EntryDto(Guid? Id, string? Name, double? X, double? Y);
}
