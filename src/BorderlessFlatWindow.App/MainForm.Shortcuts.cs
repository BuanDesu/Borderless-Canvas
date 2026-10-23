using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    private readonly Queue<string> pendingShortcutLaunches = new();
    private ShortcutCommandChannel? shortcutChannel;
    private readonly string? initialFixedShortcut;
    private bool shortcutScheduled, systemInteractionSuspended;
    private void StartShortcutCommands()
    {
        if (showWithoutActivation || IsDisposed || shortcutChannel is not null) return;
        try
        {
            shortcutChannel = new(path =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated) throw new InvalidOperationException("Manager unavailable.");
                BeginInvoke(() => QueueFixedShortcut(path));
            });
            if (initialFixedShortcut is not null) QueueFixedShortcut(initialFixedShortcut);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            management.ReportEnvironmentWarning("快捷方式服务不可用：" + error.Message);
            if (initialFixedShortcut is not null)
                MessageBox.Show(this, "已有画布实例正在处理请求，未重复启动目标。请稍后重试。", "便签启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        ApplyShortcutMenu(preferences.Current.ShortcutMenu);
    }
    private void ApplyShortcutMenu(bool enabled)
    {
        if (showWithoutActivation || IsDisposed || Disposing) return;
        try { if (enabled) ShortcutPinVerb.Install(Environment.ProcessPath ?? throw new InvalidOperationException("未找到主程序路径。")); else ShortcutPinVerb.Remove(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        { management.ReportEnvironmentWarning("软件快捷方式右键入口未更新，可从更多菜单重试：" + error.Message); }
    }
    private void QueueFixedShortcut(string path)
    {
        if (IsDisposed || Disposing || desktopLifecycleStopped || pendingShortcutLaunches.Count >= 8 || systemInteractionSuspended) return;
        pendingShortcutLaunches.Enqueue(path); ScheduleFixedShortcut();
    }
    private void ScheduleFixedShortcut()
    {
        if (shortcutScheduled || pendingShortcutLaunches.Count == 0 || systemInteractionSuspended || desktopLifecycleStopped || IsDisposed || Disposing
            || !IsHandleCreated || !management.CanUsePanelCommands || management.HasOverviewWork || recovery.IsRunning
            || OwnedForms.Any(form => form.Visible && form.Modal)) return;
        shortcutScheduled = true;
        BeginInvoke(() =>
        {
            shortcutScheduled = false;
            if (pendingShortcutLaunches.Count == 0 || systemInteractionSuspended || desktopLifecycleStopped || IsDisposed || Disposing
                || !management.CanUsePanelCommands || management.HasOverviewWork || recovery.IsRunning) return;
            management.LaunchFixedShortcut(pendingShortcutLaunches.Dequeue()); ScheduleFixedShortcut();
        });
    }
    private void StopShortcutCommands() { pendingShortcutLaunches.Clear(); shortcutChannel?.Dispose(); shortcutChannel = null; }
}
