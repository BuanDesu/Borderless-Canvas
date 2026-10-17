using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Probe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new ProbeForm());
    }
}

internal sealed class ProbeForm : Form
{
    private readonly AnimatedWindow source = new();
    private readonly Panel previewArea = new() { Bounds = new(20, 130, 350, 250), BackColor = Color.FromArgb(25, 30, 40) };
    private readonly Label status = new() { Bounds = new(20, 390, 690, 120) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private DwmWindowPreview? preview;
    private RecoverySession? session;
    private Region? savedRegion;
    private bool clipped;
    private string detail = "测试窗口保持原位。";

    internal ProbeForm()
    {
        Text = "BFW Probe Controller"; ClientSize = new(740, 530); StartPosition = FormStartPosition.CenterScreen;
        Font = new("Microsoft YaHei UI", 10);
        Controls.Add(new Label { Text = "受控测试窗口 / DWM 预览 / 恢复验证", Bounds = new(20, 15, 690, 32) });
        var actions = new FlowLayoutPanel { Bounds = new(15, 55, 710, 60) };
        Add(actions, "停放测试窗口", Park);
        Add(actions, "返回原位", Restore);
        Add(actions, "裁剪测试窗口", Clip);
        Add(actions, "打开所属弹窗", () => new Form { Text = "BFW Probe Dialog", Size = new(300, 200), StartPosition = FormStartPosition.CenterScreen }.Show(source));
        Add(actions, "关闭实验", Close);
        Controls.Add(actions); Controls.Add(previewArea); Controls.Add(status);
        Controls.Add(new Label { Text = "左侧是 DWM 实时预览。\n观察序号与颜色是否变化。\n\n本程序只移动自己的测试窗口，\n关闭实验会尝试恢复位置和区域。\n\nSource 窗口也可用于后续\n真实画布的输入与导航验证。", Bounds = new(390, 140, 330, 230) });
        timer.Tick += (_, _) => RefreshStatus();
        Resize += (_, _) => UpdatePreview();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); source.Show(); preview = DwmWindowPreview.TryCreate(Handle, source.Handle);
        UpdatePreview(); timer.Start(); RefreshStatus();
    }
    private static void Add(FlowLayoutPanel actions, string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true };
        button.Click += (_, _) => action(); actions.Controls.Add(button);
    }
    private void Park()
    {
        try
        {
            Restore();
            var catalog = new DesktopWindowCatalog(0); catalog.RefreshWindow(source.Handle);
            var window = catalog.Current.AdmittedWindows.Single(window => window.Handle == source.Handle);
            var path = Path.Combine(AppContext.BaseDirectory, "probe-state", Guid.NewGuid().ToString("N"), "recovery.json");
            session = new(new JsonRecoveryJournalStore(path), new WindowsRecoveryWindowPort(0));
            var entry = session.Register(window.Identity);
            var layout = new DesktopDisplayCatalog().ReadDisplays();
            var planner = new WindowPlacementPlanner(layout.Primary.WorkArea, layout.Monitors.Select(monitor => monitor.Bounds));
            if (!planner.TryPlan(new(new PointD(1e12, 1e12), new SizeD(source.Width, source.Height)), PointD.Zero, out var plan))
                throw new InvalidOperationException("没有可用的有界停放位置。");
            var receipt = new RegisteredWindowMover().RequestMove(session, entry.Id, plan!.PhysicalBounds);
            detail = receipt.Detail; RefreshStatus();
        }
        catch (Exception exception) { detail = exception.Message; RefreshStatus(); }
    }
    private void Clip()
    {
        Restore(); savedRegion = source.Region?.Clone(); clipped = true;
        source.Region = new Region(Rectangle.Empty); detail = "空区域裁剪，仅限本进程测试窗口。"; RefreshStatus();
    }
    private void Restore()
    {
        if (source.IsDisposed) { detail = "Source 已关闭。"; return; }
        if (clipped) { source.Region = savedRegion?.Clone(); savedRegion?.Dispose(); savedRegion = null; clipped = false; }
        if (session is not null)
        {
            var result = session.RecoverAll();
            if (!result.IsComplete) throw new InvalidOperationException($"仍有 {result.Remaining} 项待恢复。");
            session.Dispose(); session = null;
        }
        detail = "测试窗口已返回原位。"; RefreshStatus();
    }
    private void UpdatePreview()
    {
        if (preview is null || IsDisposed) return;
        var point = PointToClient(previewArea.PointToScreen(Point.Empty));
        preview.Update(new(point.X, point.Y, point.X + previewArea.Width, point.Y + previewArea.Height));
    }
    private void RefreshStatus()
    {
        status.Text = source.IsDisposed ? "Source 已关闭。" :
            $"Source 帧序号：{source.Frame} · 位置 ({source.Left}, {source.Top}) · 尺寸 {source.Width} × {source.Height}\n"
            + $"DWM：{(preview is null ? "不可用，使用标题卡片" : "已注册；画面刷新须观察")} · 区域裁剪：{clipped}\n{detail}";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose(); preview?.Dispose();
            try { Restore(); } finally { session?.Dispose(); source.Dispose(); savedRegion?.Dispose(); }
        }
        base.Dispose(disposing);
    }
}

internal sealed class AnimatedWindow : Form
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 100 };
    internal int Frame;
    internal AnimatedWindow()
    {
        Text = "BFW Probe Source"; Bounds = new(100, 100, 440, 310); DoubleBuffered = true;
        Controls.Add(new TextBox { Bounds = new(20, 190, 300, 32), Text = "在此测试正常文字输入" });
        animation.Tick += (_, _) => { Frame++; Invalidate(); }; animation.Start();
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(Frame % 2 == 0 ? Color.Teal : Color.DarkSlateBlue);
        using var font = new Font("Segoe UI", 28);
        e.Graphics.DrawString($"FRAME {Frame}", font, Brushes.White, 18, 30);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
