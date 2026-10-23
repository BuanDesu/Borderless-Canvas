using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

/// <summary>Bounded, transient launch matching. Never assigns an already existing window or a different executable.</summary>
public sealed class FixedLaunchTracker
{
    private sealed class Request(Guid id, string executable, HashSet<NativeWindowIdentity> before, DateTimeOffset deadline)
    {
        internal Guid Id = id;
        internal string Executable = executable;
        internal HashSet<NativeWindowIdentity> Before = before;
        internal DateTimeOffset Deadline = deadline;
        internal Dictionary<NativeWindowIdentity, (RectD Bounds, DateTimeOffset Since)> Candidates = [];
    }
    private readonly List<Request> requests = [];
    public int Count => requests.Count;
    public int ExpiredLastObservation { get; private set; }
    public IReadOnlySet<NativeWindowIdentity> HeldIdentities => requests.SelectMany(request => request.Candidates.Keys).ToHashSet();
    public Guid Begin(string executable, WindowCatalogSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (requests.Count >= 8) throw new InvalidOperationException("等待识别的新窗口过多，请稍后重试。");
        var id = Guid.NewGuid(); requests.Add(new(id, executable, snapshot.Entries.Where(entry => entry.Observation.Identity is not null)
            .Select(entry => entry.Observation.Identity!.Value).ToHashSet(), now.AddSeconds(30))); return id;
    }
    public void Cancel(Guid id) => requests.RemoveAll(request => request.Id == id);
    public IReadOnlyList<NativeWindowIdentity> Observe(WindowCatalogSnapshot snapshot, DateTimeOffset now,
        Func<NativeWindowIdentity, string?> executableOf, IReadOnlySet<NativeWindowIdentity> alreadyFixed)
    {
        var matched = new List<NativeWindowIdentity>(); ExpiredLastObservation = 0;
        foreach (var request in requests.ToArray())
        {
            if (now >= request.Deadline) { requests.Remove(request); ExpiredLastObservation++; continue; }
            var live = new HashSet<NativeWindowIdentity>(); NativeWindowIdentity? accepted = null;
            foreach (var entry in snapshot.Entries)
            {
                if (!entry.Admission.IsAdmitted || entry.Observation.Identity is not { IsValid: true } identity || entry.Observation.Bounds is not { } bounds
                    || request.Before.Contains(identity) || alreadyFixed.Contains(identity) || matched.Contains(identity)
                    || !string.Equals(executableOf(identity), request.Executable, StringComparison.OrdinalIgnoreCase)) continue;
                live.Add(identity);
                if (!request.Candidates.TryGetValue(identity, out var candidate) || candidate.Bounds != bounds) request.Candidates[identity] = (bounds, now);
                else if (now - candidate.Since >= RuntimeAdmissionTracker.StabilityDelay) { accepted = identity; break; }
            }
            foreach (var identity in request.Candidates.Keys.Where(identity => !live.Contains(identity)).ToArray()) request.Candidates.Remove(identity);
            if (accepted is { } ready) { matched.Add(ready); requests.Remove(request); }
        }
        return matched;
    }
}
