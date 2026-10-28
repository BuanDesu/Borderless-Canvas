using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.App;

internal sealed class WorkspaceProgressForm : Form
{
    private readonly ListBox results = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 56, Padding = new(12) };
    private readonly ChromeButton cancel = new() { Text = "取消复原", Dock = DockStyle.Bottom, Height = 44 };
    private bool active;
    internal event Action? CancelRequested;
    internal WorkspaceProgressForm(string name, bool synthetic)
    {
        Text = "工作区 · " + name; ClientSize = new(560, 300); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
        if (synthetic) Opacity = 0;
        Controls.Add(results); Controls.Add(summary); Controls.Add(cancel); ChromeTheme.Apply(this);
        cancel.Click += (_, _) => { if (active) CancelRequested?.Invoke(); else Close(); };
        FormClosing += (_, _) => { if (active) CancelRequested?.Invoke(); };
    }
    protected override bool ShowWithoutActivation => true;
    internal void Present() { if (!IsDisposed && !Visible) Show(); }
    internal void SetResults(IReadOnlyList<WorkspaceRestoreResult> values, bool running)
    {
        if (IsDisposed) return;
        active = running;
        summary.Text = running ? "正在复用或启动窗口并核验布局；已启动的软件不会因取消而关闭。" : "复原结束。失败条目可从总览卡片 → 查看成员/启动项调整。";
        cancel.Text = running ? "取消复原" : "关闭";
        results.BeginUpdate(); results.Items.Clear(); foreach (var value in values) results.Items.Add(value.Label + " · " + value.State); results.EndUpdate();
    }
}
