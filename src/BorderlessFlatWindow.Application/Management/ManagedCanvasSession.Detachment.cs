namespace BorderlessFlatWindow.Application.Management;

public sealed partial class ManagedCanvasSession
{
    public bool DetachWindow(Guid id)
    {
        if (!IsRunning || busy || NavigationSuspended || HasPending || focusMotion is not null || !managed.TryGetValue(id, out var window))
        { Message = "请等待视角移动完成后再固定窗口。"; return false; }
        if (window.Parked) { Message = "请先定位到该窗口，再固定到屏幕。"; return false; }
        busy = true;
        try
        {
            if (recovery?.ReleaseOwnership(window.RecoveryId) != true) { Message = "窗口身份或恢复所有权已变化，未固定。"; return false; }
            managed.Remove(id); Workspace.RemoveWindow(id); InvalidateArrangementFor(id);
            if (ActiveWindowId == id) ActiveWindowId = null;
            activation = cycleTarget = null;
            Message = "窗口已固定为便签，保留当前位置与层级。"; return true;
        }
        catch (Exception error) when (IsExpected(error)) { PauseInternal($"固定窗口未完成，已暂停恢复：{error.Message}"); return false; }
        finally { ExitBusy(); }
    }
}
