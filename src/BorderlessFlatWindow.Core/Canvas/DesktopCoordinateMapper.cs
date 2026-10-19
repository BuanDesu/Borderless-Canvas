using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Native scale: P = W - C + work-area origin. DPI never rescales the canvas.</summary>
public sealed class DesktopCoordinateMapper
{
    public DesktopCoordinateMapper(PointD cameraPosition, PixelRect workArea)
    {
        if (!workArea.IsValid) throw new ArgumentOutOfRangeException(nameof(workArea));
        WorkArea = workArea;
        Camera = new(cameraPosition, workArea.Size);
    }

    public PixelRect WorkArea { get; }
    public Camera2D Camera { get; }
    public PointD WorldToPhysical(PointD world) => ViewportToPhysical(Camera.WorldToViewport(world));
    public PointD PhysicalToWorld(PointD physical) => Camera.ViewportToWorld(PhysicalToViewport(physical));
    public PointD ViewportToPhysical(PointD viewport) => viewport + WorkArea.Position;
    public PointD PhysicalToViewport(PointD physical) => physical - WorkArea.Position;
    public RectD PhysicalToWorld(PixelRect physical)
    {
        if (!physical.IsValid) throw new ArgumentOutOfRangeException(nameof(physical));
        return new(PhysicalToWorld(physical.Position), physical.Size);
    }

    /// <summary>Round position and size independently, away from zero at ties. Never clamp or wrap.</summary>
    public bool TryProjectWindow(RectD world, out PixelRect physical)
    {
        physical = default;
        if (!world.Size.IsPositive
            || !TryRound(world.Position.X - Camera.Position.X + WorkArea.Left, out var x)
            || !TryRound(world.Position.Y - Camera.Position.Y + WorkArea.Top, out var y)
            || !TryRound(world.Size.Width, out var width) || width <= 0
            || !TryRound(world.Size.Height, out var height) || height <= 0
            || (long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
            return false;
        physical = new(x, y, x + width, y + height);
        return true;
    }

    private static bool TryRound(double value, out int result)
    {
        result = 0;
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue) return false;
        result = checked((int)rounded);
        return true;
    }
}
