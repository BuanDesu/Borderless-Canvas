using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Finds empty space without editing existing rectangles. Prefer a complete fit in the current viewport.</summary>
public static class NearbyWindowPlacement
{
    public const double Gap = 16;
    public static bool TryPlace(SizeD size, RectD viewport, IEnumerable<RectD> source, out RectD result)
    {
        result = default;
        if (!size.IsPositive || !viewport.Size.IsPositive) return false;
        var occupied = source.ToArray();
        if (occupied.Any(rect => !rect.Size.IsPositive)) return false;
        var preferred = new PointD(viewport.Center.X - size.Width / 2, viewport.Center.Y - size.Height / 2);
        var rows = new HashSet<double> { preferred.Y, viewport.Position.Y + Gap, viewport.Bottom - size.Height - Gap };
        foreach (var rect in occupied) { rows.Add(rect.Bottom + Gap); rows.Add(rect.Position.Y - size.Height - Gap); }
        RectD? best = null; var bestDistance = double.PositiveInfinity;
        void Consider(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(x + size.Width) || !double.IsFinite(y + size.Height)
                || x + size.Width <= x || y + size.Height <= y) return;
            var distance = Math.Abs(x - preferred.X) + Math.Abs(y - preferred.Y);
            if (best is null || distance < bestDistance) { best = new(new(x, y), size); bestDistance = distance; }
        }
        void Scan(double y, double minimum, double maximum)
        {
            if (!double.IsFinite(y) || !double.IsFinite(y + size.Height) || minimum > maximum) return;
            var spans = occupied.Where(rect => y < rect.Bottom + Gap && y + size.Height + Gap > rect.Position.Y)
                .Select(rect => (Left: rect.Position.X - size.Width - Gap, Right: rect.Right + Gap)).OrderBy(span => span.Left);
            var cursor = minimum;
            foreach (var span in spans)
            {
                if (span.Right < cursor) continue;
                if (span.Left > maximum) break;
                if (cursor <= span.Left) Consider(Math.Clamp(preferred.X, cursor, Math.Min(span.Left, maximum)), y);
                cursor = Math.Max(cursor, span.Right);
                if (cursor > maximum) return;
            }
            if (cursor <= maximum) Consider(Math.Clamp(preferred.X, cursor, maximum), y);
        }
        var minX = viewport.Position.X + Gap; var maxX = viewport.Right - size.Width - Gap;
        var minY = viewport.Position.Y + Gap; var maxY = viewport.Bottom - size.Height - Gap;
        foreach (var y in rows.Where(y => y >= minY && y <= maxY).OrderBy(y => y)) Scan(y, minX, maxX);
        if (best is not null) { result = best.Value; return true; }
        foreach (var y in rows.OrderBy(y => y)) Scan(y, -double.MaxValue, double.MaxValue - size.Width);
        if (best is null) return false;
        result = best.Value; return true;
    }
    public static bool OverlapsWithGap(RectD a, RectD b) => a.Position.X < b.Right + Gap && a.Right + Gap > b.Position.X
        && a.Position.Y < b.Bottom + Gap && a.Bottom + Gap > b.Position.Y;
    public static bool FitsInside(RectD inner, RectD outer) => inner.Position.X >= outer.Position.X && inner.Position.Y >= outer.Position.Y
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
