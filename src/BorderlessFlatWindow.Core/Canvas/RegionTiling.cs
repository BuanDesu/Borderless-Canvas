using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Size-preserving rows extending downward, with unselected windows as fixed obstacles.</summary>
public static class RegionTiling
{
    public static bool TryPlan(IEnumerable<CanvasWindow> source, IReadOnlyCollection<Guid> selection, RectD region,
        out IReadOnlyList<CanvasWindow> targets)
    {
        targets = [];
        var all = source.ToArray(); var ids = selection.ToHashSet();
        if (!region.Size.IsPositive || ids.Count == 0 || ids.Count != selection.Count || ids.Contains(Guid.Empty)
            || all.Select(w => w.Id).Distinct().Count() != all.Length || all.Any(w => !w.Bounds.Size.IsPositive)
            || !ids.IsSubsetOf(all.Select(w => w.Id))) return false;
        var occupied = all.Where(w => !ids.Contains(w.Id)).Select(w => w.Bounds).ToList();
        var arranged = new List<CanvasWindow>();
        var left = region.Position.X + NearbyWindowPlacement.Gap;
        var x = left; var y = region.Position.Y + NearbyWindowPlacement.Gap; var rowBottom = y; var rowUsed = false;
        foreach (var window in all.Where(w => ids.Contains(w.Id)).OrderBy(w => w.Bounds.Position.Y).ThenBy(w => w.Bounds.Position.X).ThenBy(w => w.Id))
        {
            var size = window.Bounds.Size;
            var right = Math.Max(region.Right - NearbyWindowPlacement.Gap, left + size.Width);
            while (true)
            {
                if (!double.IsFinite(x + size.Width) || !double.IsFinite(y + size.Height)
                    || x + size.Width <= x || y + size.Height <= y) return false;
                if (x + size.Width > right && rowUsed)
                { x = left; y = rowBottom + NearbyWindowPlacement.Gap; rowBottom = y; rowUsed = false; continue; }
                var candidate = new RectD(new(x, y), size);
                var collisions = occupied.Where(r => NearbyWindowPlacement.OverlapsWithGap(candidate, r)).ToArray();
                if (collisions.Length == 0)
                {
                    arranged.Add(window with { Bounds = candidate }); occupied.Add(candidate);
                    rowBottom = Math.Max(rowBottom, candidate.Bottom); rowUsed = true;
                    x = candidate.Right + NearbyWindowPlacement.Gap;
                    break;
                }
                var nextX = collisions.Max(r => r.Right) + NearbyWindowPlacement.Gap;
                if (nextX > x && nextX + size.Width <= right) { x = nextX; continue; }
                var nextY = rowUsed ? rowBottom + NearbyWindowPlacement.Gap : collisions.Min(r => r.Bottom) + NearbyWindowPlacement.Gap;
                if (!double.IsFinite(nextY) || nextY <= y) return false;
                x = left; y = nextY; rowBottom = y; rowUsed = false;
            }
        }
        targets = arranged.AsReadOnly(); return true;
    }
}
