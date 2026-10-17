using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class PlacementChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Visible plans preserve physical scale and work-area offset", Visible);
        yield return ("Remote world positions park in bounded coordinates without changing size or world layout", Remote);
        yield return ("Finite world inputs with overflowing relative distance still park safely", OverflowDistance);
        yield return ("Parking avoids all negative and positive monitor rectangles", MultipleMonitors);
        yield return ("Partial windows leaking into a neighboring display are rejected", Leak);
        yield return ("A topology with no bounded parking space fails without hiding or resizing", NoSpace);
        yield return ("Oversized and invalid windows cannot produce a native movement plan", Invalid);
        yield return ("Touching the viewport edge is offscreen and partial intersections keep native size", Edges);
    }
    private static RectD Rect(double x, double y, double width = 640, double height = 480) => new(new PointD(x, y), new SizeD(width, height));
    private static WindowPlacementPlanner Planner() => new(new(0, 40, 1920, 1080), [new(0, 0, 1920, 1080)]);
    private static void Visible()
    {
        var planner = Planner();
        Require(planner.TryPlan(Rect(100, 100), new(20, 30), out var plan) && plan is { IsParked: false }
            && plan.PhysicalBounds == new PixelRect(80, 110, 720, 590), "Scale/offset changed.");
    }
    private static void Remote()
    {
        var planner = Planner(); var world = Rect(1e20, -1e20);
        Require(planner.TryPlan(world, PointD.Zero, out var a) && a is { IsParked: true }, "Remote window failed to park.");
        Require(planner.TryPlan(world, new(-1e20, 1e20), out var b) && a == b
            && a!.PhysicalBounds.Size == world.Size && WindowPlacementPlanner.WithinBudget(a.PhysicalBounds), "Parking changed size or depended on remote distance.");
    }
    private static void OverflowDistance()
    {
        Require(Planner().TryPlan(Rect(double.MaxValue, 0), new(-double.MaxValue, 0), out var plan)
            && plan is { IsParked: true }, "Infinite relative intermediate was cast to native coordinates.");
    }
    private static void MultipleMonitors()
    {
        PixelRect[] monitors = [new(-2560, -1440, 0, 0), new(0, 0, 1920, 1080), new(1920, 0, 3840, 1080)];
        var planner = new WindowPlacementPlanner(new(0, 0, 1920, 1040), monitors);
        Require(planner.TryPlan(Rect(50000, -40000), PointD.Zero, out var plan) && plan is { IsParked: true }
            && monitors.All(monitor => !WindowPlacementPlanner.Intersects(monitor, plan.PhysicalBounds)), "Parked window intersects a screen.");
    }
    private static void Leak()
    {
        var planner = new WindowPlacementPlanner(new(0, 0, 1920, 1040), [new(0, 0, 1920, 1080), new(1920, 0, 3840, 1080)]);
        Require(!planner.TryPlan(Rect(1800, 0), PointD.Zero, out _), "Partially visible window leaked to a neighbor.");
    }
    private static void NoSpace()
    {
        var planner = new WindowPlacementPlanner(new(0, 0, 1920, 1040), [new(-30000, -30000, 30000, 30000)]);
        Require(!planner.TryPlan(Rect(50000, 0), PointD.Zero, out var plan) && plan is null, "No-space case fabricated a position.");
    }
    private static void Invalid()
    {
        Require(!Planner().TryPlan(default, PointD.Zero, out _), "Default bounds admitted.");
        Require(!Planner().TryPlan(Rect(0, 0, 30001, 480), PointD.Zero, out _), "Oversized window admitted.");
    }
    private static void Edges()
    {
        var planner = Planner();
        Require(planner.TryPlan(Rect(1920, 0), PointD.Zero, out var edge) && edge is { IsParked: true }, "Touching edge counted as visible.");
        Require(planner.TryPlan(Rect(-200, 100), PointD.Zero, out var partial) && partial is { IsParked: false }
            && partial.PhysicalBounds.Left == -200 && partial.PhysicalBounds.Width == 640, "Partial window was resized or gathered.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
