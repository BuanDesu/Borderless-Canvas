namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private InputStatusForm? inputStatusWindow;
    internal string CaptureRuntimeInformation() =>
        $"画布：{SessionStatus}\r\n输入接管：{(touchNavigation is not null ? "触控导航窗口接收" : globalInput?.State.Available == true ? "已启用" : "已停用")}\r\n模式：{(touchNavigation is not null ? "触控导航" : globalInput?.State.Mode.ToString() ?? "尚未初始化")}\r\n"
        + $"钩子：{globalInput?.HookHealth ?? "尚未初始化"}\r\n准星输入：{globalInput?.PointerSourceMessage ?? "尚未进入"}\r\n"
        + $"输入通知：{(globalInput?.HasPendingUiNotification == true ? "待消费" : "已同步")} · UI 泵{(navigationTimer.Enabled ? "运行" : "停止")} · 已投递 {deliveredInputWakes}\r\n"
        + $"最近退出：{globalInput?.LastExitReason ?? "尚无记录"}\r\n\r\n{navigationStatus.Text}\r\n{overviewShortcutStatus.Text}\r\n{globalInput?.PointerMessage}\r\n{session?.Message}";

    internal void ShowRuntimeInformation(string? captured = null)
    {
        var report = captured ?? CaptureRuntimeInformation();
        EndNavigationForEditing();
        if (inputStatusWindow is null || inputStatusWindow.IsDisposed)
        {
            var host = FindForm();
            inputStatusWindow = new(host is MainForm { IsTransparentFixture: true });
            inputStatusWindow.FormClosed += (_, _) => inputStatusWindow = null;
        }
        inputStatusWindow.SetReport(report);
        inputStatusWindow.TopMost = FindForm()?.TopMost == true;
        if (!inputStatusWindow.Visible) inputStatusWindow.Show((Control?)FindForm() ?? this);
        if (FindForm() is not MainForm { IsTransparentFixture: true }) inputStatusWindow.Activate();
    }
    private void DisposeInputStatus()
    {
        var window = inputStatusWindow; inputStatusWindow = null;
        window?.Dispose();
    }
}
