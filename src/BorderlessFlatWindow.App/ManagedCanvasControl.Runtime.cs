namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    internal string CaptureRuntimeInformation() =>
        $"画布：{SessionStatus}\r\n输入接管：{(touchNavigation is not null ? "触控导航窗口接收" : globalInput?.State.Available == true ? "已启用" : "已停用")}\r\n模式：{(touchNavigation is not null ? "触控导航" : globalInput?.State.Mode.ToString() ?? "尚未初始化")}\r\n"
        + $"钩子：{globalInput?.HookHealth ?? "尚未初始化"}\r\n准星输入：{globalInput?.PointerSourceMessage ?? "尚未进入"}\r\n"
        + $"最近退出：{globalInput?.LastExitReason ?? "尚无记录"}\r\n\r\n{navigationStatus.Text}\r\n{overviewShortcutStatus.Text}\r\n{globalInput?.PointerMessage}\r\n{session?.Message}";

    internal void ShowRuntimeInformation(string? captured = null)
    {
        var report = captured ?? CaptureRuntimeInformation();
        EndNavigationForEditing();
        using var dialog = new Form
        {
            Text = "输入状态", Name = "InputStatus", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new(440, 480), MinimumSize = new(380, 400), ShowInTaskbar = false,
            MinimizeBox = false, MaximizeBox = false, Padding = new(16)
        };
        var content = new TextBox
        {
            Name = "InputStatusReport", Text = report, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10), AccessibleName = "输入状态详情"
        };
        var close = new ChromeButton { Text = "关闭", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 44 };
        dialog.Controls.Add(content); dialog.Controls.Add(close); dialog.CancelButton = close; dialog.AcceptButton = close;
        ChromeTheme.Apply(dialog);
        dialog.ShowDialog((Control?)FindForm() ?? this);
        RefreshState();
    }
}
