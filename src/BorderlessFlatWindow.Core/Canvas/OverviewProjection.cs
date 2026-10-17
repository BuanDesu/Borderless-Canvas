using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>One uniform transform for every window and the viewport; never a card-grid layout.</summary>
public sealed class OverviewProjection
{
    private OverviewProjection(double scale, PointD offset)
    {
        Scale = scale;
        Offset = offset;
    }

    public double Scale { get; }
    public PointD Offset { get; }

    public static OverviewProjection Fit(IEnumerable<RectD> rectangles, SizeD mapSize, double padding = 24)
    {
        ArgumentNullException.ThrowIfNull(rectangles);
        if (!mapSize.IsPositive || !double.IsFinite(padding) || padding < 0
            || padding * 2 >= Math.Min(mapSize.Width, mapSize.Height))
            throw new ArgumentOutOfRangeException(nameof(padding));
        var items = rectangles.ToArray();
        if (items.Length == 0)
            throw new ArgumentException("At least one rectangle is required.", nameof(rectangles));
        if (items.Any(rect => !rect.Size.IsPositive))
            throw new ArgumentException("All rectangles must have a positive size.", nameof(rectangles));

        var left = items.Min(rect => rect.Position.X);
        var top = items.Min(rect => rect.Position.Y);
        var width = items.Max(rect => rect.Right) - left;
        var height = items.Max(rect => rect.Bottom) - top;
        var scale = Math.Min((mapSize.Width - padding * 2) / width,
            (mapSize.Height - padding * 2) / height);
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(rectangles), "Map extent is not representable.");
        return new(scale, new((mapSize.Width - width * scale) / 2 - left * scale,
            (mapSize.Height - height * scale) / 2 - top * scale));
    }

    public PointD ToMap(PointD world) => new(world.X * Scale + Offset.X, world.Y * Scale + Offset.Y);
    public PointD ToWorld(PointD map) => new((map.X - Offset.X) / Scale, (map.Y - Offset.Y) / Scale);
    public RectD ToMap(RectD world) => new(ToMap(world.Position),
        new SizeD(world.Size.Width * Scale, world.Size.Height * Scale));
}
