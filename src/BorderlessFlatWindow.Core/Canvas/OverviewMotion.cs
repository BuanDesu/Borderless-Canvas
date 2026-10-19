using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

public static class OverviewMotion
{
    public static double Ease(double progress)
    {
        if (!double.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        var t = Math.Clamp(progress, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }
    public static RectD Between(RectD from, RectD to, double progress)
    {
        var t = Ease(progress);
        return new(new(from.Position.X + (to.Position.X - from.Position.X) * t, from.Position.Y + (to.Position.Y - from.Position.Y) * t),
            new(from.Size.Width + (to.Size.Width - from.Size.Width) * t, from.Size.Height + (to.Size.Height - from.Size.Height) * t));
    }
}
