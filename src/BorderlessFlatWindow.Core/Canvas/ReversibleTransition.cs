namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>A reversible 0–1 transition. Retargeting starts at the currently displayed value.</summary>
public sealed class ReversibleTransition
{
    private double from, target, started, duration;
    public bool TargetOpen => target == 1;
    public double Value(double now)
    {
        if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
        return duration <= 0 ? target : from + (target - from) * OverviewMotion.Ease((now - started) / duration);
    }
    public (double From, double To, double Duration) Retarget(bool open, double now, double fullDuration)
    {
        if (!double.IsFinite(fullDuration) || fullDuration <= 0) throw new ArgumentOutOfRangeException(nameof(fullDuration));
        from = Value(now); target = open ? 1 : 0; started = now;
        duration = fullDuration * Math.Abs(target - from);
        return (from, target, duration);
    }
    public void Reset(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        from = target = value; duration = 0;
    }
}
