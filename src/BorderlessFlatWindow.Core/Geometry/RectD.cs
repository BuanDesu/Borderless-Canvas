namespace BorderlessFlatWindow.Core.Geometry;

public readonly record struct RectD
{
    public RectD(PointD position, SizeD size)
    {
        if (!size.IsPositive)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!double.IsFinite(position.X + size.Width) || !double.IsFinite(position.Y + size.Height))
            throw new ArgumentOutOfRangeException(nameof(size), "Bounds must be finite.");
        Position = position;
        Size = size;
    }

    public PointD Position { get; }
    public SizeD Size { get; }
    public double Right => Position.X + Size.Width;
    public double Bottom => Position.Y + Size.Height;
    public PointD Center => new(Position.X + Size.Width / 2, Position.Y + Size.Height / 2);
    public bool Contains(PointD point) => point.X >= Position.X && point.X <= Right
        && point.Y >= Position.Y && point.Y <= Bottom;
}
