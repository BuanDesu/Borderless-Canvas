using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Recovery;

/// <summary>Explicit target for an original normal window whose display is no longer available.</summary>
public sealed record RecoveryPlacementTarget(SavedWindowPlacement Placement, PixelRect PhysicalBounds);

public static class RecoveryPlacementFallback
{
    public static RecoveryPlacementTarget? Plan(OriginalWindowState original, PixelRect monitor, PixelRect workArea)
    {
        ArgumentNullException.ThrowIfNull(original);
        var saved = original.Placement; var physical = original.PhysicalBounds;
        if (saved is null || !saved.IsValid || saved.ShowCommand != 1 || !physical.IsValid || !monitor.Contains(workArea)
            || (long)saved.Right - saved.Left != physical.Width || (long)saved.Bottom - saved.Top != physical.Height) return null;
        // Keep native size. Oversized windows anchor at the usable top-left rather than being resized.
        var x = Math.Clamp((long)physical.Left, workArea.Left, Math.Max((long)workArea.Left, (long)workArea.Right - physical.Width));
        var y = Math.Clamp((long)physical.Top, workArea.Top, Math.Max((long)workArea.Top, (long)workArea.Bottom - physical.Height));
        var left = x - ((long)workArea.Left - monitor.Left);
        var top = y - ((long)workArea.Top - monitor.Top);
        try
        {
            var target = new PixelRect(checked((int)x), checked((int)y), checked((int)(x + physical.Width)), checked((int)(y + physical.Height)));
            var placement = saved with { Left = checked((int)left), Top = checked((int)top),
                Right = checked((int)(left + physical.Width)), Bottom = checked((int)(top + physical.Height)) };
            return new(placement, target);
        }
        catch (OverflowException) { return null; }
    }
}
