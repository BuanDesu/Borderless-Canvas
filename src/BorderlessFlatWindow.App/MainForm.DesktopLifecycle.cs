using System.ComponentModel;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    private readonly DesktopLifecycleQueue desktopChanges = new();
    private DesktopSessionNotifications? sessionNotifications;
    private bool desktopChangeScheduled, processingDesktopChange, nativeOwnerDisabled;
    private bool desktopLifecycleStopped;
    private long desktopHandleGeneration;
    private string? sessionNotificationWarning;
    private int appliedDesktopBatches;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        nativeOwnerDisabled = !Enabled;
        EnsureSessionNotifications(); ScheduleDesktopChange();
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        sessionNotifications?.Dispose(); sessionNotifications = null;
        desktopHandleGeneration++; desktopChangeScheduled = false;
        base.OnHandleDestroyed(e);
    }
    private void EnsureSessionNotifications()
    {
        if (desktopLifecycleStopped || showWithoutActivation || !IsHandleCreated || IsDisposed || Disposing || sessionNotifications is not null) return;
        try
        {
            sessionNotifications = new(Handle);
            if (sessionNotificationWarning is { } previous) management?.ClearEnvironmentWarning(previous);
            sessionNotificationWarning = null;
        }
        catch (Win32Exception error)
        {
            sessionNotificationWarning = $"会话通知未启用（{error.NativeErrorCode}）；锁屏前请先暂停画布，可重新打开面板重试。";
            management?.ReportEnvironmentWarning(sessionNotificationWarning);
        }
    }
    private void QueueDesktopChange(DesktopLifecycleSignal signal)
    {
        if (desktopLifecycleStopped || IsDisposed || Disposing) return;
        desktopChanges.Request(signal);
        management?.SetDesktopTransitionPending(true);
        if (pendingManagerTarget is not (null or ManagerTarget.Exit))
        { pendingManagerTarget = null; management?.SetPanelActionPending(false); }
        ScheduleDesktopChange();
    }
    private void ScheduleDesktopChange()
    {
        if (desktopLifecycleStopped || !desktopChanges.HasWork || desktopChangeScheduled || processingDesktopChange || nativeOwnerDisabled
            || management is null || !management.CanApplyDesktopTransition || !IsHandleCreated || IsDisposed || Disposing
            || OwnedForms.Any(form => form.Visible && form.Modal)) return;
        desktopChangeScheduled = true; var generation = desktopHandleGeneration;
        BeginInvoke(() =>
        {
            if (generation != desktopHandleGeneration) return;
            desktopChangeScheduled = false;
            if (desktopLifecycleStopped || IsDisposed || Disposing || nativeOwnerDisabled || !management.CanApplyDesktopTransition || OwnedForms.Any(form => form.Visible && form.Modal)) return;
            if (desktopChanges.Begin() is not { } work) return;
            processingDesktopChange = true;
            try
            {
                management.Pause(work.PauseReason);
                if (work.RequiresRefresh) { EnsureSessionNotifications(); displays.RefreshDisplays(); RefreshCatalog(); }
                appliedDesktopBatches++;
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            { management.InvalidateDisplay($"系统状态检查未完成；请重新检查设备后再开始：{error.Message}"); }
            finally
            {
                desktopChanges.Complete(work); processingDesktopChange = false;
                if (!IsDisposed && !Disposing)
                {
                    management.SetDesktopTransitionPending(desktopChanges.HasWork);
                    if (!desktopChanges.HasWork && snapshot is not null) management.Observe(snapshot, WindowEventListener.ForegroundWindow);
                    ScheduleDesktopChange();
                }
            }
        });
    }
    private void HandleDesktopMessage(ref Message message)
    {
        if (desktopLifecycleStopped) return;
        if (message.Msg == 0x000A)
        {
            nativeOwnerDisabled = message.WParam == 0;
            UpdateTrayState();
            if (!nativeOwnerDisabled) { ScheduleDesktopChange(); TryScheduleManagerAction(); }
        }
        if (DesktopSessionNotifications.TryRead(message.Msg, unchecked((nuint)message.WParam), out var signal))
        { QueueDesktopChange(signal); if (message.Msg == 0x0218) message.Result = 1; }
    }
    private void StopDesktopLifecycle()
    {
        desktopLifecycleStopped = true; desktopHandleGeneration++; desktopChangeScheduled = false;
        sessionNotifications?.Dispose(); sessionNotifications = null;
    }
}
