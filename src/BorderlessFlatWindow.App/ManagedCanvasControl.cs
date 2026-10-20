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
    private readonly WrapLabel status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(18, 8, 18, 0) };
    private readonly ChromeButton start = new() { Text = "开始画布", AutoSize = true };
    private readonly ChromeButton pause = new() { Text = "暂停并恢复", AutoSize = true, Enabled = false };
    private readonly Panel selector = new() { Dock = DockStyle.Left, Width = 272, Padding = new(14) };
    private readonly ContextMenuStrip moreMenu = new();
    private readonly ToolTip hints = new();
    private readonly GameCommandDeck commandDeck;
    private readonly ToolStripMenuItem wallpaperColors = new("跟随壁纸取色") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem overviewAnimation = new("总览缩放动画") { Checked = true, CheckOnClick = true };
    private Guid? lastFocusedWindow;
    internal event Action<bool>? WallpaperColorsChanged;
    internal event Action? WallpaperRefreshRequested;
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
    private bool overviewRequested, overviewScheduled, openingOverview, openingCloseRequested;
    private long overviewInputSession;
    private long overviewRequestTimestamp, overviewReceivedTimestamp;
    internal bool HasOverview => overview is not null;
    internal bool IsOverviewTransitioning => overview?.IsTransitioning == true;
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
        selector.Dock = DockStyle.Bottom; selector.Height = 186; selector.Padding = new(12, 2, 12, 2);
        selector.Controls.Add(new Label { Text = "选择窗口", Dock = DockStyle.Top, Height = 30, Padding = new(0, 3, 0, 0) });
        var admission = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(0, 8, 0, 0) };
        InitializeAdmissionControls(admission); selector.Controls.Add(admission);
        var overviewButton = new ChromeButton { Text = "总览", AccessibleName = "空间总览" };
        overviewButton.Click += (_, _) => RequestOverview();
        var more = new ChromeButton { Text = "⋯", AccessibleName = "更多操作", Size = new(54, 46) };
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, 0), ToolStripDropDownDirection.AboveLeft);
        commandDeck = new(start, pause, overviewButton, more,
            delta => Act(() => { if (session?.IsRunning == true) session.Navigate(session.Workspace.Camera.Position + delta, DateTimeOffset.UtcNow); }),
            () => Act(() => session?.Home(DateTimeOffset.UtcNow)), () => Act(() => session?.Back(DateTimeOffset.UtcNow)));
        foreach (var button in commandDeck.NavigationButtons) hints.SetToolTip(button, button.AccessibleName);
        hints.SetToolTip(overviewButton, "空间总览 · Tab / Ctrl+Alt+O");
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
        var appearance = new ToolStripMenuItem("外观");
        appearance.DropDownItems.Add(wallpaperColors);
        wallpaperColors.CheckedChanged += (_, _) => WallpaperColorsChanged?.Invoke(wallpaperColors.Checked);
        appearance.DropDownItems.Add("重新采样壁纸", null, (_, _) => { if (!wallpaperColors.Checked) wallpaperColors.Checked = true; else WallpaperRefreshRequested?.Invoke(); });
        appearance.DropDownItems.Add(overviewAnimation); ChromeTheme.StyleMenu(appearance.DropDown); moreMenu.Items.Add(appearance);
        moreMenu.Items.Add("操作提示", null, (_, _) => MessageBox.Show(this,
            "桌面空白处：滚轮移动，Shift 横移，左右双键拖动。\nTab 进入/退出总览，Ctrl+滚轮轮选窗口。\n总览内方向键选择，Ctrl+Tab 轮选，Enter 定位。\n应用窗口内直接操作；Esc 可暂停自动导航。\n关闭管理器收入托盘，托盘菜单可退出并恢复。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information));
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
        Controls.Add(map); Controls.Add(selector); Controls.Add(status); Controls.Add(commandDeck);
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
        // The camera is suspended in overview. Keep the input watchdog alive, and defer
        // catalog/UI reconciliation until the short compositor transition has finished.
        if (IsOverviewTransitioning)
        {
            globalInput?.Heartbeat();
            foreach (var queued in globalInput?.Drain() ?? [])
            {
                if (queued.Input.Kind == NavigationInputKind.Overview) RequestOverview(queued.CapturedTimestamp);
                else if (queued.Input.Kind == NavigationInputKind.Fault) { Pause("导航输入异常，已暂停恢复。"); break; }
            }
            return;
        }
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
            if (item.Input.Kind == NavigationInputKind.Overview) RequestOverview(item.CapturedTimestamp);
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
    private void RequestOverview(long capturedTimestamp = 0)
    {
        if (overview is not null) { overview.ToggleOverview(capturedTimestamp); return; }
        // COM probes can pump messages before the form exists. Retain every toggle
        // without scheduling a second overview or dropping navigation suspension.
        if (openingOverview) { openingCloseRequested = !openingCloseRequested; return; }
        if (overviewRequested) { overviewRequested = false; globalInput?.EndOverview(overviewInputSession); return; }
        if (session?.IsRunning != true) { notice = "请先开始画布管理，再打开空间总览。"; RefreshState(); return; }
        notice = null;
        overviewRequestTimestamp = capturedTimestamp > 0 ? capturedTimestamp : System.Diagnostics.Stopwatch.GetTimestamp();
        overviewReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        gestures?.End(DateTimeOffset.UtcNow); overviewRequested = true;
        overviewInputSession = globalInput?.BeginOverview() ?? 0; ScheduleOverview();
    }
    internal void ShowOverview() => RequestOverview();
    internal void RefreshMenus()
    {
        ChromeTheme.StyleMenu(moreMenu);
        foreach (ToolStripMenuItem item in moreMenu.Items.OfType<ToolStripMenuItem>()) if (item.HasDropDownItems) ChromeTheme.StyleMenu(item.DropDown);
    }
    private IEnumerable<SpatialOverviewForm.PreviewSource> OverviewPreviewSources()
    {
        if (lastSnapshot is null || layout is null) return [];
        var managedIds = OverviewWindows().Select(window => window.Id).ToHashSet();
        return lastSnapshot.Entries.Where(entry => entry.Id is { } id && managedIds.Contains(id) && entry.Observation.Identity is not null)
            .Select(entry => new SpatialOverviewForm.PreviewSource(entry.Id!.Value, entry.Observation.Identity!.Value,
                entry.Observation.Bounds is { } bounds && layout.Monitors.Any(m => bounds.Position.X >= m.WorkArea.Left && bounds.Position.Y >= m.WorkArea.Top
                && bounds.Right <= m.WorkArea.Right && bounds.Bottom <= m.WorkArea.Bottom), entry.Observation.Bounds)).ToArray();
    }
    private void ScheduleOverview()
    {
        if (!overviewRequested || overviewScheduled || openingOverview || reading || session?.IsBusy == true || session?.HasPending == true || IsDisposed) return;
        if (session?.IsRunning != true) { overviewRequested = false; globalInput?.EndOverview(overviewInputSession); return; }
        overviewScheduled = true;
        BeginInvoke(() => { overviewScheduled = false; OpenOverview(); });
    }
    private void OpenOverview()
    {
        if (!overviewRequested || openingOverview || reading || session?.IsRunning != true || session.IsBusy || session.HasPending || layout is null) return;
        overviewRequested = false; openingOverview = true; openingCloseRequested = false;
        var openStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var inputSession = overviewInputSession;
        hints.Active = false; moreMenu.Close();
        var managed = session; managed.SuspendNavigation(true);
        ForegroundWindowBookmark? focus = null;
        var handedOff = false;
        try
        {
            var reconcileStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            focus = ForegroundWindowBookmark.Capture(WindowEventListener.ForegroundWindow);
            var observed = RefreshManagedSnapshot(managed);
            managed.Observe(observed, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            if (!managed.IsRunning) { notice = managed.Message; return; }
            if (openingCloseRequested) return;
            var area = layout.Primary.WorkArea;
            if (FindForm() is not MainForm host) throw new InvalidOperationException("总览主窗口不可用。");
            var reconcileMs = System.Diagnostics.Stopwatch.GetElapsedTime(reconcileStarted).TotalMilliseconds;
            var constructionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var dialog = new SpatialOverviewForm(OverviewWindows(), managed.Workspace.Camera,
                new(area.Left, area.Top, area.Width, area.Height), managed.ActiveWindowId ?? lastFocusedWindow, bookmarks: bookmarks,
                viewports: layout.Monitors.Select(m => new RectD(managed.Workspace.Camera.Position + m.WorkArea.Position - layout.CanvasWorkArea.Position, m.WorkArea.Size)),
                animate: overviewAnimation.Checked);
            dialog.SetOpenTimings(overviewRequestTimestamp, reconcileMs, System.Diagnostics.Stopwatch.GetElapsedTime(constructionStarted).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(overviewRequestTimestamp, overviewReceivedTimestamp).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(overviewReceivedTimestamp, openStarted).TotalMilliseconds);
            overview = dialog; var retainedFocus = focus;
            if (openingCloseRequested) dialog.ToggleOverview();
            dialog.Shown += (_, _) => globalInput?.SetOverviewHost(inputSession, dialog.Handle);
            dialog.Activated += (_, _) => globalInput?.SetOverviewHost(inputSession, dialog.Handle, activated: true);
            dialog.SetPreviewSources(OverviewPreviewSources());
            // Presentation can finish synchronously (cancelled before Shown). End
            // this preparation before completion/focus probes can reenter a new open.
            openingOverview = false; openingCloseRequested = false;
            host.PresentOverview(dialog, () =>
            {
                handedOff = true; focus = null;
                FinishOverview(dialog, managed, retainedFocus, inputSession);
            });
            handedOff = true; focus = null;
        }
        catch (Exception exception) when (Expected(exception)) { notice = $"总览未完成：{exception.Message}"; focus?.TryRestore(); }
        finally
        {
            if (!handedOff)
            {
                openingOverview = false; openingCloseRequested = false;
                hints.Active = true;
                overview?.Dispose(); overview = null; focus?.Dispose(); managed.SuspendNavigation(false);
                if (!IsDisposed && !Disposing) { globalInput?.EndOverview(inputSession); RefreshState(); }
            }
        }
    }
    private void FinishOverview(SpatialOverviewForm dialog, ManagedCanvasSession managed, ForegroundWindowBookmark? focus, long inputSession)
    {
        if (!IsDisposed && !Disposing) hints.Active = true;
        if (ReferenceEquals(overview, dialog)) overview = null;
        // Release this host before focus/COM work can pump a new desktop Tab request.
        // The final cleanup is generation-scoped and cannot clear that newer request.
        if (!IsDisposed && !Disposing) globalInput?.EndOverview(inputSession);
        (FindForm() as MainForm)?.ApplyDeferredPalette();
        managed.SuspendNavigation(false);
        try
        {
            if (IsDisposed || Disposing) return;
            if (dialog.ConfirmedCameraPosition is { } position && ReferenceEquals(session, managed) && managed.IsRunning)
            { if (!managed.Navigate(position, DateTimeOffset.UtcNow)) notice = managed.Message; }
            else if (dialog.ConfirmedTarget is { } selected && ReferenceEquals(session, managed) && managed.IsRunning)
            {
                RefreshManagedSnapshot(managed);
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
            if (!IsDisposed && !Disposing) { globalInput?.EndOverview(inputSession); RefreshState(); }
        }
    }

    private WindowCatalogSnapshot RefreshManagedSnapshot(ManagedCanvasSession managed)
    {
        // WinEvent maintains the catalog. Revalidate our sources without enumerating the unrelated desktop on each transition.
        var ids = managed.Workspace.Windows.Select(window => window.Id).ToHashSet();
        var handles = catalog.Current.Entries.Where(entry => entry.Id is { } id && ids.Contains(id))
            .Select(entry => entry.Observation.Handle).Distinct().ToArray();
        foreach (var handle in handles) catalog.RefreshWindow(handle);
        return lastSnapshot = catalog.Current;
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
        if (session?.ActiveWindowId is { } focused) lastFocusedWindow = focused;
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
        var button = new ChromeButton { Text = text, AutoSize = true };
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
        commandDeck.SetRunning(running && !reading);
        selector.Visible = !running || showWindowPicker;
        if (running && session!.HasPending) verify.Start(); else verify.Stop();
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; preview.Invalidate(); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        var attention = notice ?? (session?.Status is CanvasSessionStatus.RecoveryRequired or CanvasSessionStatus.Paused ? session.Message : null);
        status.Text = attention ?? (running ? "桌面空白处移动视角 · 应用窗口内直接操作" : "勾选需要的窗口，开始画布");
        status.FitText(attention is null ? 42 : 0);
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
