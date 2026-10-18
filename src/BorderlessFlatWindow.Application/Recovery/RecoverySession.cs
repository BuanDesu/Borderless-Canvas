using System.Security.Cryptography;
using BorderlessFlatWindow.Application.Discovery;

namespace BorderlessFlatWindow.Application.Recovery;

/// <summary>Write-ahead registration. A failed durable commit never grants permission to move.</summary>
public sealed class RecoverySession : IDisposable
{
    private readonly IRecoveryJournalStore store;
    private readonly IRecoveryWindowPort windows;
    private bool inheritedPending;
    private bool disposed;
    public RecoveryJournal Journal { get; private set; }
    public RecoveryReport? LastReport { get; private set; }
    public string? LastCloseError { get; private set; }

    public RecoverySession(IRecoveryJournalStore store, IRecoveryWindowPort windows)
    {
        this.store = store; this.windows = windows;
        try
        {
            Journal = store.Load() ?? RecoveryJournal.Empty();
            Journal.Validate();
            inheritedPending = Journal.Entries.Count > 0;
        }
        catch { store.Dispose(); throw; }
    }

    public RecoveryEntry Register(NativeWindowIdentity identity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (inheritedPending) throw new InvalidOperationException("Recover the previous session before starting new management.");
        var existing = Journal.Entries.FirstOrDefault(entry => entry.Identity.Handle == identity.Handle);
        if (existing is not null)
        {
            if (existing.Identity == identity && existing.Armed && windows.Matches(existing, Journal.MarkerName)) return existing;
            throw new InvalidOperationException("Existing recovery ownership no longer matches; recover before continuing.");
        }
        var original = windows.Capture(identity);
        var pending = new RecoveryEntry(Guid.NewGuid(), identity, original, RandomNumberGenerator.GetInt32(1, int.MaxValue), false);
        var before = Journal;
        Commit(Journal.WithEntries(Journal.Entries.Append(pending))); // Before native ownership or movement.
        try
        {
            windows.Mark(pending, Journal.MarkerName);
            if (!windows.Matches(pending, Journal.MarkerName)) throw new InvalidOperationException("Window changed during registration.");
            var armed = pending with { Armed = true };
            Commit(Journal.WithEntries(Journal.Entries.Select(entry => entry.Id == pending.Id ? armed : entry)));
            return armed;
        }
        catch
        {
            // An unarmed record never authorizes a placement change, even if cleanup or storage fails.
            try { windows.RemoveMarker(pending, Journal.MarkerName); }
            finally { Commit(new(before.SessionId, checked(Journal.Revision + 1), before.Entries)); }
            throw;
        }
    }

    public bool CanMove(Guid entryId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = Journal.Entries.FirstOrDefault(item => item.Id == entryId);
        return !inheritedPending && entry is { Armed: true } && windows.Matches(entry, Journal.MarkerName);
    }

    public RecoveryReport RecoverAll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        inheritedPending = true; // Any failed recovery/progress write blocks subsequent movement and registration.
        var results = new List<RecoveryResult>();
        foreach (var entry in Journal.Entries.ToArray())
        {
            RecoveryResult result;
            try
            {
                if (entry.Armed) result = windows.Restore(entry, Journal.MarkerName);
                else
                {
                    windows.RemoveMarker(entry, Journal.MarkerName);
                    result = new(entry.Id, RecoveryOutcome.ReleasedUnarmed, "Incomplete registration released without moving a window.");
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or System.ComponentModel.Win32Exception)
            { result = new(entry.Id, RecoveryOutcome.Failed, exception.Message); }
            results.Add(result);
            // Persist progress per entry. Failed entries retain both record and marker for retry.
            if (result.IsTerminal) Commit(Journal.WithEntries(Journal.Entries.Where(item => item.Id != entry.Id)));
        }
        inheritedPending = Journal.Entries.Count > 0;
        LastReport = new(results.AsReadOnly(), Journal.Entries.Count);
        return LastReport;
    }

    private void Commit(RecoveryJournal next)
    {
        next.Validate(); store.Save(next); Journal = next;
    }

    public void Dispose()
    {
        if (disposed) return;
        try { if (!inheritedPending && Journal.Entries.Count > 0) RecoverAll(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { LastCloseError = exception.Message; }
        finally { disposed = true; store.Dispose(); }
    }
}
