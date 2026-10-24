namespace BorderlessFlatWindow.Application.Navigation;

/// <summary>Accumulates precision wheel packets into discrete window selections.</summary>
public sealed class WindowCycleWheel
{
    private int remainder;
    private long? last;
    public int Add(int delta, long timestamp)
    {
        if (delta == 0) return 0;
        if (last is { } previous && (timestamp < previous || timestamp - previous >= 400)) remainder = 0;
        last = timestamp;
        var total = (long)remainder + delta;
        remainder = (int)(total % 120);
        return (int)Math.Clamp(total / 120, -12, 12);
    }
    public void Reset() { remainder = 0; last = null; }
}
