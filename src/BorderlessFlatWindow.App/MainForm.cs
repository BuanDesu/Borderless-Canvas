using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed class MainForm : Form
{
    private readonly CanvasWorkspace workspace = new(new SizeD(1280, 720));
    private readonly ITrackedDesktopWindowCatalog catalog;
    private readonly CanvasWorkspace liveWorkspace = new(new SizeD(1280, 720));
    private readonly DesktopSpatialModel spatial;
    private readonly CanvasPreviewControl livePreview;
    private readonly Label liveStatus = new() { Dock = DockStyle.Bottom, Height = 88, Padding = new(10) };
    private readonly WindowEventBuffer eventBuffer = new();
    private readonly System.Windows.Forms.Timer eventTimer = new() { Interval = 40 };
    private WindowEventListener? listener;
    private readonly bool showWithoutActivation;
    protected override bool ShowWithoutActivation => showWithoutActivation;
    private readonly CanvasPreviewControl preview;
    private readonly ToolStripStatusLabel cameraStatus = new();
    private readonly Label catalogSummary = new() { AutoSize = true, Margin = new Padding(12, 9, 0, 0) };
    private readonly CheckBox showExcluded = new() { Text = "显示被排除的窗口", AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
    private WindowCatalogSnapshot? snapshot;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly DisplayDiagnosticsControl displays;
    private readonly RecoveryDiagnosticsControl recovery;
    private readonly ManagedCanvasControl management;
    private readonly ListView windowList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true
    };

    internal MainForm(ITrackedDesktopWindowCatalog catalog, bool showWithoutActivation = false, string? journalPath = null)
    {
        this.catalog = catalog;
        this.showWithoutActivation = showWithoutActivation;
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
        spatial = new(liveWorkspace, new PixelRect(0, 0, 1280, 720));
        livePreview = new(liveWorkspace) { Dock = DockStyle.Fill, AutoFit = false };
        livePreview.WindowChosen += id => { liveWorkspace.FocusWindow(id); livePreview.Invalidate(); };
        livePreview.CameraPanRequested += delta => { liveWorkspace.PanBy(delta); livePreview.Invalidate(); };
        eventTimer.Tick += (_, _) => FlushEvents();

        management = new(catalog, journalPath) { Dock = DockStyle.Fill };
        management.StatusChanged += UpdateCameraStatus;
        management.RecoveryRequested += () => tabs.SelectedIndex = 5;
        var managementTab = new TabPage("窗口画布");
        managementTab.Controls.Add(management);
        tabs.TabPages.Add(managementTab);

        var canvasTab = new TabPage("空间模型预览");
        var catalogTab = new TabPage("Windows 窗口候选（只读）");
        tabs.TabPages.Add(canvasTab);
        tabs.TabPages.Add(catalogTab);
        var displayTab = new TabPage("显示器与坐标（只读）");
        displays = new(new BorderlessFlatWindow.Windows.DesktopDisplayCatalog(), layout =>
        {
            workspace.SetViewportSize(layout.Primary.WorkArea.Size);
            management.SetDisplay(layout);
            liveWorkspace.SetViewportSize(layout.Primary.WorkArea.Size);
            spatial.WorkArea = layout.Primary.WorkArea;
            if (snapshot is not null) UpdateLiveMap();
            UpdateCameraStatus();
            preview.Invalidate();
        }) { Dock = DockStyle.Fill };
        displayTab.Controls.Add(displays);
        tabs.TabPages.Add(displayTab);
        var liveTab = new TabPage("真实空间地图（只读）");
        liveTab.Controls.Add(livePreview);
        liveTab.Controls.Add(liveStatus);
        var liveActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new(6) };
        var fit = new Button { Text = "适配全部窗口", AutoSize = true };
        fit.Click += (_, _) => livePreview.FitToContent();
        var home = new Button { Text = "返回原点视角", AutoSize = true };
        home.Click += (_, _) => { liveWorkspace.ReturnHome(); livePreview.Invalidate(); };
        var reconcile = new Button { Text = "重新检查桌面", AutoSize = true };
        reconcile.Click += (_, _) => RefreshCatalog();
        liveActions.Controls.AddRange([fit, home, reconcile]);
        liveTab.Controls.Add(liveActions);
        tabs.TabPages.Add(liveTab);
        var recoveryTab = new TabPage("恢复日志");
        recovery = new(journalPath) { Dock = DockStyle.Fill };
        recoveryTab.Controls.Add(recovery);
        tabs.TabPages.Add(recoveryTab);
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

        windowList.Columns.Add("标题", 230);
        windowList.Columns.Add("准入结果", 260);
        windowList.Columns.Add("进程 ID", 85);
        windowList.Columns.Add("所属窗口", 105);
        windowList.Columns.Add("模型 ID", 110);
        windowList.Columns.Add("窗口类", 210);
        catalogTab.Controls.Add(windowList);
        var catalogActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6) };
        var refresh = new Button { Text = "检查窗口（只读）", AutoSize = true };
        refresh.Click += (_, _) => RefreshCatalog();
        showExcluded.CheckedChanged += (_, _) => RenderSnapshot();
        catalogActions.Controls.AddRange([refresh, showExcluded, catalogSummary]);
        catalogTab.Controls.Add(catalogActions);

        var intro = new Label
        {
            Dock = DockStyle.Top, Height = 96, Padding = new Padding(14),
            Text = "连续二维空间 / 原生比例操作 / 保留位置的总览\n勾选窗口开始画布管理；使用按钮或地图内滚轮平移，暂停会恢复原位。"
        };
        var status = new StatusStrip();
        status.Items.Add(cameraStatus);
        Controls.Add(tabs);
        Controls.Add(intro);
        Controls.Add(status);
        UpdateCameraStatus();
        displays.RefreshDisplays();
        if (recovery.RefreshJournal() > 0 && !showWithoutActivation) tabs.SelectedIndex = 5;
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

    private void UpdateCameraStatus()
    {
        var managed = management.Diagnostics();
        cameraStatus.Text = tabs.SelectedIndex == 0
            ? $"画布相机 ({managed.Camera.X:0}, {managed.Camera.Y:0}) · 窗口 {managed.Count} · {(managed.Running ? "管理中" : "未接管 / 已暂停")}" :
                $"示例相机 ({workspace.Camera.Position.X:0}, {workspace.Camera.Position.Y:0}) · 示例窗口 {workspace.Windows.Count}";
    }

    private void RefreshCatalog()
    {
        try
        {
            snapshot = catalog.Inspect();
            UpdateLiveMap();
            RenderSnapshot();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            catalogSummary.Text = $"读取失败；保留上次结果：{exception.Message}";
            liveStatus.Text = catalogSummary.Text;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!showWithoutActivation) management.InitializeGlobalInput();
        try
        {
            listener = new();
            listener.EventReceived += change =>
            {
                eventBuffer.Add(change);
                if (!eventTimer.Enabled) eventTimer.Start();
            };
            RefreshCatalog();
        }
        catch (Win32Exception exception) { liveStatus.Text = $"事件监听未启动：{exception.Message}；可手动检查。"; }
    }

    private void FlushEvents()
    {
        eventTimer.Stop();
        try
        {
            snapshot = eventBuffer.Drain(catalog);
            if (IsDisposed) return;
            UpdateLiveMap();
            RenderSnapshot();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        { liveStatus.Text = $"事件更新失败：{exception.Message}；保留地图，可重新检查桌面。"; }
    }

    private void UpdateLiveMap()
    {
        if (snapshot is null) return;
        management.Observe(snapshot, WindowEventListener.ForegroundWindow);
        spatial.Synchronize(snapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
        livePreview.ActiveWindowId = spatial.ActiveWindowId;
        livePreview.Invalidate();
        liveStatus.Text = $"真实窗口 {liveWorkspace.Windows.Count} · {(listener?.IsRunning == true ? "WinEvent 实时监听" : "手动快照")} · {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}\n点击只改变地图视角；窗口相对位置保持。金框标记前台窗口，适配按钮可找回范围外的窗口。";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            eventTimer.Dispose();
            listener?.Dispose();
            listener = null;
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is 0x007E or 0x02E0 && management is not null && IsHandleCreated && !IsDisposed)
            BeginInvoke(() => { management.Pause("显示配置发生变化，已暂停恢复。"); displays.RefreshDisplays(); RefreshCatalog(); });
    }

    internal void PrepareSampleSmoke() => tabs.SelectedIndex = 1;

    internal void PrepareCatalogSmoke()
    {
        tabs.SelectedIndex = 2;
        showExcluded.Checked = true;
        RefreshCatalog();
    }

    internal void PrepareDisplaySmoke()
    {
        tabs.SelectedIndex = 3;
        displays.RefreshDisplays();
    }

    internal void PrepareLiveSmoke()
    {
        tabs.SelectedIndex = 4;
        RefreshCatalog();
        livePreview.FitToContent();
    }

    internal (int WindowCount, bool Watching) GetTrackingDiagnostics() =>
        (liveWorkspace.Windows.Count, listener?.IsRunning == true);

    internal void PrepareRecoverySmoke()
    {
        tabs.SelectedIndex = 5;
        recovery.RefreshJournal();
    }

    private void RenderSnapshot()
    {
        if (snapshot is null) return;
        var selected = windowList.SelectedItems.Count == 0 ? null : windowList.SelectedItems[0].Tag as Guid?;
        windowList.BeginUpdate();
        try
        {
            windowList.Items.Clear();
            foreach (var entry in snapshot.Entries.Where(entry => showExcluded.Checked || entry.Admission.IsAdmitted)
                .OrderByDescending(entry => entry.Admission.IsAdmitted)
                .ThenByDescending(entry => entry.Observation.IsVisible))
            {
                var window = entry.Observation;
                var item = new ListViewItem([
                    string.IsNullOrEmpty(window.Title) ? "（无标题）" : window.Title,
                    entry.Admission.Description, window.ProcessId.ToString(),
                    window.OwnerHandle == 0 ? "—" : $"0x{window.OwnerHandle:X}",
                    entry.Id?.ToString("N")[..8] ?? "—", window.ClassName
                ]) { Tag = entry.Id, ForeColor = entry.Admission.IsAdmitted ? SystemColors.WindowText : SystemColors.GrayText };
                item.Selected = entry.Id is not null && entry.Id == selected;
                windowList.Items.Add(item);
            }
            catalogSummary.Text = $"可纳入 {snapshot.AdmittedWindows.Count} / 扫描 {snapshot.Entries.Count} · {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
        }
        finally
        {
            windowList.EndUpdate();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (management.HasOverview) return management.ProcessOverviewKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        if (tabs.SelectedIndex == 0 && management.ProcessNavigationKey(keyData)) return true;
        if (tabs.SelectedIndex != 1) return base.ProcessCmdKey(ref msg, keyData);
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

    internal void PresentOverview(SpatialOverviewForm overview, Action completed)
    {
        var oldBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var oldState = WindowState; var oldBorder = FormBorderStyle; var oldTopMost = TopMost; var oldTitle = Text;
        var visible = Controls.Cast<Control>().Where(control => control.Visible).ToArray();
        var oldControl = ActiveControl;
        var restored = false;
        void Restore()
        {
            if (restored) return; restored = true;
            try
            {
                var activate = ForegroundWindowBookmark.IsApplicationForeground;
                if (!IsDisposed && !Disposing)
                {
                    Controls.Remove(overview);
                    WindowState = FormWindowState.Normal; FormBorderStyle = oldBorder; Bounds = oldBounds;
                    TopMost = oldTopMost; Text = oldTitle;
                    foreach (var control in visible) if (!control.IsDisposed) control.Show();
                    WindowState = oldState;
                    if (activate && !showWithoutActivation && oldState != FormWindowState.Minimized) { Activate(); oldControl?.Select(); }
                }
            }
            finally { completed(); }
        }
        overview.FormClosed += (_, _) => Restore();
        try
        {
            foreach (var control in visible) control.Hide();
            WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None;
            Bounds = overview.Bounds; Text = "BFW 空间总览"; TopMost = true;
            overview.TopMost = false; overview.TopLevel = false; overview.Dock = DockStyle.Fill;
            Controls.Add(overview); overview.BringToFront(); overview.Show(); if (!showWithoutActivation) Activate();
        }
        catch { Restore(); throw; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && management.HasOverview)
        { e.Cancel = true; management.CancelOverview(); return; }
        base.OnFormClosing(e);
    }
}
