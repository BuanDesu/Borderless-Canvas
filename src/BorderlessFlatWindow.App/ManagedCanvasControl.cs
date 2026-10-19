using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Recovery;
using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl : UserControl
{
    private readonly ITrackedDesktopWindowCatalog catalog;
    private readonly string journalPath;
    private readonly BookmarkLibrary bookmarks;
    private readonly CheckedListBox candidates = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly Panel map = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(18, 8, 18, 0) };
    private readonly Button start = new() { Text = "开始画布", AutoSize = true };
    private readonly Button pause = new() { Text = "暂停并恢复", AutoSize = true, Enabled = false };
    private readonly Panel selector = new() { Dock = DockStyle.Left, Width = 272, Padding = new(14) };
    private readonly ContextMenuStrip moreMenu = new();
    private readonly ToolTip hints = new();
    private bool showWindowPicker;
    private readonly System.Windows.Forms.Timer verify = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer navigationTimer = new() { Interval = 16 };
    private readonly Label navigationStatus = new() { AutoSize = true, Padding = new(4) };
    private GlobalNavigationInput? globalInput;
    private NavigationGestureSession? gestures;
    private NavigationHud? hud;
    private GlobalNavigationState? navigationState;
    private bool? inputAvailable;
    private NavigationInputOptions inputOptions = new();
    private SpatialOverviewForm? overview;
    private WindowCatalogSnapshot? lastSnapshot;
    private bool overviewRequested, overviewScheduled, openingOverview;
    internal bool HasOverview => overview is not null;
    internal void CancelOverview() => overview?.CancelOverview();
    internal bool ProcessOverviewKey(Keys keys) => overview?.HandleCommand(keys) == true;
    private readonly Label overviewShortcutStatus = new() { AutoSize = true, Padding = new(4) };
    private ManagedCanvasSession? session;
    private DisplayLayout? layout;
    private CanvasPreviewControl? preview;
    private bool reading;
    private string? notice;
    private sealed record Candidate(Guid Id, string Label, bool Supported, bool Managed)
    { public override string ToString() => Label; }
    internal event Action? StatusChanged;
    internal event Action? RecoveryRequested;
    internal PointD BookmarkPosition => session?.Workspace.Camera.Position ?? PointD.Zero;
    internal bool CanJumpToBookmark => session?.IsRunning == true && !reading && !session.IsBusy && overview is null;

    internal ManagedCanvasControl(ITrackedDesktopWindowCatalog catalog, string? journalPath, BookmarkLibrary bookmarks)
    {
        this.bookmarks = bookmarks;
        this.catalog = catalog; this.journalPath = journalPath ?? JsonRecoveryJournalStore.DefaultPath;
        selector.Controls.Add(candidates);
        selector.Controls.Add(new Label { Text = "选择窗口", Dock = DockStyle.Top, Height = 44, Padding = new(0, 10, 0, 0) });
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(14, 10, 14, 8), WrapContents = true };
        toolbar.Controls.AddRange([start, pause]);
        var admission = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(0, 8, 0, 0) };
        InitializeAdmissionControls(admission); selector.Controls.Add(admission);
        Add(toolbar, "空间总览", RequestOverview);
        var more = new Button { Text = "•••", AccessibleName = "更多操作", Size = new(54, 46) };
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, more.Height)); toolbar.Controls.Add(more);
        var navigation = new ToolStripMenuItem("视角");
        navigation.DropDownItems.Add("返回原点    Home", null, (_, _) => Act(() => session?.Home(DateTimeOffset.UtcNow)));
        navigation.DropDownItems.Add("上一视角    Backspace", null, (_, _) => Act(() => session?.Back(DateTimeOffset.UtcNow)));
        navigation.DropDownItems.Add("适配地图", null, (_, _) => preview?.FitToContent());
        navigation.DropDownItems.Add("手动导航", null, (_, _) => globalInput?.Toggle());
        ChromeTheme.StyleMenu(navigation.DropDown);
        moreMenu.Items.Add(navigation);
        moreMenu.Items.Add("管理窗口", null, (_, _) => { showWindowPicker = !selector.Visible; RefreshState(); });
        moreMenu.Items.Add("刷新窗口", null, (_, _) => RefreshCandidates());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("导航设置…", null, (_, _) =>
        {
            globalInput?.Exit();
            using var dialog = new NavigationSettingsDialog(inputOptions, hotkey: globalInput?.State.Hotkey);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            gestures?.End(DateTimeOffset.UtcNow); inputOptions = dialog.Options;
            if (gestures is not null) gestures.Options = inputOptions;
            globalInput?.Configure(inputOptions);
            if (globalInput is not null && dialog.Hotkey != globalInput.State.Hotkey) globalInput.ChangeHotkey(dialog.Hotkey);
        });
        moreMenu.Items.Add("恢复中心", null, (_, _) => RecoveryRequested?.Invoke());
        moreMenu.Items.Add("操作提示", null, (_, _) => MessageBox.Show(this,
            "桌面空白处：滚轮移动，Shift 横移，左右双键拖动。\nTab 打开总览，Ctrl+滚轮轮选窗口。\n应用窗口内直接操作；Esc 可暂停自动导航。\n关闭管理器收入托盘，托盘菜单可退出并恢复。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information));
        moreMenu.Items.Add("运行信息", null, (_, _) => MessageBox.Show(this,
            $"{navigationStatus.Text}\n{overviewShortcutStatus.Text}\n{session?.Message}\n相机 {session?.Workspace.Camera.Position}", "运行信息", MessageBoxButtons.OK, MessageBoxIcon.Information));
        ChromeTheme.StyleMenu(moreMenu); ChromeTheme.Accent(start, ChromeTheme.Purple);
        hints.SetToolTip(start, "接管勾选的普通窗口，记录原位以便恢复");
        hints.SetToolTip(pause, "暂停画布并恢复全部接管窗口的原始位置");
        hints.SetToolTip(more, "窗口、视角、设置与恢复");
        start.Click += (_, _) => Begin();
        pause.Click += (_, _) => Pause();
        candidates.ItemCheck += OnCandidateItemCheck;
        verify.Tick += (_, _) => VerifyPending();
        navigationTimer.Tick += (_, _) => NavigationFrame();
        Controls.Add(map); Controls.Add(selector); Controls.Add(status); Controls.Add(toolbar);
    }

    internal void InitializeGlobalInput()
    {
        if (globalInput is not null) return;
        try
        {
            globalInput = new(() =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() => navigationTimer.Start());
            });
            NavigationFrame();
        }
        catch (Exception exception) when (Expected(exception)) { navigationStatus.Text = $"导航不可用：{exception.Message}"; }
    }

    private void NavigationFrame()
    {
        if (IsDisposed) return;
        if (reading || session?.IsBusy == true) { globalInput?.Heartbeat(); return; }
        var events = globalInput?.Drain() ?? [];
        navigationState = globalInput?.State;
        overviewShortcutStatus.Text = globalInput?.OverviewMessage ?? "Ctrl+Alt+O 打开空间总览；快捷键不可用时使用按钮。";
        if (navigationState is not null) navigationStatus.Text = navigationState.Registered ? navigationState.Message
            : $"快捷键 {navigationState.Hotkey.Label} 未注册：{navigationState.Message}";
        if (events.Any(item => item.Input.Kind == NavigationInputKind.Fault))
        {
            gestures?.Cancel(); Pause("导航输入异常或响应超时，已暂停恢复。"); return;
        }
        foreach (var item in events)
        {
            if (item.Input.Kind == NavigationInputKind.Overview) RequestOverview();
            else if (overview is not null || openingOverview) continue;
            else if (item.Input.Kind is NavigationInputKind.Home or NavigationInputKind.Back)
            {
                gestures?.Accept(new(NavigationInputKind.Exit, PointD.Zero), item.At);
                if (item.Input.Kind == NavigationInputKind.Home) session?.Home(item.At); else session?.Back(item.At);
            }
            else if (item.Input.Kind == NavigationInputKind.CycleWindow)
            {
                gestures?.End(item.At);
                var steps = Math.Clamp(Math.Abs(item.Input.WheelDelta) / 120, 1, 12);
                for (var step = 0; step < steps; step++) session?.CycleWindow(item.Input.WheelDelta < 0 ? 1 : -1, item.At);
            }
            else gestures?.Accept(item.Input, item.At);
        }
        gestures?.Tick(DateTimeOffset.UtcNow);
        if (navigationState?.Active == true && session?.IsRunning == true)
        {
            navigationTimer.Start();
            if (navigationState.Manual)
            { hud ??= new(); hud.Present(navigationState.Hotkey.Label, session.Workspace.Camera.Position, inputOptions); }
            else hud?.Hide();
        }
        else
        {
            hud?.Hide();
            if (gestures?.HasGesture == true) navigationTimer.Start(); else navigationTimer.Stop();
        }
        RefreshState();
    }

    private IEnumerable<CanvasWindow> OverviewWindows()
    {
        var admitted = lastSnapshot?.Entries.Where(entry => entry.Admission.IsAdmitted && entry.Id is not null)
            .Select(entry => entry.Id!.Value).ToHashSet() ?? [];
        return session?.Workspace.Windows.Where(window => admitted.Contains(window.Id)).ToArray() ?? [];
    }
    private void RequestOverview()
    {
        if (overview is not null) { overview.CancelOverview(); return; }
        if (session?.IsRunning != true) { notice = "请先开始画布管理，再打开空间总览。"; RefreshState(); return; }
        notice = null;
        gestures?.End(DateTimeOffset.UtcNow); overviewRequested = true;
        globalInput?.SetOverviewOpen(true); ScheduleOverview();
    }
    internal void ShowOverview() => RequestOverview();
    private IEnumerable<SpatialOverviewForm.PreviewSource> OverviewPreviewSources()
    {
        if (lastSnapshot is null || layout is null) return [];
        var managedIds = OverviewWindows().Select(window => window.Id).ToHashSet();
        return lastSnapshot.Entries.Where(entry => entry.Id is { } id && managedIds.Contains(id) && entry.Observation.Identity is not null)
            .Select(entry => new SpatialOverviewForm.PreviewSource(entry.Id!.Value, entry.Observation.Identity!.Value,
                entry.Observation.Bounds is { } bounds && layout.Monitors.Any(m => bounds.Position.X >= m.WorkArea.Left && bounds.Position.Y >= m.WorkArea.Top
                && bounds.Right <= m.WorkArea.Right && bounds.Bottom <= m.WorkArea.Bottom))).ToArray();
    }
    private void ScheduleOverview()
    {
        if (!overviewRequested || overviewScheduled || openingOverview || reading || session?.IsBusy == true || session?.HasPending == true || IsDisposed) return;
        if (session?.IsRunning != true) { overviewRequested = false; globalInput?.SetOverviewOpen(false); return; }
        overviewScheduled = true;
        BeginInvoke(() => { overviewScheduled = false; OpenOverview(); });
    }
    private void OpenOverview()
    {
        if (!overviewRequested || openingOverview || reading || session?.IsRunning != true || session.IsBusy || session.HasPending || layout is null) return;
        overviewRequested = false; openingOverview = true;
        var managed = session; managed.SuspendNavigation(true);
        ForegroundWindowBookmark? focus = null;
        var handedOff = false;
        try
        {
            focus = ForegroundWindowBookmark.Capture(WindowEventListener.ForegroundWindow);
            lastSnapshot = catalog.Inspect();
            managed.Observe(lastSnapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            if (!managed.IsRunning) { notice = managed.Message; return; }
            var area = layout.Primary.WorkArea;
            if (FindForm() is not MainForm host) throw new InvalidOperationException("总览主窗口不可用。");
            var dialog = new SpatialOverviewForm(OverviewWindows(), managed.Workspace.Camera,
                new(area.Left, area.Top, area.Width, area.Height), managed.ActiveWindowId, bookmarks: bookmarks,
                viewports: layout.Monitors.Select(m => new RectD(managed.Workspace.Camera.Position + m.WorkArea.Position - layout.CanvasWorkArea.Position, m.WorkArea.Size)));
            overview = dialog; var retainedFocus = focus;
            dialog.SetPreviewSources(OverviewPreviewSources());
            host.PresentOverview(dialog, () => FinishOverview(dialog, managed, retainedFocus));
            handedOff = true; focus = null;
        }
        catch (Exception exception) when (Expected(exception)) { notice = $"总览未完成：{exception.Message}"; focus?.TryRestore(); }
        finally
        {
            openingOverview = false;
            if (!handedOff)
            {
                overview?.Dispose(); overview = null; focus?.Dispose(); managed.SuspendNavigation(false);
                if (!IsDisposed && !Disposing) { globalInput?.SetOverviewOpen(false); RefreshState(); }
            }
        }
    }
    private void FinishOverview(SpatialOverviewForm dialog, ManagedCanvasSession managed, ForegroundWindowBookmark? focus)
    {
        if (ReferenceEquals(overview, dialog)) overview = null;
        managed.SuspendNavigation(false);
        try
        {
            if (IsDisposed || Disposing) return;
            if (dialog.ConfirmedCameraPosition is { } position && ReferenceEquals(session, managed) && managed.IsRunning)
            { if (!managed.Navigate(position, DateTimeOffset.UtcNow)) notice = managed.Message; }
            else if (dialog.ConfirmedTarget is { } selected && ReferenceEquals(session, managed) && managed.IsRunning)
            {
                lastSnapshot = catalog.Inspect();
                if (!OverviewWindows().Any(window => window.Id == selected.Id))
                { notice = "目标窗口已关闭或改变，未执行跳转。"; focus?.TryRestore(); }
                else if (!managed.Focus(selected.Id, DateTimeOffset.UtcNow)) notice = managed.Message;
            }
            else
            {
                var restored = focus?.TryRestore() == true;
                notice = managed.IsRunning ? restored ? "已取消总览，视角未改变，已返回原窗口。" : "已取消总览，视角未改变。" : managed.Message;
            }
        }
        catch (Exception exception) when (Expected(exception)) { notice = $"总览未完成：{exception.Message}"; }
        finally
        {
            focus?.Dispose(); dialog.Dispose();
            if (!IsDisposed && !Disposing) { globalInput?.SetOverviewOpen(false); RefreshState(); }
        }
    }

    internal void SetDisplay(DisplayLayout current)
    {
        if (session is not null && session.MatchesDisplay(current)) return;
        overviewRequested = false; overview?.CancelOverview();
        if (session?.IsRunning == true) Pause("显示配置已变化，已暂停恢复；请重新选择窗口开始。");
        session?.Dispose(); layout = current;
        admissionTimer.Stop();
        session = new(() => new RecoverySession(new JsonRecoveryJournalStore(journalPath), new WindowsRecoveryWindowPort()),
            new NativeWindowMotionPort(), current, bookmarks.Collection);
        gestures = new(() => session.Workspace.Camera.Position,
            (position, now) => session.Navigate(position, now, recordHistory: false), session.Workspace.RecordNavigationOrigin);
        gestures.Options = inputOptions;
        if (preview is not null) { map.Controls.Remove(preview); preview.Dispose(); }
        preview = new(session.Workspace) { Dock = DockStyle.Fill, AutoFit = false };
        preview.WindowChosen += id => Act(() => session.Focus(id, DateTimeOffset.UtcNow));
        preview.CameraPanRequested += delta =>
        {
            if (!session.IsRunning || reading || session.IsBusy) return;
            gestures.Accept(new(delta.X != 0 ? NavigationInputKind.HorizontalWheel : NavigationInputKind.Wheel,
                PointD.Zero, checked((int)(delta.X != 0 ? delta.X : -delta.Y))), DateTimeOffset.UtcNow);
            navigationTimer.Start();
        };
        ChromeTheme.Apply(preview); map.Controls.Add(preview); RefreshState();
    }

    internal void Observe(WindowCatalogSnapshot snapshot, nint foreground)
    {
        if (reading || IsDisposed) return;
        lastSnapshot = snapshot;
        DrainAdmissionHints(snapshot);
        session?.Observe(snapshot, foreground, DateTimeOffset.UtcNow);
        RefreshState();
    }

    private void Begin()
    {
        if (session is null || reading) return;
        notice = null;
        var selected = candidates.CheckedItems.OfType<Candidate>().Where(item => item.Supported).Select(item => item.Id).ToHashSet();
        try
        {
            reading = true;
            var snapshot = catalog.Inspect();
            lastSnapshot = snapshot;
            var entries = snapshot.Entries.Where(entry => entry.Id is { } id && selected.Contains(id)).ToArray();
            if (entries.Length != selected.Count) { notice = "窗口已经改变，请刷新并重新选择。"; return; }
            session.Start(entries, DateTimeOffset.UtcNow);
            if (session.IsRunning) ResetAutomaticAdmission(snapshot);
            preview?.FitToContent();
        }
        catch (Exception exception) when (Expected(exception)) { notice = $"接管未完成：{exception.Message}"; }
        finally { reading = false; RefreshState(); }
    }

    private void VerifyPending()
    {
        if (reading || session?.IsBusy == true) return;
        if (session?.IsRunning != true || !session.HasPending) { verify.Stop(); return; }
        try
        {
            reading = true;
            foreach (var handle in session.PendingHandles) catalog.RefreshWindow(handle);
            session.Observe(catalog.Current, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (Expected(exception)) { session.Pause($"位置确认失败：{exception.Message}"); }
        finally { reading = false; RefreshState(); }
    }

    private void RefreshCandidates()
    {
        if (reading) return;
        try { Observe(catalog.Inspect(), WindowEventListener.ForegroundWindow); }
        catch (Exception exception) when (Expected(exception)) { notice = exception.Message; RefreshState(); }
    }
    private void Act(Action action)
    {
        if (reading || session?.IsBusy == true) return;
        try { notice = null; gestures?.End(DateTimeOffset.UtcNow); action(); }
        catch (Exception exception) when (Expected(exception)) { session?.Pause($"导航失败：{exception.Message}"); }
        RefreshState();
    }
    private void Add(FlowLayoutPanel toolbar, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => Act(action); toolbar.Controls.Add(button);
    }
    internal void Pause(string reason = "已暂停并恢复原始位置；再次开始可恢复画布布局。")
    { notice = null; overviewRequested = false; overview?.CancelOverview(); gestures?.Cancel(); verify.Stop(); session?.Pause(reason); RefreshState(); }
    private void RefreshState()
    {
        var running = session?.IsRunning == true;
        if (lastSnapshot is not null) RenderCandidates(lastSnapshot);
        UpdateAdmissionState();
        if (globalInput is not null && inputAvailable != running)
        { inputAvailable = running; globalInput.SetAvailable(running); }
        if (!running) { navigationTimer.Stop(); hud?.Hide(); gestures?.Cancel(); }
        overview?.UpdateWindows(OverviewWindows(), running, session?.Message ?? "");
        overview?.SetPreviewSources(OverviewPreviewSources());
        candidates.Enabled = !reading && overview is null; start.Enabled = !running;
        start.Visible = !running; pause.Visible = running; pause.Enabled = running;
        selector.Visible = !running || showWindowPicker;
        if (running && session!.HasPending) verify.Start(); else verify.Stop();
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; preview.Invalidate(); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        var attention = notice ?? (session?.Status is CanvasSessionStatus.RecoveryRequired or CanvasSessionStatus.Paused ? session.Message : null);
        status.Text = attention ?? (running ? "桌面空白处移动视角 · 应用窗口内直接操作" : "勾选需要的窗口，开始画布");
        status.Height = attention is null ? 42 : 76;
        hints.SetToolTip(status, $"{state} · 离屏 {session?.ParkedCount ?? 0} · 相机 ({camera?.Position.X:0}, {camera?.Position.Y:0})\n{session?.Message}");
        StatusChanged?.Invoke();
        ScheduleOverview();
    }
    internal (bool Running, int Count, PointD Camera) Diagnostics() =>
        (session?.IsRunning == true, session?.Workspace.Windows.Count ?? 0, session?.Workspace.Camera.Position ?? PointD.Zero);
    internal void EndNavigationForEditing()
    { gestures?.End(DateTimeOffset.UtcNow); globalInput?.Exit(); hud?.Hide(); }
    internal string GoToBookmark(Guid id)
    {
        if (!CanJumpToBookmark) return "请先在窗口画布页开始管理，再跳转位置标签。";
        var bookmark = bookmarks.Collection.Items.FirstOrDefault(entry => entry.Id == id);
        if (bookmark is null) return "标签已不存在。";
        var accepted = false;
        Act(() => accepted = session!.GoToBookmark(id, DateTimeOffset.UtcNow));
        return accepted ? $"已请求移至“{bookmark.Name}” ({bookmark.CameraPosition.X:G12}, {bookmark.CameraPosition.Y:G12})；窗口布局保持。" : notice ?? session!.Message;
    }
    internal bool ProcessNavigationKey(Keys key) => key switch
    {
        Keys.Home => Key(() => session?.Home(DateTimeOffset.UtcNow)),
        Keys.Back => Key(() => session?.Back(DateTimeOffset.UtcNow)),
        _ => false
    };
    private bool Key(Action action) { if (session?.IsRunning != true) return false; Act(action); return true; }
    private static bool Expected(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
        or Win32Exception or ArgumentException or OverflowException;
    protected override void Dispose(bool disposing)
    {
        if (disposing) { overviewRequested = false; overview?.CancelOverview(); moreMenu.Dispose(); hints.Dispose(); admissionTimer.Dispose(); navigationTimer.Dispose(); globalInput?.Dispose(); hud?.Dispose(); gestures?.Cancel(); verify.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
}
