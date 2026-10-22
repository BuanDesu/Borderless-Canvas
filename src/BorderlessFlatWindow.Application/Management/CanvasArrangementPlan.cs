using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

/// <summary>Read-only preview bound to its management session and full source geometry.</summary>
public sealed class CanvasArrangementPlan
{
    internal CanvasArrangementPlan(object owner, IReadOnlyList<CanvasWindow> source, IReadOnlyList<CanvasWindow> targets, RectD region)
    {
        Owner = owner; Source = Array.AsReadOnly(source.ToArray()); Targets = Array.AsReadOnly(targets.ToArray()); Region = region;
        var ids = targets.Select(w => w.Id).ToHashSet(); Before = Array.AsReadOnly(source.Where(w => ids.Contains(w.Id)).ToArray());
        ChangedCount = targets.Count(w => Before.Single(old => old.Id == w.Id).Bounds != w.Bounds);
    }
    internal object Owner { get; }
    internal IReadOnlyList<CanvasWindow> Source { get; }
    internal IReadOnlyList<CanvasWindow> Before { get; }
    public IReadOnlyList<CanvasWindow> Targets { get; }
    public RectD Region { get; }
    public int ChangedCount { get; }
    internal static bool Matches(IReadOnlyList<CanvasWindow> expected, IReadOnlyList<CanvasWindow> current, bool exactSet)
    {
        if (exactSet && expected.Count != current.Count) return false;
        var byId = current.ToDictionary(w => w.Id);
        return expected.All(w => byId.TryGetValue(w.Id, out var actual) && actual.Bounds == w.Bounds);
    }
}
