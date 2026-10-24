using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private TouchNavigationHost? touchNavigation;
    private readonly Queue<TimedNavigationInput> touchInputs = new();
    private bool touchNavigationRequested, touchInputOverflow;
    internal void ToggleTouchNavigation()
    { if (touchNavigation is not null || touchNavigationRequested) CloseTouchNavigation(); else ShowTouchNavigation(); }
    internal void ShowTouchNavigation()
    {
        if (session?.IsRunning != true) { notice = "请先开始画布，再进入触控导航。"; RefreshState(); return; }
        if (!CanUsePanelCommands || HasOverviewWork || touchNavigation is not null || touchNavigationRequested) return;
        touchNavigationRequested = true;
        BeginInvoke(() =>
        {
            if (!touchNavigationRequested) return;
            touchNavigationRequested = false;
            if (IsDisposed || Disposing || session?.IsRunning != true || !CanUsePanelCommands || HasOverviewWork || GlobalInputBlocked) return;
            EndNavigationForEditing(); InvalidateOverviewPreparation(); session.StopCameraTransition();
            var host = new TouchNavigationHost(); touchNavigation = host;
            host.Input += QueueTouchInput;
            host.ExitRequested += () => CloseTouchNavigation();
            RefreshState();
            try
            {
                if (globalInput?.SetTouchNavigation(true) != true) { CloseTouchNavigation(); notice = "画布输入尚未就绪，未进入触控模式。"; RefreshState(); return; }
                var areas = Screen.AllScreens.Select(screen => screen.WorkingArea).Where(area => area.Width > 0 && area.Height > 0).ToArray();
                var pointer = Cursor.Position;
                FindForm()?.Hide(); host.Show(areas, pointer); navigationTimer.Start();
            }
            catch (Exception error) when (Expected(error))
            { CloseTouchNavigation(discard: true); notice = "触控导航未能打开：" + error.Message; FindForm()?.Show(); RefreshState(); }
        });
    }
    private void QueueTouchInput(NavigationInput input)
    {
        if (IsDisposed || Disposing) return;
        if (touchInputs.Count >= 2048) touchInputOverflow = true;
        else touchInputs.Enqueue(new(input, DateTimeOffset.UtcNow));
        navigationTimer.Start();
    }
    private void CloseTouchNavigation(bool discard = false, bool refresh = true)
    {
        touchNavigationRequested = false;
        var host = touchNavigation; touchNavigation = null;
        if (host is not null)
        {
            host.Dispose(); globalInput?.SetTouchNavigation(false);
        }
        if (discard) { touchInputs.Clear(); touchInputOverflow = false; }
        if (host is not null && refresh && !IsDisposed && !Disposing) RefreshState();
    }
}
