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
using BorderlessFlatWindow.Application.Preferences;

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
    private readonly ToolStripMenuItem appearanceMenu = new("外观");
    private readonly ToolStripMenuItem localSettingsMenu = new("本地设置");
    private readonly ToolTip hints = new();
    private readonly GameCommandDeck commandDeck;
    private readonly ToolStripMenuItem wallpaperColors = new("跟随壁纸取色") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem overviewAnimation = new("镜头与总览动画") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem shortcutContext = new("软件快捷方式右键入口") { Checked = true, CheckOnClick = true };
    internal event Action<bool>? ShortcutMenuChanged;
    private Guid? lastFocusedWindow;
    internal event Action<bool>? WallpaperColorsChanged;
    internal event Action? WallpaperRefreshRequested;
    private readonly System.Windows.Forms.Timer verify = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer navigationTimer = new() { Interval = 16 };
    private readonly Label navigationStatus = new() { AutoSize = true, Padding = new(4) };
    private GlobalNavigationInput? globalInput;
    private NavigationGestureSession? gestures;
    private NavigationHud? hud;
    private CrosshairOverlay? crosshairOverlay;
    private PixelRect[] configuredPointerScreens = [];
    private GlobalNavigationState? navigationState;
    private bool? inputAvailable;
    private NavigationInputOptions inputOptions = new();
    private IOverviewPresenter? overview;
    private WindowCatalogSnapshot? lastSnapshot;
    private bool overviewRequested, overviewScheduled, openingOverview, openingCloseRequested;
    private long overviewInputSession;
    private long overviewRequestTimestamp, overviewReceivedTimestamp;
    internal bool HasOverview => overview is not null;
    internal bool IsOverviewTransitioning => overview?.IsTransitioning == true;
    internal bool HasOverviewWork => overview is not null || overviewRequested || openingOverview || preparingOverviewHost;
    internal bool CanUsePanelCommands => !reading && session?.IsBusy != true && !editingArrangement && !editingSettings && !desktopTransitionPending
        && (FindForm() is not MainForm host || host.AllowsCanvasCommands);
    // Reads and native transactions can pump UI messages. Queue input through these
    // short operations; only an explicit interaction/safety boundary revokes it.
    private bool GlobalInputBlocked => editingArrangement || editingSettings || desktopTransitionPending || panelActionPending || externalRecoveryRunning
        || layout is null || FindForm() is MainForm { AllowsCanvasCommands: false };
    private bool CanCaptureGlobalInput => session?.IsRunning == true && !GlobalInputBlocked;
    internal bool CanApplyDesktopTransition => !reading && session?.IsBusy != true && !editingArrangement && !editingSettings && !HasOverviewWork && !externalRecoveryRunning;
    internal bool DesktopTransitionPending => desktopTransitionPending;
    internal bool CanStartForLifecycleCheck => start.Enabled;
    private bool desktopTransitionPending;
    internal event Action? DisplayRecheckRequested;
    private string? displayFailure;
    private string? environmentWarning;
    internal CanvasSessionStatus SessionStatus => session?.Status ?? CanvasSessionStatus.Idle;
    private bool panelActionPending, externalRecoveryRunning;
    internal void CancelOverview()
    {
        InvalidateOverviewPreparation(); overviewRequested = false;
        if (openingOverview) openingCloseRequested = true;
        if (overview is not null) overview.CancelOverview();
        else if (!openingOverview) globalInput?.EndOverview(overviewInputSession);
    }
    internal void SetPanelActionPending(bool pending)
    { panelActionPending = pending; if (pending) { EndNavigationForEditing(); CancelOverview(); } RefreshState(); }
    internal void SetDesktopTransitionPending(bool pending)
    {
        if (desktopTransitionPending == pending) return;
        desktopTransitionPending = pending;
        if (pending) { EndNavigationForEditing(); gestures?.Cancel(); verify.Stop(); CancelOverview(); }
        RefreshState();
    }
    internal void InvalidateDisplay(string reason)
    {
        layout = null; displayFailure = reason; Pause(reason);
        if (session?.IsRunning == true) DisplayRecheckRequested?.Invoke();
    }
    internal void ReportEnvironmentWarning(string warning) { environmentWarning = warning; RefreshState(); }
    internal void ClearEnvironmentWarning(string warning) { if (environmentWarning == warning) environmentWarning = null; RefreshState(); }
    internal void HostInteractionChanged()
    {
        if (IsDisposed || Disposing) return;
        if (FindForm() is MainForm { AllowsCanvasCommands: false }) { EndNavigationForEditing(); session?.StopCameraTransition(); }
        RefreshState();
    }
    internal void SetExternalRecoveryRunning(bool running)
    { if (externalRecoveryRunning == running) return; externalRecoveryRunning = running; RefreshState(); }
    internal void ExternalRecoveryCompleted(RecoveryReport report)
    { session?.RecheckAfterExternalRecovery(report); RefreshState(); }
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
    internal bool CanJumpToBookmark => session?.IsRunning == true && !reading && !session.IsBusy && overview is null && !desktopTransitionPending && layout is not null;

    internal ManagedCanvasControl(ITrackedDesktopWindowCatalog catalog, string? journalPath, BookmarkLibrary bookmarks, PreferencesSession preferences)
    {
        this.preferences = preferences;
        this.bookmarks = bookmarks;
        this.catalog = catalog; this.journalPath = journalPath ?? JsonRecoveryJournalStore.DefaultPath;
        selector.Controls.Add(candidates);
        selector.Dock = DockStyle.Bottom; selector.Height = 186; selector.Padding = new(12, 2, 12, 2);
        selector.Controls.Add(new Label { Text = "窗口与便签", Dock = DockStyle.Top, Height = 30, Padding = new(0, 3, 0, 0) });
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
        navigation.DropDownItems.Add("准星模式    Ctrl+Alt+C", null, (_, _) => globalInput?.ToggleCrosshair(afterMenu: true));
        ChromeTheme.StyleMenu(navigation.DropDown);
        moreMenu.Items.Add(navigation);
        InitializeArrangementMenu();
        moreMenu.Items.Add("固定当前窗口    Ctrl+Alt+P", null, (_, _) => ToggleFocusedPin());
        moreMenu.Items.Add("刷新窗口", null, (_, _) => RefreshCandidates());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("导航设置…", null, (_, _) => ShowNavigationSettings());
        moreMenu.Items.Add("恢复中心", null, (_, _) => RecoveryRequested?.Invoke());
        var appearance = appearanceMenu;
        appearance.DropDownItems.Add(wallpaperColors);
        wallpaperColors.CheckedChanged += (_, _) => WallpaperColorsChanged?.Invoke(wallpaperColors.Checked);
        appearance.DropDownItems.Add("重新采样壁纸", null, (_, _) => { if (!wallpaperColors.Checked) wallpaperColors.Checked = true; else WallpaperRefreshRequested?.Invoke(); });
        appearance.DropDownItems.Add(overviewAnimation); ChromeTheme.StyleMenu(appearance.DropDown); moreMenu.Items.Add(appearance);
        InitializePreferences();
        moreMenu.Items.Add(shortcutContext);
        moreMenu.Items.Add("操作提示", null, (_, _) => MessageBox.Show(this,
            "开始后接管所有可用窗口，便签保持独立。\n桌面滚轮、Shift 横移、双键拖动；屏幕外沿继续向外推动可平移。\nCtrl+滚轮平滑轮选；Tab 进入/退出总览。\nCtrl+Alt+C 准星模式，移动鼠标直接移动镜头；Esc 或再次按组合键退出。\nCtrl+Alt+P 固定当前窗口；标题栏右键也可切换便签。\n软件 .lnk 快捷方式右键可“以画布便签模式打开”。\n关闭管理器收入托盘，显式退出恢复画布窗口。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information));
        moreMenu.Items.Add("运行信息", null, (_, _) => MessageBox.Show(this,
            $"{navigationStatus.Text}\n输入接管 {(globalInput?.State.Available == true ? "已启用" : "已停用")} · 模式 {globalInput?.State.Mode}\n{globalInput?.HookHealth}\n最近退出：{globalInput?.LastExitReason}\n{overviewShortcutStatus.Text}\n{globalInput?.PointerMessage}\n{session?.Message}\n相机 {session?.Workspace.Camera.Position}", "运行信息", MessageBoxButtons.OK, MessageBoxIcon.Information));
        ChromeTheme.StyleMenu(moreMenu); ChromeTheme.Accent(start, ChromeTheme.Purple);
        hints.SetToolTip(start, "接管所有可用普通窗口，便签保持独立；接管前记录原位以便恢复");
        hints.SetToolTip(pause, "暂停画布并恢复全部接管窗口的原始位置");
        hints.SetToolTip(more, "窗口、视角、设置与恢复");
        start.Click += (_, _) => Begin();
        pause.Click += (_, _) => Pause();
        candidates.ItemCheck += OnCandidateItemCheck;
        verify.Tick += (_, _) => VerifyPending();
        navigationTimer.Tick += (_, _) => NavigationFrame();
        preparationTimer.Tick += (_, _) => PrepareOverviewWhileIdle();
        Controls.Add(map); Controls.Add(selector); Controls.Add(status); Controls.Add(commandDeck);
        InitializePinControls();
        InitializeFixedLaunchTimer();
    }

    internal void InitializeGlobalInput()
    {
        if (globalInput is not null) return;
        try
        {
            globalInput = new(() =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() => navigationTimer.Start());
            }, inputOptions, PreferredHotkey);
            ConfigurePointerScreens();
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
        if (events.Count != 0) lastCanvasInput = System.Diagnostics.Stopwatch.GetTimestamp();
        navigationState = globalInput?.State;
        if (navigationState is { Crosshair: true, CrosshairCenter: { } center }) { crosshairOverlay ??= new(); crosshairOverlay.Present(center); }
        else crosshairOverlay?.Hide();
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
                for (var step = 0; step < steps; step++) session?.CycleWindow(item.Input.WheelDelta < 0 ? 1 : -1, item.At, animate: overviewAnimation.Checked && System.Windows.SystemParameters.ClientAreaAnimation);
            }
            else if (item.Input.Kind == NavigationInputKind.TogglePin) RequestPinToggle(item.Input.TargetWindow, item.Input.TargetIdentity, item.Input.TargetId);
            else gestures?.Accept(item.Input, item.At);
        }
        session?.AdvanceCameraTransition(DateTimeOffset.UtcNow);
        gestures?.Tick(DateTimeOffset.UtcNow);
        if (navigationState?.Active == true && session?.IsRunning == true)
        {
            navigationTimer.Start();
            if (navigationState.Manual && !navigationState.Crosshair)
            { hud ??= new(); hud.Present(navigationState.Hotkey.Label, session.Workspace.Camera.Position, inputOptions); }
            else hud?.Hide();
        }
        else
        {
            hud?.Hide();
            if (gestures?.HasGesture == true || session?.HasCameraTransition == true) navigationTimer.Start(); else navigationTimer.Stop();
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
        if (editingArrangement || editingSettings || panelActionPending || externalRecoveryRunning || desktopTransitionPending || layout is null) return;
        if (preparationDisposed) return;
        preparationTimer.Stop();
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
        InvalidateOverviewPreparation();
        ChromeTheme.StyleMenu(moreMenu);
        foreach (ToolStripMenuItem item in moreMenu.Items.OfType<ToolStripMenuItem>()) if (item.HasDropDownItems) ChromeTheme.StyleMenu(item.DropDown);
        ScheduleOverviewPreparation();
    }
    private IEnumerable<OverviewPreviewSource> OverviewPreviewSources()
    {
        if (lastSnapshot is null || layout is null) return [];
        var managedIds = OverviewWindows().Select(window => window.Id).ToHashSet();
        return lastSnapshot.Entries.Where(entry => entry.Id is { } id && managedIds.Contains(id) && entry.Observation.Identity is not null)
            .Select(entry => new OverviewPreviewSource(entry.Id!.Value, entry.Observation.Identity!.Value,
                entry.Observation.Bounds is { } bounds && layout.Monitors.Any(m => bounds.Position.X >= m.WorkArea.Left && bounds.Position.Y >= m.WorkArea.Top
                && bounds.Right <= m.WorkArea.Right && bounds.Bottom <= m.WorkArea.Bottom), entry.Observation.Bounds)).ToArray();
    }
    private void ScheduleOverview()
    {
        if (preparationDisposed || !overviewRequested || overviewScheduled || openingOverview || preparingOverviewHost || reading || session?.IsBusy == true || session?.HasPending == true || IsDisposed) return;
        if (session?.IsRunning != true) { overviewRequested = false; globalInput?.EndOverview(overviewInputSession); return; }
        overviewScheduled = true;
        BeginInvoke(() => { overviewScheduled = false; OpenOverview(); });
    }
    private void OpenOverview()
    {
        if (preparationDisposed || !overviewRequested || openingOverview || preparingOverviewHost || reading || session?.IsRunning != true || session.IsBusy || session.HasPending || layout is null) return;
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
            if (FindForm() is not MainForm host) throw new InvalidOperationException("总览主窗口不可用。");
            var reconcileMs = System.Diagnostics.Stopwatch.GetElapsedTime(reconcileStarted).TotalMilliseconds;
            var constructionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var preparation = PreparationSnapshot(managed, host);
            var dialog = preparedOverview.Take(preparation) ?? CreateOverview(preparation);
            overview = dialog;
            dialog.SetInitialSelection(managed.ActiveWindowId ?? lastFocusedWindow);
            dialog.SetOpenTimings(overviewRequestTimestamp, reconcileMs, System.Diagnostics.Stopwatch.GetElapsedTime(constructionStarted).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(overviewRequestTimestamp, overviewReceivedTimestamp).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(overviewReceivedTimestamp, openStarted).TotalMilliseconds);
            var retainedFocus = focus;
            if (openingCloseRequested) dialog.ToggleOverview();
            dialog.Shown += (_, _) => globalInput?.SetOverviewHost(inputSession, dialog.Handle);
            dialog.Activated += (_, _) => globalInput?.SetOverviewHost(inputSession, dialog.Handle);
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
        catch (Exception exception) when (Expected(exception)) { notice = $"总览未完成：{exception.Message}"; if (!desktopTransitionPending) focus?.TryRestore(); }
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
    private void FinishOverview(IOverviewPresenter dialog, ManagedCanvasSession managed, ForegroundWindowBookmark? focus, long inputSession)
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
            if (!desktopTransitionPending && dialog.ConfirmedCameraPosition is { } position && ReferenceEquals(session, managed) && managed.IsRunning)
            { if (!managed.Navigate(position, DateTimeOffset.UtcNow)) notice = managed.Message; }
            else if (!desktopTransitionPending && dialog.ConfirmedTarget is { } selected && ReferenceEquals(session, managed) && managed.IsRunning)
            {
                RefreshManagedSnapshot(managed);
                if (!OverviewWindows().Any(window => window.Id == selected.Id))
                { notice = "目标窗口已关闭或改变，未执行跳转。"; focus?.TryRestore(); }
                else if (!managed.Focus(selected.Id, DateTimeOffset.UtcNow)) notice = managed.Message;
            }
            else
            {
                var restored = !desktopTransitionPending && focus?.TryRestore() == true;
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
        if (reading || session?.IsBusy == true || editingArrangement || editingSettings || HasOverviewWork)
        { DisplayRecheckRequested?.Invoke(); return; }
        displayFailure = null;
        if (session is not null && session.MatchesDisplay(current)) { layout = current; RefreshState(); return; }
        InvalidateOverviewPreparation();
        overviewRequested = false; overview?.CancelOverview();
        if (session?.IsRunning == true) Pause("显示配置已变化，已暂停恢复；请重新选择窗口开始。");
        if (session is not null && session.ReconfigureDisplay(current))
        { layout = current; preview?.Invalidate(); RefreshState(); return; }
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
        if (reading || IsDisposed || desktopTransitionPending || layout is null) return;
        lastSnapshot = snapshot;
        UpdateFixedLaunches(snapshot);
        PruneFixedWindows(snapshot);
        DrainAdmissionHints(snapshot);
        session?.Observe(snapshot, foreground, DateTimeOffset.UtcNow);
        if (session?.ActiveWindowId is { } focused) lastFocusedWindow = focused;
        RefreshState();
    }

    private void Begin()
    {
        if (session is null || layout is null || !CanUsePanelCommands || externalRecoveryRunning) return;
        notice = null;
        try
        {
            reading = true;
            var snapshot = catalog.Inspect();
            lastSnapshot = snapshot;
            UpdateFixedLaunches(snapshot);
            PruneFixedWindows(snapshot);
            var exclusions = AutomaticExclusions;
            var entries = snapshot.Entries.Where(entry => ManagedCanvasSession.Unsupported(entry, layout) is null
                && entry.Observation.Identity is { } identity && !exclusions.Contains(identity)).ToArray();
            session.Start(entries, DateTimeOffset.UtcNow, skipUnavailable: true);
            if (session.IsRunning) ResetAutomaticAdmission(snapshot);
            preview?.FitToContent();
        }
        catch (Exception exception) when (Expected(exception)) { notice = $"接管未完成：{exception.Message}"; }
        finally { reading = false; RefreshState(); }
    }

    private void VerifyPending()
    {
        if (reading || desktopTransitionPending || session?.IsBusy == true) return;
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
        lastCanvasInput = System.Diagnostics.Stopwatch.GetTimestamp();
        if (reading || desktopTransitionPending || session?.IsBusy == true || layout is null) return;
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
    { InvalidateOverviewPreparation(); notice = null; overviewRequested = false; overview?.CancelOverview(); gestures?.Cancel(); verify.Stop(); session?.Pause(reason); RefreshState(); }
    private void RefreshState()
    {
        ConfigurePointerScreens();
        RefreshPinMenuTargets();
        var running = session?.IsRunning == true;
        if (lastSnapshot is not null) RenderCandidates(lastSnapshot);
        UpdateAdmissionState();
        var available = CanCaptureGlobalInput;
        if (globalInput is not null && (inputAvailable != available || globalInput.State.Available != available))
        { inputAvailable = available; globalInput.SetAvailable(available); }
        if (!running) { navigationTimer.Stop(); hud?.Hide(); gestures?.Cancel(); }
        overview?.UpdateWindows(OverviewWindows(), running, session?.Message ?? "");
        overview?.SetPreviewSources(OverviewPreviewSources());
        candidates.Enabled = !reading && overview is null && !externalRecoveryRunning && !desktopTransitionPending; start.Enabled = !running && !externalRecoveryRunning && !desktopTransitionPending && layout is not null;
        start.Visible = !running; pause.Visible = running; pause.Enabled = running;
        commandDeck.SetRunning(running && !reading && !desktopTransitionPending && layout is not null);
        selector.Visible = selector.Parent != this;
        if (running && session!.HasPending && !desktopTransitionPending) verify.Start(); else verify.Stop();
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; preview.Invalidate(); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        var attention = displayFailure ?? notice ?? (session?.Status is CanvasSessionStatus.RecoveryRequired or CanvasSessionStatus.Paused ? session.Message : null) ?? environmentWarning ?? preferences.Warning;
        status.Text = attention ?? (running ? $"画布 {session?.ManagedIds.Count ?? 0} · 便签 {fixedWindows.Count} · 桌面空白处移动视角" : "开始后接管所有可用窗口 · 便签保持独立");
        status.FitText(attention is null ? 42 : 0);
        hints.SetToolTip(status, $"{state} · 离屏 {session?.ParkedCount ?? 0} · 相机 ({camera?.Position.X:0}, {camera?.Position.Y:0})\n{session?.Message}");
        StatusChanged?.Invoke();
        SchedulePinToggle();
        ScheduleOverview();
        ScheduleOverviewPreparation();
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
        if (disposing) { DisposePinControls(); DisposeOverviewPreparation(); overviewRequested = false; overview?.CancelOverview(); moreMenu.Dispose(); hints.Dispose(); admissionTimer.Dispose(); navigationTimer.Dispose(); globalInput?.Dispose(); crosshairOverlay?.Dispose(); hud?.Dispose(); gestures?.Cancel(); verify.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
    private void ConfigurePointerScreens()
    {
        if (globalInput is null) return;
        var screens = layout?.Monitors.Select(m => m.Bounds).ToArray() ?? [];
        if (configuredPointerScreens.SequenceEqual(screens)) return;
        configuredPointerScreens = screens; globalInput.ConfigureScreens(screens);
    }
}
