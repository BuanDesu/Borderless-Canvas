using System.Text.Json;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows.Recovery;

/// <summary>Same-directory atomic replacement and an OS-owned exclusive file lease.</summary>
public sealed class JsonRecoveryJournalStore : IRecoveryJournalStore
{
    private readonly FileStream lease;
    private bool disposed;
    public string JournalPath { get; }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BorderlessFlatWindow", "recovery.json");
    private const int MaxBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public JsonRecoveryJournalStore(string path)
    {
        JournalPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        lease = new(JournalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public RecoveryJournal? Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(JournalPath)) return null;
        if (new FileInfo(JournalPath).Length > MaxBytes) throw new InvalidDataException("Recovery journal exceeds size limit.");
        try
        {
            var dto = JsonSerializer.Deserialize<JournalDto>(File.ReadAllBytes(JournalPath), Options)
                ?? throw new InvalidDataException("Recovery journal is empty.");
            if (dto.Version != RecoveryJournal.CurrentVersion || dto.Entries is null || dto.Entries.Length > 10000)
                throw new InvalidDataException("Unsupported or incomplete recovery journal; file preserved.");
            var entries = dto.Entries.Select(item => new RecoveryEntry(item.Id,
                new NativeWindowIdentity(checked((nint)item.Handle), item.ProcessId, item.Created),
                new(item.Placement!, new PixelRect(item.Left, item.Top, item.Right, item.Bottom)), item.Marker, item.Armed)).ToArray();
            var journal = new RecoveryJournal(dto.SessionId, dto.Revision, Array.AsReadOnly(entries));
            journal.Validate();
            return journal;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException or NullReferenceException)
        { throw new InvalidDataException("Recovery journal is malformed; file preserved and no windows touched.", exception); }
    }

    public void Save(RecoveryJournal journal)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        journal.Validate();
        var dto = new JournalDto(RecoveryJournal.CurrentVersion, journal.SessionId, journal.Revision,
            journal.Entries.Select(entry => new EntryDto(entry.Id, entry.Identity.Handle.ToInt64(), entry.Identity.ProcessId,
                entry.Identity.ProcessCreationFileTime, entry.Original.Placement, entry.Original.PhysicalBounds.Left,
                entry.Original.PhysicalBounds.Top, entry.Original.PhysicalBounds.Right, entry.Original.PhysicalBounds.Bottom,
                entry.Marker, entry.Armed)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Recovery journal exceeds size limit.");
        var temporary = JournalPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, JournalPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() { if (disposed) return; disposed = true; lease.Dispose(); }
    private sealed record JournalDto(int Version, Guid SessionId, long Revision, EntryDto[]? Entries);
    private sealed record EntryDto(Guid Id, long Handle, uint ProcessId, long Created, SavedWindowPlacement? Placement,
        int Left, int Top, int Right, int Bottom, int Marker, bool Armed);
}
