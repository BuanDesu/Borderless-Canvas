using System.ComponentModel;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.App;

internal sealed class DisplayDiagnosticsControl : UserControl
{
    private readonly IDesktopDisplayCatalog catalog;
    private readonly Action<DisplayLayout> onRead;
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 88, Padding = new(10) };

    internal DisplayDiagnosticsControl(IDesktopDisplayCatalog catalog, Action<DisplayLayout> onRead)
    {
        this.catalog = catalog;
        this.onRead = onRead;
        list.Columns.Add("显示器", 140);
        list.Columns.Add("主屏", 60);
        list.Columns.Add("屏幕物理边界（左上 / 右下）", 290);
        list.Columns.Add("可用工作区（左上 / 右下）", 290);
        list.Columns.Add("有效 DPI", 160);
        var refresh = new Button { Text = "刷新显示器（只读）", AutoSize = true };
        refresh.Click += (_, _) => RefreshDisplays();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new(6) };
        actions.Controls.Add(refresh);
        var explanation = new Label
        {
            Dock = DockStyle.Bottom, Height = 112, Padding = new(10),
            Text = "工作区由 Windows 提供，保留任务栏等系统区域；边界允许负坐标。\n物理坐标 = 世界坐标 − 相机位置 + 工作区左上角；一画布单位对应一物理像素。\nDPI 用于诊断，不对应用内容额外缩放。模型视口使用主屏工作区尺寸；尚未移动真实窗口。"
        };
        Controls.Add(list); Controls.Add(summary); Controls.Add(actions); Controls.Add(explanation);
    }

    internal void RefreshDisplays()
    {
        try
        {
            var layout = catalog.ReadDisplays();
            onRead(layout);
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var monitor in layout.Monitors.OrderByDescending(monitor => monitor.IsPrimary))
                    list.Items.Add(new ListViewItem([
                        monitor.DeviceName, monitor.IsPrimary ? "是" : "—", BoundsText(monitor.Bounds),
                        BoundsText(monitor.WorkArea), monitor.EffectiveDpi is { } dpi ? $"{dpi} ({dpi / 96d:P0})" : "未知"
                    ]));
            }
            finally { list.EndUpdate(); }
            summary.Text = $"{layout.Monitors.Count} 个显示器 · {layout.BaselineDescription}\n{layout.CapturedAt.ToLocalTime():HH:mm:ss} · 手动快照；配置变化后请刷新";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
        {
            summary.Text = $"读取失败，保留上次结果：{exception.Message}";
        }
    }

    private static string BoundsText(BorderlessFlatWindow.Core.Geometry.PixelRect rectangle) =>
        $"({rectangle.Left}, {rectangle.Top})–({rectangle.Right}, {rectangle.Bottom})";
}
