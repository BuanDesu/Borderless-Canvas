using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class ArrangementChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Region tiling preserves native sizes, avoids fixed obstacles and is deterministic", Obstacles);
        yield return ("Region tiling extends beyond a small viewport rather than shrinking windows", Extension);
        yield return ("Region tiling handles negative, distant and oversized source geometry", Coordinates);
        yield return ("Region tiling rejects duplicate, missing, empty and nonrepresentable requests", Invalid);
        yield return ("Seeded mixed-size region layouts remain nonoverlapping across obstacle and row patterns", Stress);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static CanvasWindow Window(double x, double y, double w = 260, double h = 180) => new(Guid.NewGuid(), "Synthetic", new(new(x, y), new(w, h)));
    private static void Obstacles()
    {
        var source = new[] { Window(5, 5), Window(20, 5, 310, 220), Window(30, 70), Window(16, 16, 400, 330), Window(500, 100, 300, 400) };
        var ids = source.Take(3).Select(w => w.Id).ToArray();
        var region = new RectD(PointD.Zero, new(900, 600));
        Require(RegionTiling.TryPlan(source, ids, region, out var result) && result.Count == 3, "Plan failed.");
        Require(RegionTiling.TryPlan(source.Reverse(), ids.Reverse().ToArray(), region, out var reverse) && reverse.SequenceEqual(result), "Order changed plan.");
        foreach (var target in result)
        {
            Require(target.Bounds.Size == source.Single(w => w.Id == target.Id).Bounds.Size, "Resized window.");
            Require(result.Where(w => w.Id != target.Id).Concat(source.Skip(3)).All(w => !NearbyWindowPlacement.OverlapsWithGap(target.Bounds, w.Bounds)), "Overlap/gap lost.");
        }
    }
    private static void Extension()
    {
        var source = Enumerable.Range(0, 8).Select(i => Window(i * 10, 0)).ToArray();
        var area = new RectD(PointD.Zero, new(500, 200));
        Require(RegionTiling.TryPlan(source, source.Select(w => w.Id).ToArray(), area, out var result)
            && result.Last().Bounds.Bottom > area.Bottom && result.All(w => w.Bounds.Size == source[0].Bounds.Size)
            && result.Select(w => w.Bounds.Position.X).Distinct().Count() == 1, "Layout was compressed into viewport.");
    }
    private static void Coordinates()
    {
        foreach (var origin in new[] { new PointD(-5000, -3000), new(1e12, -1e12) })
        {
            var source = new[] { Window(origin.X, origin.Y, 1200, 500), Window(origin.X + 16, origin.Y + 16, 1300, 600) };
            Require(RegionTiling.TryPlan(source, [source[0].Id], new(origin, new(800, 400)), out var result)
                && result[0].Bounds.Size == source[0].Bounds.Size && !NearbyWindowPlacement.OverlapsWithGap(result[0].Bounds, source[1].Bounds), "Coordinate/oversized obstacle failed.");
        }
    }
    private static void Invalid()
    {
        var w = Window(0, 0); var area = new RectD(PointD.Zero, new(800, 600));
        Require(!RegionTiling.TryPlan([w], [], area, out _) && !RegionTiling.TryPlan([w], [w.Id, w.Id], area, out _)
            && !RegionTiling.TryPlan([w], [Guid.NewGuid()], area, out _) && !RegionTiling.TryPlan([w, w], [w.Id], area, out _)
            && !RegionTiling.TryPlan([w], [w.Id], default, out _)
            && !RegionTiling.TryPlan([w], [w.Id], new(new(1e300, 1e300), new(800, 600)), out _), "Invalid plan accepted.");
    }
    private static void Stress()
    {
        var random = new Random(84731);
        for (var run = 0; run < 160; run++)
        {
            var source = Enumerable.Range(0, 24).Select(_ => Window(random.Next(-1000, 2400), random.Next(-700, 2800), random.Next(80, 1400), random.Next(80, 900))).ToArray();
            var ids = source.Take(12).Select(w => w.Id).ToArray(); var region = new RectD(new(-350, -200), new(random.Next(300, 1800), 700));
            Require(RegionTiling.TryPlan(source, ids, region, out var result), "Seeded plan failed.");
            Require(result.Count == ids.Length && result.All(w => w.Bounds.Size == source.Single(old => old.Id == w.Id).Bounds.Size), "Seeded selection/size changed.");
            foreach (var target in result)
                Require(result.Where(w => w.Id != target.Id).Concat(source.Skip(12)).All(w => !NearbyWindowPlacement.OverlapsWithGap(target.Bounds, w.Bounds)), "Seeded overlap found.");
        }
    }
}
