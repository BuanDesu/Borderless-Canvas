using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

public sealed record WindowPlacementPlan(PixelRect PhysicalBounds, bool IsParked);

/// <summary>World positions stay unbounded by native parking. Never hide, minimize or resize a window.</summary>
public sealed class WindowPlacementPlanner
{
    // A deliberately conservative prototype budget, not a promise about every Win32 application's limits.
    public const int CoordinateBudget = 30000;
    private readonly PixelRect[] monitors;
    private readonly PixelRect[] visibleAreas;
    private readonly bool continuous;
    public PixelRect WorkArea { get; }

    public WindowPlacementPlanner(PixelRect workArea, IEnumerable<PixelRect> monitors, IEnumerable<PixelRect>? visibleAreas = null)
    {
        this.monitors = monitors.ToArray();
        continuous = visibleAreas is not null;
        this.visibleAreas = visibleAreas?.ToArray() ?? [workArea];
        if (this.monitors.Length == 0 || this.monitors.Any(monitor => !monitor.IsValid)
            || !workArea.IsValid || this.visibleAreas.Length == 0
            || this.visibleAreas.Any(area => !area.IsValid || !workArea.Contains(area) || !this.monitors.Any(m => m.Contains(area)))
            || !continuous && !this.monitors.Any(monitor => monitor.Contains(workArea)))
            throw new ArgumentException("Need valid monitors containing the viewport work area.");
        WorkArea = workArea;
    }

    public bool TryPlan(RectD world, PointD camera, out WindowPlacementPlan? plan)
    {
        plan = null;
        if (!world.Size.IsPositive || world.Size.Width > CoordinateBudget || world.Size.Height > CoordinateBudget) return false;
        var x = world.Position.X - camera.X;
        var y = world.Position.Y - camera.Y;
        var visible = x < WorkArea.Width && y < WorkArea.Height && x + world.Size.Width > 0 && y + world.Size.Height > 0;
        if (visible)
        {
            var mapper = new DesktopCoordinateMapper(camera, WorkArea);
            if (!mapper.TryProjectWindow(world, out var target) || !WithinBudget(target)) return false;
            if (!continuous && monitors.Any(monitor => monitor != monitors.First(item => item.Contains(WorkArea)) && Intersects(target, monitor)))
                return false; // Partial windows must not leak into an adjacent monitor.
            if (visibleAreas.Any(area => Intersects(target, area))) { plan = new(target, false); return true; }
        }
        var width = checked((int)Math.Round(world.Size.Width, MidpointRounding.AwayFromZero));
        var height = checked((int)Math.Round(world.Size.Height, MidpointRounding.AwayFromZero));
        if (width <= 0 || height <= 0) return false;
        var left = monitors.Min(monitor => (long)monitor.Left);
        var top = monitors.Min(monitor => (long)monitor.Top);
        var right = monitors.Max(monitor => (long)monitor.Right);
        var bottom = monitors.Max(monitor => (long)monitor.Bottom);
        var anchorX = Math.Clamp(WorkArea.Left, -CoordinateBudget, CoordinateBudget - width);
        var anchorY = Math.Clamp(WorkArea.Top, -CoordinateBudget, CoordinateBudget - height);
        foreach (var (px, py) in new (long X, long Y)[]
        { (left - width - 64, anchorY), (right + 64, anchorY), (anchorX, top - height - 64), (anchorX, bottom + 64) })
        {
            if (px < -CoordinateBudget || py < -CoordinateBudget || px + width > CoordinateBudget || py + height > CoordinateBudget) continue;
            var parked = new PixelRect((int)px, (int)py, (int)px + width, (int)py + height);
            if (monitors.All(monitor => !Intersects(parked, monitor))) { plan = new(parked, true); return true; }
        }
        return false;
    }

    public static bool WithinBudget(PixelRect bounds) => bounds.IsValid && bounds.Left >= -CoordinateBudget
        && bounds.Top >= -CoordinateBudget && bounds.Right <= CoordinateBudget && bounds.Bottom <= CoordinateBudget
        && bounds.Width <= CoordinateBudget && bounds.Height <= CoordinateBudget;
    public static bool Intersects(PixelRect a, PixelRect b) => a.IsValid && b.IsValid
        && a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}
