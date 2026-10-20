namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Serializes overview input ownership across asynchronous show/close callbacks.</summary>
public sealed class OverviewInputSession
{
    public long Generation { get; private set; }
    public bool IsOpen { get; private set; }
    public bool Preparing { get; private set; }
    public nint Host { get; private set; }
    public nint RequestForeground { get; private set; }

    public bool Begin(long generation, nint foreground = 0)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        if (generation <= Generation) return false;
        Generation = generation; IsOpen = Preparing = true; Host = 0; RequestForeground = foreground; return true;
    }
    public bool AttachHost(long generation, nint host, nint foreground)
    {
        if (!IsOpen || generation != Generation || host == 0) return false;
        Host = host; ObserveForeground(foreground); return true;
    }
    public void ObserveForeground(nint foreground)
    {
        // Shown can precede activation. Preparation ends only after our host really
        // becomes foreground; subsequent modal/application Tab must pass through.
        if (IsOpen && Host != 0 && Host == foreground) Preparing = false;
    }
    public bool Close(long generation)
    {
        if (!IsOpen || generation != Generation) return false;
        IsOpen = Preparing = false; Host = RequestForeground = 0; return true;
    }
    public bool CapturesTab(nint foreground, bool modified, bool menuActive, bool hostUnderPointer = false, bool hostEnabled = true)
    {
        ObserveForeground(foreground);
        // A request may be shown without acquiring foreground (notably after a
        // shell window was selected). Its visible surface still owns desktop Tab.
        // Preparation must not capture Tab after the user switches to another app.
        var preparingHere = Preparing && foreground == RequestForeground;
        return OverviewTogglePolicy.CapturesTab(IsOpen, preparingHere, Host, foreground, modified, menuActive, hostUnderPointer, hostEnabled);
    }
}
