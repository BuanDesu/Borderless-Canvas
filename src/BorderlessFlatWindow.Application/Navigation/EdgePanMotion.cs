using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Navigation;

/// <summary>Time-based edge speed, advanced only by fresh outward input; no inertia or hover timer.</summary>
public sealed class EdgePanMotion
{
    public const double InitialSpeed = 120, MaximumSpeed = 1800, RampSeconds = 1.2;
    public const int ContinuityMilliseconds = 100;
    private readonly Axis x = new(), y = new();

    public PointD Observe(PointD relative, PointD pointer, IReadOnlyList<PixelRect> screens, NavigationInputOptions options, long now)
    {
        var outward = PointerNavigation.CameraDelta(relative, pointer, screens, options, false);
        return new(x.Observe(Math.Sign(outward.X), now) * options.PointerSpeed,
            y.Observe(Math.Sign(outward.Y), now) * options.PointerSpeed);
    }
    public void ConstrainToPointer(PointD pointer, IReadOnlyList<PixelRect> screens, NavigationInputOptions options)
    {
        var outward = PointerNavigation.CameraDelta(new(x.Direction, y.Direction), pointer, screens, options, false);
        if (outward.X == 0) x.Reset(); if (outward.Y == 0) y.Reset();
    }
    public void Reset() { x.Reset(); y.Reset(); }
    private sealed class Axis
    {
        internal int Direction;
        private long started, last;
        internal double Observe(int direction, long now)
        {
            if (direction == 0) { Reset(); return 0; }
            if (Direction != direction || now < last || now - last > ContinuityMilliseconds)
            { Direction = direction; started = last = now; return 0; }
            if (now == last) return 0; // Several high-rate packets can share one millisecond.
            var elapsed = (now - last) / 1000d;
            var from = Math.Min(RampSeconds, (last - started) / 1000d);
            var to = Math.Min(RampSeconds, (now - started) / 1000d);
            var ramp = to - from;
            var distance = InitialSpeed * ramp + (MaximumSpeed - InitialSpeed) / (2 * RampSeconds) * (to * to - from * from)
                + MaximumSpeed * (elapsed - ramp);
            last = now; return direction * distance;
        }
        internal void Reset() { Direction = 0; started = last = 0; }
    }
}
