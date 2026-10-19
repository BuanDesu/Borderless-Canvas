using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.Application.Discovery;

public sealed record InspectedWindow(Guid? Id, WindowObservation Observation, WindowAdmissionDecision Admission);

public sealed record WindowCatalogSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<InspectedWindow> Entries)
{
    public IReadOnlyList<DesktopWindowInfo> AdmittedWindows => Entries
        .Where(entry => entry.Admission.IsAdmitted)
        .Select(entry => new DesktopWindowInfo(entry.Id!.Value, entry.Observation.Identity!.Value,
            entry.Observation.Title, entry.Observation.ClassName, entry.Observation.Bounds!.Value)).ToArray();
}

/// <summary>Retains IDs across metadata changes; forgets absent or unverified identities.</summary>
public sealed class WindowInspectionService(IWindowObservationSource source, WindowAdmissionPolicy policy)
    : IDesktopWindowCatalog
{
    private readonly Dictionary<nint, (NativeWindowIdentity Identity, Guid Id)> identities = [];

    public IReadOnlyList<DesktopWindowInfo> GetWindows() => Inspect().AdmittedWindows;

    public WindowCatalogSnapshot Inspect()
    {
        var observations = source.ReadWindows().ToArray();
        if (observations.Select(window => window.Handle).Distinct().Count() != observations.Length)
            throw new InvalidOperationException("Window source returned duplicate handles.");
        var next = new Dictionary<nint, (NativeWindowIdentity Identity, Guid Id)>();
        var entries = new List<InspectedWindow>(observations.Length);
        foreach (var observation in observations)
        {
            Guid? id = null;
            if (observation.IsAlive && observation.Identity is { IsValid: true } identity)
            {
                id = identities.TryGetValue(observation.Handle, out var previous) && previous.Identity == identity
                    ? previous.Id : Guid.NewGuid();
                next.Add(observation.Handle, (identity, id.Value));
            }
            entries.Add(new(id, observation, policy.Evaluate(observation)));
        }
        identities.Clear();
        foreach (var item in next) identities.Add(item.Key, item.Value);
        return new(DateTimeOffset.UtcNow, entries.AsReadOnly());
    }

    /// <summary>Lifecycle adapters must invalidate on destroy, including same-process HWND reuse.</summary>
    public void Forget(nint handle) => identities.Remove(handle);
}
