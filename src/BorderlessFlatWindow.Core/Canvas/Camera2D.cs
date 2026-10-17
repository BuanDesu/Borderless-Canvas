using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Native-scale viewport. Coordinates use canvas units, independent of HWND placement.</summary>
public sealed record Camera2D
{
    public Camera2D(PointD position, SizeD viewportSize)
    {
        if (!viewportSize.IsPositive)
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        Position = position;
        ViewportSize = viewportSize;
    }

    public PointD Position { get; }
    public SizeD ViewportSize { get; }
    public RectD Bounds => new(Position, ViewportSize);
    public PointD WorldToViewport(PointD world) => world - Position;
    public PointD ViewportToWorld(PointD viewport) => viewport + Position;
    public Camera2D At(PointD position) => new(position, ViewportSize);
    public Camera2D CenterOn(RectD target) => At(new(
        target.Center.X - ViewportSize.Width / 2,
        target.Center.Y - ViewportSize.Height / 2));
}
