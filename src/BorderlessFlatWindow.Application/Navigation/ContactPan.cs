using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Navigation;

public enum ContactPhase { Down, Move, Up, Cancel }

/// <summary>One contact owns a pan until its release, regardless of additional fingers.</summary>
public sealed class ContactPan
{
    private uint? contact;
    private PointD last;
    public NavigationInput? Accept(uint id, ContactPhase phase, PointD point)
    {
        if (phase == ContactPhase.Down)
        {
            if (contact is not null) return null;
            contact = id; last = point; return new(NavigationInputKind.PanStart, point);
        }
        if (contact != id) return null;
        if (phase == ContactPhase.Cancel) return Cancel();
        last = point;
        if (phase == ContactPhase.Up) { contact = null; return new(NavigationInputKind.PanEnd, point); }
        return new(NavigationInputKind.PanMove, point);
    }
    public NavigationInput? Cancel()
    {
        if (contact is null) return null;
        contact = null; return new(NavigationInputKind.PanEnd, last);
    }
}
