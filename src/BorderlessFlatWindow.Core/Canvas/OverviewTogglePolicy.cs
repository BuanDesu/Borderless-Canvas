namespace BorderlessFlatWindow.Core.Canvas;

public static class OverviewTogglePolicy
{
    public static bool CapturesTab(bool open, bool preparing, nint host, nint foreground, bool modified, bool menuActive,
        bool hostUnderPointer = false, bool hostEnabled = true)
        => open && !modified && !menuActive && hostEnabled
            && (preparing || host != 0 && (host == foreground || hostUnderPointer));
}
