using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

internal static class RecoveryPlacementChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recovery fallback translates top/left taskbar work coordinates without changing size", Taskbars);
        yield return ("Recovery fallback supports negative monitor origins and oversized native windows", Geometry);
        yield return ("Recovery fallback refuses ambiguous show states, sizes, work areas and integer overflow", Invalid);
    }
    private static OriginalWindowState Original(int x = 9000, int y = 9000, int width = 640, int height = 480)
        => new(new(0, 1, -1, -1, -1, -1, x, y, checked(x + width), checked(y + height)), new(x, y, checked(x + width), checked(y + height)));
    private static void Taskbars()
    {
        var original = Original();
        foreach (var work in new[] { new PixelRect(0, 40, 1920, 1080), new PixelRect(50, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), new PixelRect(0, 0, 1880, 1080) })
        {
            var target = RecoveryPlacementFallback.Plan(original, new(0, 0, 1920, 1080), work)!;
            Require(work.Contains(target.PhysicalBounds) && target.PhysicalBounds.Size == original.PhysicalBounds.Size
                && target.Placement.Left + work.Left == target.PhysicalBounds.Left && target.Placement.Top + work.Top == target.PhysicalBounds.Top,
                "Fallback confused physical and workspace coordinates.");
        }
    }
    private static void Geometry()
    {
        var normal = RecoveryPlacementFallback.Plan(Original(-9000, -9000), new(-1920, -1080, 0, 0), new(-1880, -1040, 0, 0))!;
        Require(normal.PhysicalBounds.Left == -1880 && normal.PhysicalBounds.Top == -1040 && normal.Placement.Left == -1920 && normal.Placement.Top == -1080,
            "Negative monitor origin was lost.");
        var oversized = RecoveryPlacementFallback.Plan(Original(width: 2560, height: 1440), new(0, 0, 1280, 800), new(0, 40, 1280, 800))!;
        Require(oversized.PhysicalBounds == new PixelRect(0, 40, 2560, 1480), "Oversized fallback resized or placed title bar outside the work area.");
    }
    private static void Invalid()
    {
        var original = Original(); var monitor = new PixelRect(0, 0, 1920, 1080);
        foreach (var state in new[] { original with { Placement = original.Placement with { ShowCommand = 2 } },
            original with { Placement = original.Placement with { Right = original.Placement.Right + 1 } } })
            Require(RecoveryPlacementFallback.Plan(state, monitor, monitor) is null, "Ambiguous restore state was normalized.");
        Require(RecoveryPlacementFallback.Plan(original, monitor, new(-1, 0, 1920, 1080)) is null, "Invalid work area accepted.");
        Require(RecoveryPlacementFallback.Plan(Original(width: 1000), new(int.MaxValue - 10, 0, int.MaxValue, 800), new(int.MaxValue - 10, 0, int.MaxValue, 800)) is null,
            "Overflowing fallback was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
