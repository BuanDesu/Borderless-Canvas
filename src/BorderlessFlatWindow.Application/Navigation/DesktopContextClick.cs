using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Navigation;

/// <summary>Recognizes a solitary captured right click without turning a chord into a shell click.</summary>
public sealed class DesktopContextClick
{
    private PointD? origin;
    private bool left;
    public void Cancel() { origin = null; left = false; }
    public bool Accept(NavigationInputKind kind, PointD position, bool captured, bool desktop, double tolerance)
    {
        if (kind == NavigationInputKind.LeftDown) left = true;
        if (kind == NavigationInputKind.LeftUp) left = false;
        if (!desktop || kind is NavigationInputKind.LeftDown or NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel
            || origin is { } start && (Math.Abs(start.X - position.X) > tolerance || Math.Abs(start.Y - position.Y) > tolerance)) origin = null;
        if (kind == NavigationInputKind.RightDown) origin = captured && desktop && !left ? position : null;
        if (kind != NavigationInputKind.RightUp) return false;
        var click = captured && desktop && origin is not null && !left;
        origin = null;
        return click;
    }
}
