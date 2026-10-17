namespace BorderlessFlatWindow.Core.Geometry;

public readonly record struct PointD
{
    public PointD(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite.");
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
    public static PointD Zero => new(0, 0);
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
}
