using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Recovery;

/// <summary>Raw WINDOWPLACEMENT workspace coordinates. Never feed these to SetWindowPos.</summary>
public sealed record SavedWindowPlacement(uint Flags, uint ShowCommand, int MinX, int MinY, int MaxX, int MaxY,
    int Left, int Top, int Right, int Bottom)
{
    public bool IsValid => (Flags & ~3u) == 0 && ShowCommand is 1 or 2 or 3
        && Right > Left && Bottom > Top && (long)Right - Left <= int.MaxValue && (long)Bottom - Top <= int.MaxValue;
}

public sealed record OriginalWindowState(SavedWindowPlacement Placement, PixelRect PhysicalBounds);
public sealed record RecoveryEntry(Guid Id, NativeWindowIdentity Identity, OriginalWindowState Original,
    int Marker, bool Armed);

public sealed record RecoveryJournal(Guid SessionId, long Revision, IReadOnlyList<RecoveryEntry> Entries)
{
    public const int CurrentVersion = 1;
    public string MarkerName => $"BorderlessFlatWindow.Recovery.{SessionId:N}";
    public static RecoveryJournal Empty() => new(Guid.NewGuid(), 0, Array.Empty<RecoveryEntry>());
    public RecoveryJournal WithEntries(IEnumerable<RecoveryEntry> entries) =>
        new(SessionId, checked(Revision + 1), Array.AsReadOnly(entries.ToArray()));

    public void Validate()
    {
        if (SessionId == Guid.Empty || Revision < 0 || Entries is null || Entries.Count > 10000
            || Entries.Any(entry => entry is null || entry.Id == Guid.Empty || !entry.Identity.IsValid
                || entry.Marker <= 0 || entry.Original is null || entry.Original.Placement is null
                || !entry.Original.Placement.IsValid || !entry.Original.PhysicalBounds.IsValid)
            || Entries.Select(entry => entry.Id).Distinct().Count() != Entries.Count
            || Entries.Select(entry => entry.Identity.Handle).Distinct().Count() != Entries.Count)
            throw new InvalidDataException("Invalid recovery journal; no window operation is allowed.");
    }
}

public enum RecoveryOutcome { Restored, ReleasedUnarmed, SkippedClosedOrReused, Failed }
public sealed record RecoveryResult(Guid EntryId, RecoveryOutcome Outcome, string Detail)
{
    public bool IsTerminal => Outcome != RecoveryOutcome.Failed;
}
public sealed record RecoveryReport(IReadOnlyList<RecoveryResult> Results, int Remaining)
{
    public bool IsComplete => Remaining == 0;
}

/// <summary>A store instance holds an exclusive lease until Dispose, shared by manager and recovery CLI.</summary>
public interface IRecoveryJournalStore : IDisposable
{
    RecoveryJournal? Load();
    void Save(RecoveryJournal journal);
}

public interface IRecoveryWindowPort
{
    OriginalWindowState Capture(NativeWindowIdentity identity);
    void Mark(RecoveryEntry entry, string markerName);
    bool Matches(RecoveryEntry entry, string markerName);
    void RemoveMarker(RecoveryEntry entry, string markerName);
    RecoveryResult Restore(RecoveryEntry entry, string markerName);
}
