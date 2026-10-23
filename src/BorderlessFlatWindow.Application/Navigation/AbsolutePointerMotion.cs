using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Navigation;

/// <summary>Converts consecutive absolute positions into motion; first samples, source changes and warps only establish a baseline.</summary>
public sealed class AbsolutePointerMotion
{
    private PointD? previous;
    private nint device;
    private PixelRect surface;
    private long lastAt;
    public void Reset() => previous = null;
    public PointD Observe(PointD position, PixelRect bounds, nint source, long at)
    {
        if (!bounds.IsValid) { Reset(); return PointD.Zero; }
        var delta = previous is { } last && device == source && surface == bounds && at >= lastAt && at - lastAt <= 750
            ? position - last : PointD.Zero;
        previous = position; device = source; surface = bounds; lastAt = at;
        return Math.Abs(delta.X) > bounds.Width / 2d || Math.Abs(delta.Y) > bounds.Height / 2d ? PointD.Zero : delta;
    }
    public static PointD MapNormalized(PointD value, PixelRect bounds)
    {
        if (!bounds.IsValid || value.X is < 0 or > 65535 || value.Y is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(value));
        return new(bounds.Left + Math.Min(bounds.Width - 1, value.X * bounds.Width / 65535d),
            bounds.Top + Math.Min(bounds.Height - 1, value.Y * bounds.Height / 65535d));
    }
}
