using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;

namespace BorderlessFlatWindow.Application.Management;

/// <summary>Auto admission excludes the pre-existing desktop and waits for new windows to settle.</summary>
public sealed class RuntimeAdmissionTracker
{
    public static readonly TimeSpan StabilityDelay = TimeSpan.FromMilliseconds(200);
    private readonly Dictionary<nint, NativeWindowIdentity?> baseline = [];
    private readonly Dictionary<Guid, Pending> pending = [];
    private sealed record Pending(InspectedWindow Entry, DateTimeOffset Since);
    public IReadOnlyList<nint> PendingHandles => pending.Values.Select(value => value.Entry.Observation.Handle).Distinct().ToArray();
    public void Reset(WindowCatalogSnapshot snapshot)
    {
        pending.Clear(); baseline.Clear();
        foreach (var entry in snapshot.Entries) baseline[entry.Observation.Handle] = entry.Observation.Identity;
    }
    public void ForgetDestroyed(nint handle) => baseline.Remove(handle);
    public void Observe(WindowCatalogSnapshot snapshot, IReadOnlySet<Guid> managedIds, DisplayLayout layout, DateTimeOffset now)
    {
        var eligible = snapshot.Entries.Where(entry => entry.Id is { } id && !managedIds.Contains(id)
            && entry.Observation.Identity is { IsValid: true } && entry.Observation.Bounds is not null
            && ManagedCanvasSession.Unsupported(entry, layout) is null
            && (!baseline.TryGetValue(entry.Observation.Handle, out var original) || original is not null && original != entry.Observation.Identity)).ToArray();
        var ids = eligible.Select(entry => entry.Id!.Value).ToHashSet();
        foreach (var id in pending.Keys.Where(id => !ids.Contains(id)).ToArray()) pending.Remove(id);
        foreach (var entry in eligible)
        {
            var id = entry.Id!.Value;
            if (!pending.TryGetValue(id, out var before) || before.Entry.Observation.Identity != entry.Observation.Identity
                || before.Entry.Observation.Bounds != entry.Observation.Bounds || before.Entry.Observation.Title != entry.Observation.Title)
                pending[id] = new(entry, now);
            else pending[id] = before with { Entry = entry };
        }
    }
    public IReadOnlyList<InspectedWindow> Ready(DateTimeOffset now) => pending.Values.Where(value => now - value.Since >= StabilityDelay)
        .Select(value => value.Entry).OrderBy(entry => entry.Id).ToArray();
}
