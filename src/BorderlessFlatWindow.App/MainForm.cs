using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Windows.Bookmarks;
using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows.Preferences;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm : Form
{
    private readonly CanvasWorkspace workspace = new(new SizeD(1280, 720));
    private readonly ITrackedDesktopWindowCatalog catalog;
    private readonly CanvasWorkspace liveWorkspace = new(new SizeD(1280, 720));
    private readonly DesktopSpatialModel spatial;
    private readonly CanvasPreviewControl livePreview;
    private readonly WrapLabel liveStatus = new() { Dock = DockStyle.Bottom, Padding = new(10) };
    private readonly WindowEventBuffer eventBuffer = new();
    private readonly System.Windows.Forms.Timer eventTimer = new() { Interval = 40 };
    private WindowEventListener? listener;
    private readonly bool showWithoutActivation;
    private readonly bool reviewPanel;
    internal bool AllowsOverviewPreparation => !showWithoutActivation && !IsDisposed && !Disposing && pendingManagerTarget is null && !desktopChanges.HasWork;
    internal bool OverviewShowsInTaskbar => reviewPanel;
    protected override bool ShowWithoutActivation => showWithoutActivation;
    internal bool IsTransparentFixture => showWithoutActivation;
    internal bool AllowsCanvasCommands => !nativeOwnerDisabled && !OwnedForms.Any(form => form.Visible && form.Modal);
    private readonly CanvasPreviewControl preview;
    private readonly ToolStripStatusLabel cameraStatus = new();
    private readonly Label catalogSummary = new() { AutoSize = true, Margin = new Padding(12, 9, 0, 0) };
    private readonly CheckBox showExcluded = new() { Text = "显示被排除的窗口", AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
    private WindowCatalogSnapshot? snapshot;
    private readonly TabControl tabs = new ChromeTabs() { Dock = DockStyle.Fill };
    private readonly List<Control> verificationPages = [];
    private readonly List<TabPageTransition> pageTransitions = [];
    private NotifyIcon? tray;
    private readonly Icon brandIcon = BrandIcon.Load(32);
    private readonly Icon trayIcon = BrandIcon.Load(Math.Max(16, SystemInformation.SmallIconSize.Width));
    private ContextMenuStrip? trayMenu;
    private bool exitRequested;
    private readonly DisplayDiagnosticsControl displays;
    private readonly RecoveryDiagnosticsControl recovery;
    private readonly ManagedCanvasControl management;
    private readonly BookmarkLibrary bookmarks;
    private readonly PreferencesSession preferences;
    private readonly BookmarkControl bookmarkControl;
    private readonly ListView windowList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true
    };

    internal MainForm(ITrackedDesktopWindowCatalog catalog, bool showWithoutActivation = false, string? journalPath = null, string? bookmarksPath = null, bool reviewPanel = false, string? settingsPath = null, string? initialFixedShortcut = null)
    {
        this.initialFixedShortcut = initialFixedShortcut;
        this.reviewPanel = reviewPanel;
        this.catalog = catalog;
        this.showWithoutActivation = showWithoutActivation;
        if (!showWithoutActivation || settingsPath is not null)
            JsonPreferencesStore.EnsureSeparateDataFiles(settingsPath ?? JsonPreferencesStore.DefaultPath,
                journalPath ?? BorderlessFlatWindow.Windows.Recovery.JsonRecoveryJournalStore.DefaultPath, bookmarksPath ?? JsonBookmarkStore.DefaultPath);
        Text = "Borderless Flat Window · 无限桌面";
        ClientSize = showWithoutActivation ? new Size(1100, 740) : new Size(432, 700);
        MinimumSize = showWithoutActivation ? new Size(800, 560) : new Size(360, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        KeyPreview = true;
        Icon = brandIcon;

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

        preferences = new(() => showWithoutActivation && settingsPath is null ? new EphemeralPreferencesStore() : new JsonPreferencesStore(settingsPath ?? JsonPreferencesStore.DefaultPath));
        preferences.Reload(); followWallpaper = preferences.Current.FollowWallpaper;
        bookmarks = new(() => new JsonBookmarkStore(bookmarksPath ?? JsonBookmarkStore.DefaultPath)); bookmarks.Reload();
        management = new(catalog, journalPath, bookmarks, preferences) { Dock = DockStyle.Fill };
        management.DisplayRecheckRequested += () => QueueDesktopChange(BorderlessFlatWindow.Application.Management.DesktopLifecycleSignal.DisplayChanged);
        bookmarkControl = new(bookmarks, () => management.BookmarkPosition, () => management.CanJumpToBookmark,
            management.GoToBookmark, management.EndNavigationForEditing)
        { Dock = DockStyle.Fill };
        management.StatusChanged += UpdateCameraStatus;
        management.StatusChanged += bookmarkControl.RefreshNavigationState;
        management.WallpaperColorsChanged += enabled => { followWallpaper = enabled; _ = RefreshPaletteAsync(); };
        management.WallpaperRefreshRequested += () => { WallpaperPaletteReader.PreloadPreview(invalidate: true); _ = RefreshPaletteAsync(); };
        management.ShortcutMenuChanged += ApplyShortcutMenu;
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedIndex == 6) { management.EndNavigationForEditing(); bookmarkControl.RefreshNavigationState(); }
            UpdateCameraStatus();
        };
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
            workspace.SetViewportSize(layout.CanvasWorkArea.Size);
            management.SetDisplay(layout);
            liveWorkspace.SetViewportSize(layout.CanvasWorkArea.Size);
            spatial.WorkArea = layout.CanvasWorkArea;
            if (snapshot is not null) UpdateLiveMap();
            UpdateCameraStatus();
            preview.Invalidate();
        })
        { Dock = DockStyle.Fill };
        displays.ReadFailed += management.InvalidateDisplay;
        displayTab.Controls.Add(displays);
        tabs.TabPages.Add(displayTab);
        var liveTab = new TabPage("真实空间地图（只读）");
        liveTab.Controls.Add(livePreview);
        liveTab.Controls.Add(liveStatus);
        var liveActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(6) };
        var fit = new ChromeButton { Text = "适配全部窗口", AutoSize = true };
        fit.Click += (_, _) => livePreview.FitToContent();
        var home = new ChromeButton { Text = "返回原点视角", AutoSize = true };
        home.Click += (_, _) => { liveWorkspace.ReturnHome(); livePreview.Invalidate(); };
        var reconcile = new ChromeButton { Text = "重新检查桌面", AutoSize = true };
        reconcile.Click += (_, _) => RefreshCatalog();
        liveActions.Controls.AddRange([fit, home, reconcile]);
        liveTab.Controls.Add(liveActions);
        tabs.TabPages.Add(liveTab);
        var recoveryTab = new TabPage("恢复日志");
        recovery = new(journalPath) { Dock = DockStyle.Fill };
        recovery.StateChanged += RecoveryStateChanged;
        recovery.RecoveryCompleted += management.ExternalRecoveryCompleted;
        recoveryTab.Controls.Add(recovery);
        tabs.TabPages.Add(recoveryTab);
        var bookmarkTab = new TabPage("位置标签"); bookmarkTab.Controls.Add(bookmarkControl); tabs.TabPages.Add(bookmarkTab);
        management.RecoveryRequested += () => RequestManager(ManagerTarget.Recovery);
        canvasTab.Controls.Add(preview);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(6),
            AutoSize = false
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
            Dock = DockStyle.Bottom,
            Height = 84,
            Padding = new Padding(10),
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
        var catalogActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6) };
        var refresh = new ChromeButton { Text = "检查窗口（只读）", AutoSize = true };
        var inputStatus = new ChromeButton { Text = "输入状态", AutoSize = true };
        inputStatus.Click += (_, _) => management.ShowRuntimeInformation();
        refresh.Click += (_, _) => RefreshCatalog();
        showExcluded.CheckedChanged += (_, _) => RenderSnapshot();
        catalogActions.Controls.AddRange([refresh, inputStatus, showExcluded, catalogSummary]);
        catalogTab.Controls.Add(catalogActions);

        if (!showWithoutActivation)
        {
            tabs.TabPages.Clear();
            var diagnostics = new TabPage("设备诊断");
            var subpages = new ChromeTabs { Dock = DockStyle.Fill };
            catalogTab.Text = "窗口候选"; displayTab.Text = "显示器与坐标"; liveTab.Text = "真实空间地图";
            subpages.TabPages.AddRange([catalogTab, displayTab, liveTab]); diagnostics.Controls.Add(subpages);
            management.WindowScopeControls.Dock = DockStyle.Top; management.WindowScopeControls.Height = 240;
            catalogTab.Controls.Add(management.WindowScopeControls);
            tabs.TabPages.AddRange([managementTab, diagnostics, recoveryTab]); recoveryTab.Text = "恢复中心";
            verificationPages.AddRange([canvasTab, bookmarkTab]);
        }
        var intro = new CanvasHeader();
        var status = new StatusStrip();
        status.Items.Add(cameraStatus);
        var player = new PlayerScreenPanel { Dock = DockStyle.Fill, Padding = new(14) };
        player.Controls.Add(tabs);
        Controls.Add(player);
        Controls.Add(intro);
        Controls.Add(status);
        ChromeTheme.Apply(this);
        if (!showWithoutActivation)
        {
            pageTransitions.Add(new(tabs));
            foreach (var nested in tabs.TabPages.Cast<TabPage>().SelectMany(page => page.Controls.OfType<TabControl>())) pageTransitions.Add(new(nested));
            foreach (var transition in pageTransitions) transition.Finished += ApplyDeferredPalette;
        }
        if (!showWithoutActivation) ConfigurePopup(intro);
        UpdateCameraStatus();
        displays.RefreshDisplays();
        recovery.RefreshJournal();
        if (recovery.RequiresAttention && !showWithoutActivation) tabs.SelectedTab = recoveryTab;
    }

    private static CanvasWindow Sample(string title, double x, double y, double width, double height) =>
        new(Guid.NewGuid(), title, new(new PointD(x, y), new SizeD(width, height)));

    private void AddButton(FlowLayoutPanel parent, string text, Action action)
    {
        var button = new ChromeButton { Text = text, AutoSize = true, Height = 32 };
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
        if (IsDisposed || Disposing) return;
        if (tray is not null && management.CanUsePanelCommands && lastRecoveryStatus != management.SessionStatus)
        {
            lastRecoveryStatus = management.SessionStatus;
            if (management.SessionStatus != BorderlessFlatWindow.Application.Management.CanvasSessionStatus.Running) recovery.RefreshJournal();
        }
        cameraStatus.ToolTipText = sessionNotificationWarning ?? "";
        UpdateTrayState(); ScheduleDesktopChange(); TryScheduleManagerAction(); ScheduleFixedShortcut();
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
        if (!showWithoutActivation)
        {
            InitializeTray(); StartPopupServices();
            // Pay WPF initialization once at startup, before installing the input watchdog.
            try
            {
                if (OverviewRuntimeOptions.UseNative) BorderlessFlatWindow.Presentation.SpatialOverviewView.WarmUp();
                else { BorderlessFlatWindow.Presentation.CompositionSurface.WarmUp(); BorderlessFlatWindow.Presentation.SpatialOverviewScene.WarmUp(); }
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException) { }
            management.InitializeGlobalInput();
        }
        try
        {
            listener = new();
            listener.EventReceived += change =>
            {
                if (desktopLifecycleStopped || IsDisposed || Disposing) return;
                eventBuffer.Add(change);
                management.RecordWindowChange(change);
                if (!eventTimer.Enabled) eventTimer.Start();
            };
            RefreshCatalog();
        }
        catch (Win32Exception exception) { liveStatus.Text = $"事件监听未启动：{exception.Message}；可手动检查。"; }
        if (!showWithoutActivation) StartShortcutCommands();
    }

    private void FlushEvents()
    {
        // WinEvent hints remain queued. COM catalog reads and rebuilding hidden diagnostics
        // must not interrupt moving thumbnails; confirmation still revalidates its target.
        if (management.DesktopTransitionPending || management.IsOverviewTransitioning || pageTransitions.Any(transition => transition.IsActive)) return;
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
            StopDesktopLifecycle();
            StopShortcutCommands();
            foreach (var transition in pageTransitions) transition.Dispose();
            eventTimer.Dispose();
            StopPopupServices();
            listener?.Dispose();
            listener = null;
            if (recovery is not null) { recovery.StateChanged -= RecoveryStateChanged; recovery.RecoveryCompleted -= management.ExternalRecoveryCompleted; }
            pendingManagerTarget = null;
            if (tray is not null) { tray.Visible = false; tray.Dispose(); tray = null; }
            trayMenu?.Dispose(); trayMenu = null;
            trayIcon.Dispose(); brandIcon.Dispose();
            foreach (var page in verificationPages) page.Dispose();
            bookmarks?.Dispose();
            preferences?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (management is not null && IsHandleCreated && !IsDisposed) HandleDesktopMessage(ref message);
    }

    internal void PrepareSampleSmoke() => tabs.SelectedIndex = 1;
    internal void PrepareRuntimeChromeSmoke()
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Verification entry only.");
        var pages = tabs.TabPages.Cast<TabPage>().ToArray();
        tabs.TabPages.Clear();
        var diagnostics = new TabPage("设备诊断");
        var children = new ChromeTabs { Dock = DockStyle.Fill };
        pages[2].Text = "窗口候选"; pages[3].Text = "显示器与坐标"; pages[4].Text = "真实空间地图";
        children.TabPages.AddRange([pages[2], pages[3], pages[4]]); diagnostics.Controls.Add(children);
        tabs.TabPages.AddRange([pages[0], diagnostics, pages[5]]); pages[5].Text = "恢复中心";
        verificationPages.AddRange([pages[1], pages[6]]); ChromeTheme.Apply(diagnostics);
        if (tabs.TabCount != 3 || children.TabCount != 3) throw new InvalidOperationException("Runtime page hierarchy is invalid.");
    }
    internal void PrepareRuntimePageSmoke(int index, int diagnostic = 0)
    {
        tabs.SelectedIndex = index;
        if (index == 1) tabs.TabPages[1].Controls.OfType<TabControl>().Single().SelectedIndex = diagnostic;
    }
    internal void CheckTraySmoke()
    {
        InitializeTray();
        if (tray!.Icon?.Handle == 0 || tray.Icon is null || !tray.Visible) throw new InvalidOperationException("Tray icon resource is missing.");
        using (var icon = tray.Icon.ToBitmap())
        {
            if (icon.Width < 16 || icon.Height < 16) throw new InvalidOperationException("Tray icon is too small.");
            var visible = 0;
            for (var y = 0; y < icon.Height; y++) for (var x = 0; x < icon.Width; x++) if (icon.GetPixel(x, y).A > 128) visible++;
            if (visible < icon.Width * icon.Height / 2) throw new InvalidOperationException("Tray icon artwork is transparent.");
        }
        Hide();
        if (Visible) throw new InvalidOperationException("Manager did not hide.");
        trayOpen!.PerformClick(); System.Windows.Forms.Application.DoEvents();
        if (!Visible || WindowState != FormWindowState.Normal) throw new InvalidOperationException("Tray open action did not restore the manager.");
        if (trayPause!.Enabled || trayOverview!.Enabled || !traySettings!.Enabled || tray!.Text.Length > 63)
            throw new InvalidOperationException("Idle tray command state is invalid.");
        trayRecovery!.PerformClick(); System.Windows.Forms.Application.DoEvents();
        if (!ReferenceEquals(tabs.SelectedTab, recovery.Parent)) throw new InvalidOperationException("Tray recovery action did not select the recovery page.");
        CheckTrayCommands();
        tray!.Visible = false; tray.Dispose(); tray = null; trayMenu!.Dispose(); trayMenu = null;
        lastTrayPresentation = null;
    }

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
    internal void CheckRecoveryErrorSmoke() { tabs.SelectedIndex = 5; recovery.CheckCorruptJournalSmoke(); }
    internal void PrepareBookmarkSmoke()
    {
        bookmarks.Add("工作区", new(0, 0)); bookmarks.Add("工作区", new(-4800, 2700));
        bookmarks.Add("资料", new(9600, -3600)); bookmarkControl.RefreshSmoke(); tabs.SelectedIndex = 6;
    }
    internal void PrepareBookmarkSearchSmoke() => bookmarkControl.SetSmokeQuery("资料");
    internal void CheckBookmarkCommandsSmoke() => bookmarkControl.CheckSmokeCommands();
    internal void ReloadBookmarksSmoke() { bookmarks.Reload(); bookmarkControl.RefreshSmoke(); }
    internal void ResetBookmarksSmoke()
    { if (!bookmarks.BackupAndReset()) throw new InvalidOperationException(bookmarks.Message); bookmarkControl.RefreshSmoke(); }

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
                ])
                { Tag = entry.Id, ForeColor = entry.Admission.IsAdmitted ? SystemColors.WindowText : SystemColors.GrayText };
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
        if (OwnedForms.Any(form => form.Modal && !form.IsDisposed)) return base.ProcessCmdKey(ref msg, keyData);
        if (management.HasOverview) return management.ProcessOverviewKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        if (tabs.SelectedIndex == 0 && management.ProcessNavigationKey(keyData)) return true;
        if (!showWithoutActivation || tabs.SelectedIndex != 1) return base.ProcessCmdKey(ref msg, keyData);
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

    internal void PresentOverview(SpatialOverviewForm overview, Action completed) => PresentOverview(new FormOverviewPresenter(overview), completed);
    internal void PresentOverview(IOverviewPresenter overview, Action completed)
    {
        overview.ShowInTaskbar = reviewPanel;
        var wasVisible = Visible;
        var restored = false;
        void Restore()
        {
            if (restored) return; restored = true;
            try
            {
                if (!IsDisposed && !Disposing && (showWithoutActivation || reviewPanel))
                {
                    if (wasVisible) Show(); else Hide();
                }
            }
            finally { completed(); }
        }
        overview.Closed += (_, _) => Restore();
        try
        {
            if (!overview.WantsPresentation) { Restore(); return; }
            // An independent, fully composed top-level host avoids stretching/recreating the popup HWND.
            var hiding = System.Diagnostics.Stopwatch.GetTimestamp();
            double? flushMs = null;
            if (!showWithoutActivation)
            {
                Hide();
                // Snapshot presenters need the hidden manager committed before
                // reading pixels. A native compositor surface has no such readback.
                if (wasVisible && overview.RequiresDesktopSnapshot)
                {
                    var flushing = System.Diagnostics.Stopwatch.GetTimestamp(); FrostedBackdrop.FlushPresentation();
                    flushMs = System.Diagnostics.Stopwatch.GetElapsedTime(flushing).TotalMilliseconds;
                }
            }
            var hideMs = System.Diagnostics.Stopwatch.GetElapsedTime(hiding).TotalMilliseconds;
            overview.PreparePresentation();
            // A COM/capture call can dispatch a queued Tab. An already cancelled
            // overview must not create a visible host just to close it in Shown.
            if (!overview.WantsPresentation) { Restore(); return; }
            var showing = System.Diagnostics.Stopwatch.GetTimestamp();
            overview.Show(!showWithoutActivation);
            overview.SetHostTimings(hideMs, System.Diagnostics.Stopwatch.GetElapsedTime(showing).TotalMilliseconds, flushMs);
        }
        catch { Restore(); throw; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && (recovery.IsRunning || OwnedForms.Any(form => form.Visible && form.Modal)))
        { e.Cancel = true; return; }
        if (e.CloseReason == CloseReason.UserClosing && management.HasOverview)
        { e.Cancel = true; management.CancelOverview(); return; }
        if (!showWithoutActivation && !exitRequested && e.CloseReason == CloseReason.UserClosing && tray is not null)
        { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!showWithoutActivation && WindowState == FormWindowState.Minimized && tray is not null && management?.HasOverview != true) Hide();
    }
}
