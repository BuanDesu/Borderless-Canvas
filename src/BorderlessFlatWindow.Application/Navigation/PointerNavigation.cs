using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Navigation;

public static class PointerNavigation
{
    public static PointD CameraDelta(PointD relative, PointD pointer, IReadOnlyList<PixelRect> screens, NavigationInputOptions options, bool crosshair)
    {
        ArgumentNullException.ThrowIfNull(screens); ArgumentNullException.ThrowIfNull(options);
        var x = relative.X; var y = relative.Y;
        if (!crosshair)
        {
            if (!options.EdgePan) return PointD.Zero;
            var screen = screens.FirstOrDefault(bounds => Contains(bounds, pointer.X, pointer.Y));
            if (!screen.IsValid) return PointD.Zero;
            var outwardX = x < 0 && pointer.X <= screen.Left && !Inside(screens, pointer.X - 1, pointer.Y)
                || x > 0 && pointer.X >= screen.Right - 1 && !Inside(screens, pointer.X + 1, pointer.Y);
            var outwardY = y < 0 && pointer.Y <= screen.Top && !Inside(screens, pointer.X, pointer.Y - 1)
                || y > 0 && pointer.Y >= screen.Bottom - 1 && !Inside(screens, pointer.X, pointer.Y + 1);
            if (!outwardX) x = 0; if (!outwardY) y = 0;
        }
        return new(x * options.PointerSpeed, y * options.PointerSpeed);
    }
    private static bool Inside(IEnumerable<PixelRect> screens, double x, double y) => screens.Any(bounds => Contains(bounds, x, y));
    private static bool Contains(PixelRect bounds, double x, double y) => bounds.IsValid && x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
}
