namespace BorderlessFlatWindow.Core.Canvas;

public static class OverviewTogglePolicy
{
    public static bool CapturesTab(bool open, bool preparing, nint host, nint foreground, bool modified, bool menuActive)
        => open && !modified && !menuActive && (preparing || host != 0 && host == foreground);
}
