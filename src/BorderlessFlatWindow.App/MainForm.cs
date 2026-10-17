using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class MainForm : Form
{
    private readonly CanvasWorkspace workspace = new(new SizeD(1280, 720));
    private readonly IDesktopWindowCatalog catalog;
    private readonly CanvasPreviewControl preview;
    private readonly ToolStripStatusLabel cameraStatus = new();
    private readonly ListView windowList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true
    };

    internal MainForm(IDesktopWindowCatalog catalog)
    {
        this.catalog = catalog;
        Text = "Borderless Flat Window · 空间画布基础项目";
        ClientSize = new Size(1100, 740);
        MinimumSize = new Size(800, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        KeyPreview = true;

        workspace.UpsertWindow(Sample("编辑器", 80, 90, 820, 600));
        workspace.UpsertWindow(Sample("浏览器", 980, 90, 900, 600));
        workspace.UpsertWindow(Sample("资料", 80, -640, 820, 600));
        workspace.UpsertWindow(Sample("终端", -740, 90, 720, 600));
        preview = new(workspace) { Dock = DockStyle.Fill };
        preview.WindowChosen += id => Navigate(() => workspace.FocusWindow(id));
        preview.CameraPanRequested += delta => Navigate(() => workspace.PanBy(delta));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var canvasTab = new TabPage("空间模型预览");
        var catalogTab = new TabPage("Windows 窗口候选（只读）");
        tabs.TabPages.Add(canvasTab);
        tabs.TabPages.Add(catalogTab);
        canvasTab.Controls.Add(preview);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(6), AutoSize = false
        };
        AddButton(navigation, "←", () => workspace.PanBy(new(-240, 0)));
        AddButton(navigation, "↑", () => workspace.PanBy(new(0, -180)));
        AddButton(navigation, "↓", () => workspace.PanBy(new(0, 180)));
        AddButton(navigation, "→", () => workspace.PanBy(new(240, 0)));
        AddButton(navigation, "返回原点", workspace.ReturnHome);
        AddButton(navigation, "上一视角", () => workspace.ReturnToPreviousView());
        canvasTab.Controls.Add(navigation);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(10),
            Text = "示例矩形仅验证空间位置。点击聚焦；滚轮纵移，Shift＋滚轮横移。\nHome 返回原点；Backspace 返回上一视角。"
        };
        canvasTab.Controls.Add(hint);

        windowList.Columns.Add("标题", 450);
        windowList.Columns.Add("窗口类", 230);
        windowList.Columns.Add("进程 ID", 100);
        catalogTab.Controls.Add(windowList);
        var refresh = new Button { Text = "刷新候选列表（只读）", Dock = DockStyle.Top, Height = 42 };
        refresh.Click += (_, _) => RefreshCatalog();
        catalogTab.Controls.Add(refresh);

        var intro = new Label
        {
            Dock = DockStyle.Top, Height = 96, Padding = new Padding(14),
            Text = "连续二维空间 / 原生比例操作 / 保留位置的总览\n项目骨架：仅模型预览和只读枚举；真实窗口接管尚未接入。"
        };
        var status = new StatusStrip();
        status.Items.Add(cameraStatus);
        Controls.Add(tabs);
        Controls.Add(intro);
        Controls.Add(status);
        UpdateCameraStatus();
    }

    private static CanvasWindow Sample(string title, double x, double y, double width, double height) =>
        new(Guid.NewGuid(), title, new(new PointD(x, y), new SizeD(width, height)));

    private void AddButton(FlowLayoutPanel parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32 };
        button.Click += (_, _) => Navigate(action);
        parent.Controls.Add(button);
    }

    private void Navigate(Action action)
    {
        action();
        UpdateCameraStatus();
        preview.Invalidate();
    }

    private void UpdateCameraStatus() => cameraStatus.Text =
        $"相机 ({workspace.Camera.Position.X:0}, {workspace.Camera.Position.Y:0}) · 示例窗口 {workspace.Windows.Count} · 视口 1280 × 720";

    private void RefreshCatalog()
    {
        windowList.BeginUpdate();
        try
        {
            windowList.Items.Clear();
            foreach (var window in catalog.GetWindows())
                windowList.Items.Add(new ListViewItem([window.Title, window.ClassName, window.ProcessId.ToString()]));
        }
        catch (Win32Exception exception)
        {
            cameraStatus.Text = $"窗口候选读取失败：{exception.NativeErrorCode}。可稍后重新刷新。";
        }
        finally
        {
            windowList.EndUpdate();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Home:
                Navigate(workspace.ReturnHome);
                return true;
            case Keys.Back:
                Navigate(() => workspace.ReturnToPreviousView());
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
