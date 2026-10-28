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
    private readonly System.Windows.Forms.Timer inputHealthTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer feedbackTimer = new() { Interval = 900 };
    private readonly System.Windows.Forms.Timer launchTimer = new() { Interval = 100 };
    private readonly CanvasLaunchCoordinator launch = new();
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
    private CanvasInteractionState InteractionState => new(reading, session?.IsBusy == true, editingArrangement || editingSettings,
        desktopTransitionPending, FindForm() is MainForm { AllowsCanvasCommands: false }, panelActionPending,
        externalRecoveryRunning, recoveryAttention, layout is not null);
    internal bool CanUsePanelCommands => InteractionState.CanUsePanelCommands && !WorkspaceRestoring && !workspaceStarting && !workspaceOperation;
    // Reads and native transactions can pump UI messages. Queue input through these
    // short operations; only an explicit interaction/safety boundary revokes it.
    private bool GlobalInputBlocked => InteractionState.GlobalInputBlocked || WorkspaceRestoring || workspaceStarting || workspaceOperation;
    private bool CanCaptureGlobalInput => InteractionState.CanCaptureInput(session?.IsRunning == true) && !WorkspaceRestoring && !workspaceStarting && !workspaceOperation;
    internal bool CanApplyDesktopTransition => InteractionState.CanApplyDesktopTransition(HasOverviewWork);
    internal bool DesktopTransitionPending => desktopTransitionPending;
    internal bool CanStartForLifecycleCheck => start.Enabled && !recoveryAttention;
    private bool desktopTransitionPending;
    internal event Action? DisplayRecheckRequested;
    private string? displayFailure;
    private string? environmentWarning;
    internal CanvasSessionStatus SessionStatus => session?.Status ?? CanvasSessionStatus.Idle;
    private bool panelActionPending, externalRecoveryRunning, recoveryAttention;
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
        if (pending) { CancelWorkspaceRestore(); EndNavigationForEditing(); gestures?.Cancel(); verify.Stop(); CancelOverview(); }
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
    { if (externalRecoveryRunning == running) return; if (running) CancelWorkspaceRestore(); externalRecoveryRunning = running; RefreshState(); }
    internal void SetRecoveryAttention(bool attention)
    {
        attention &= session?.IsRunning != true;
        if (recoveryAttention == attention) return;
        recoveryAttention = attention;
        hints.SetToolTip(start, attention ? "打开恢复中心，先处理旧会话或日志问题；不会开始接管" : "接管所有可用普通窗口，便签保持独立；接管前记录原位以便恢复");
        RefreshState();
    }
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
        overviewKeyButton = overviewButton;
        var navigation = new ToolStripMenuItem("视角");
        navigation.DropDownItems.Add(KeyMenu("返回原点", () => preferredBindings.Home, (_, _) => Act(() => session?.Home(DateTimeOffset.UtcNow))));
        navigation.DropDownItems.Add(KeyMenu("上一视角", () => preferredBindings.Back, (_, _) => Act(() => session?.Back(DateTimeOffset.UtcNow))));
        navigation.DropDownItems.Add("适配地图", null, (_, _) => preview?.FitToContent());
        navigation.DropDownItems.Add(KeyMenu("触控导航 · 两指滚动", () => preferredBindings.Touch, (_, _) => ToggleTouchNavigation()));
        navigation.DropDownItems.Add(KeyMenu("准星模式", () => preferredBindings.Crosshair, (_, _) => globalInput?.ToggleCrosshair(afterMenu: true)));
        ChromeTheme.StyleMenu(navigation.DropDown);
        moreMenu.Items.Add(navigation);
        InitializeArrangementMenu();
        moreMenu.Items.Add(KeyMenu("固定当前窗口", () => preferredBindings.Pin, (_, _) => ToggleFocusedPin()));
        moreMenu.Items.Add("刷新窗口", null, (_, _) => RefreshCandidates());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("导航与键位设置…", null, (_, _) => ShowNavigationSettings());
        moreMenu.Items.Add("恢复中心", null, (_, _) => RecoveryRequested?.Invoke());
        var appearance = appearanceMenu;
        appearance.DropDownItems.Add(wallpaperColors);
        wallpaperColors.CheckedChanged += (_, _) => WallpaperColorsChanged?.Invoke(wallpaperColors.Checked);
        appearance.DropDownItems.Add("重新采样壁纸", null, (_, _) => { if (!wallpaperColors.Checked) wallpaperColors.Checked = true; else WallpaperRefreshRequested?.Invoke(); });
        appearance.DropDownItems.Add(overviewAnimation); ChromeTheme.StyleMenu(appearance.DropDown); moreMenu.Items.Add(appearance);
        InitializePreferences();
        moreMenu.Items.Add(shortcutContext);
        moreMenu.Items.Add("操作提示", null, (_, _) => MessageBox.Show(this,
            $"桌面滚轮、Shift 横移、双键拖动；屏幕外沿向外推动可平移。\nCtrl+滚轮平滑轮选；Tab 进入/退出总览。\n{GlobalNavigationInput.Describe(preferredBindings.Crosshair)} 准星，{GlobalNavigationInput.Describe(preferredBindings.Touch)} 触控；Esc 退出。\n触控模式两指滚动移动画布，单击及按住拖动交给应用。\n{GlobalNavigationInput.Describe(preferredBindings.Pin)} 固定当前窗口；标题栏右键也可切换便签。\n快捷键可在“导航与键位设置”修改。\n关闭管理器收入托盘，显式退出恢复画布窗口。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information));
        moreMenu.Items.Add("输入状态…", null, (_, _) => ShowRuntimeInformation());
        ChromeTheme.StyleMenu(moreMenu); ChromeTheme.Accent(start, ChromeTheme.Purple);
        hints.SetToolTip(start, "接管所有可用普通窗口，便签保持独立；接管前记录原位以便恢复");
        hints.SetToolTip(pause, "暂停画布并恢复全部接管窗口的原始位置");
        hints.SetToolTip(more, "窗口、视角、设置与恢复");
        start.Click += (_, _) => { if (recoveryAttention) RecoveryRequested?.Invoke(); else Begin(); };
        pause.Click += (_, _) => Pause();
        candidates.ItemCheck += OnCandidateItemCheck;
        verify.Tick += (_, _) => VerifyPending();
        navigationTimer.Tick += (_, _) => NavigationFrame();
        inputHealthTimer.Tick += (_, _) => MaintainInputHealth();
        feedbackTimer.Tick += (_, _) => UpdateNavigationFeedback();
        launchTimer.Tick += (_, _) => ContinueLaunchStart(globalInput is not null);
        preparationTimer.Tick += (_, _) => PrepareOverviewWhileIdle();
        Controls.Add(map); Controls.Add(selector); Controls.Add(status); Controls.Add(commandDeck);
        InitializePinControls();
        InitializeFixedLaunchTimer();
        InitializeWorkspaceRestore();
    }

    internal void InitializeGlobalInput()
    {
        if (globalInput is not null) return;
        try
        {
            globalInput = new(CreateInputWake(), inputOptions, keyBindings: preferredBindings);
            inputHealthTimer.Start();
            ConfigurePointerScreens();
            NavigationFrame();
        }
        catch (Exception exception) when (Expected(exception)) { navigationStatus.Text = $"导航不可用：{exception.Message}"; }
    }
    private long deliveredInputWakes;
    private long lastNavigationFeedback;
    private Action CreateInputWake()
    {
        // The manager can start on the recovery page: this child may have no HWND.
        // Its top-level owner already exists in OnShown and survives tab changes.
        var owner = (Control?)FindForm() ?? this;
        return () => owner.BeginInvoke(() =>
        {
            if (IsDisposed || Disposing) return;
            globalInput?.AcknowledgeUiWake();
            deliveredInputWakes++; navigationTimer.Start();
        });
    }

    private void NavigationFrame()
    {
        if (IsDisposed) return;
        if (workspaceStarting || workspaceOperation || WorkspaceRestoring)
        {
            globalInput?.Heartbeat();
            var fault = globalInput?.Drain().Any(e => e.Input.Kind == NavigationInputKind.Fault) == true;
            touchInputs.Clear(); gestures?.Cancel(); navigationTimer.Stop();
            if (fault) Pause("工作区复原期间输入异常，已暂停恢复。");
            return;
        }
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
        if (touchInputOverflow) { CloseTouchNavigation(discard: true, refresh: false); Pause("触控输入积压，已暂停并恢复窗口。"); return; }
        var runtimeBefore = NavigationRuntimeState();
        IReadOnlyList<TimedNavigationInput> events = globalInput?.Drain() ?? [];
        if (touchInputs.Count > 0) events = events.Concat(touchInputs).ToArray();
        touchInputs.Clear();
        if (events.Count != 0) lastCanvasInput = System.Diagnostics.Stopwatch.GetTimestamp();
        navigationState = globalInput?.State;
        if (touchNavigation is null && navigationState is { Crosshair: true, CrosshairCenter: { } center }) { crosshairOverlay ??= new(); crosshairOverlay.Present(center); }
        else crosshairOverlay?.Hide();
        overviewShortcutStatus.Text = globalInput?.OverviewMessage ?? "Ctrl+Alt+O 打开空间总览；快捷键不可用时使用按钮。";
        if (navigationState is not null) navigationStatus.Text = navigationState.Registered ? navigationState.Message
            : $"快捷键 {navigationState.Hotkey.Label} 未注册：{navigationState.Message}";
        if (events.Any(item => item.Input.Kind == NavigationInputKind.Fault))
        {
            gestures?.Cancel(); Pause("导航输入异常或响应超时，已暂停恢复。"); return;
        }
        var previousCamera = session?.Workspace.Camera.Position;
        foreach (var item in events)
        {
            if (item.Input.Kind == NavigationInputKind.ToggleTouch) ToggleTouchNavigation();
            else if (item.Input.Kind == NavigationInputKind.Exit && touchNavigation is not null && navigationState?.Touch != true) CloseTouchNavigation();
            else if (item.Input.Kind == NavigationInputKind.Overview) RequestOverview(item.CapturedTimestamp);
            else if (overview is not null || openingOverview) continue;
            else if (item.Input.Kind is NavigationInputKind.Home or NavigationInputKind.Back)
            {
                gestures?.Accept(new(NavigationInputKind.Exit, PointD.Zero), item.At);
                if (item.Input.Kind == NavigationInputKind.Home) session?.Home(item.At); else session?.Back(item.At);
            }
            else if (item.Input.Kind == NavigationInputKind.CycleWindow)
            {
                gestures?.End(item.At);
                var steps = Math.Clamp(Math.Abs(item.Input.WheelDelta) / 120, 0, 12);
                for (var step = 0; step < steps; step++) session?.CycleWindow(item.Input.WheelDelta < 0 ? 1 : -1, item.At, animate: overviewAnimation.Checked && System.Windows.SystemParameters.ClientAreaAnimation);
            }
            else if (item.Input.Kind == NavigationInputKind.TogglePin) RequestPinToggle(item.Input.TargetWindow, item.Input.TargetIdentity, item.Input.TargetId);
            else gestures?.Accept(item.Input, item.At);
        }
        session?.AdvanceCameraTransition(DateTimeOffset.UtcNow);
        gestures?.Tick(DateTimeOffset.UtcNow);
        var cameraChanged = previousCamera != session?.Workspace.Camera.Position;
        if (cameraChanged) lastNavigationFeedback = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateNavigationFeedback();
        if (gestures?.NeedsFrame == true || session?.HasCameraTransition == true) navigationTimer.Start();
        else navigationTimer.Stop();
        // Admission, candidate construction, pin targets and overview source lists
        // follow observations/commands. A camera-only frame does not change them.
        if (runtimeBefore != NavigationRuntimeState()) RefreshState();
        else
        {
            if (cameraChanged) { RefreshNavigationPresentation(); StatusChanged?.Invoke(); }
            SchedulePinToggle(); ScheduleOverview(); ScheduleOverviewPreparation();
        }
    }

    private (CanvasSessionStatus? Status, string? Message, bool Pending, int Count, Guid? Active, bool Overview) NavigationRuntimeState()
        => (session?.Status, session?.Message, session?.HasPending == true, session?.ManagedCount ?? 0, session?.ActiveWindowId, HasOverviewWork);

    private void MaintainInputHealth()
    {
        if (IsDisposed || Disposing) return;
        // Independent of the 16ms pump: well below the worker's 1800ms watchdog.
        // Do not acknowledge a notification here; only Drain consumes it. The
        // worker retains its own retry and touch-receiver maintenance cadence.
        globalInput?.Heartbeat();
        if (globalInput?.HasPendingUiNotification == true && !navigationTimer.Enabled) NavigationFrame();
    }

    private void UpdateNavigationFeedback()
    {
        var feedback = session?.IsRunning == true && !HasOverviewWork && touchNavigation is null && navigationState?.Crosshair != true
            && lastNavigationFeedback != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(lastNavigationFeedback).TotalMilliseconds < 900;
        feedbackTimer.Stop();
        if (feedback)
        {
            hud ??= new(); if (!hud.Visible) hud.Present(inputOptions, preferredBindings);
            feedbackTimer.Interval = Math.Max(1, (int)Math.Ceiling(900 - System.Diagnostics.Stopwatch.GetElapsedTime(lastNavigationFeedback).TotalMilliseconds));
            feedbackTimer.Start();
        }
        else hud?.Hide();
    }

    private IEnumerable<CanvasWindow> OverviewWindows()
    {
        var admitted = lastSnapshot?.Entries.Where(entry => entry.Admission.IsAdmitted && entry.Id is not null)
            .Select(entry => entry.Id!.Value).ToHashSet() ?? [];
        return session?.Workspace.Windows.Where(window => admitted.Contains(window.Id)).ToArray() ?? [];
    }
    private void RequestOverview(long capturedTimestamp = 0)
    {
        CancelWorkspaceRestore();
        CloseTouchNavigation();
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
            if (!desktopTransitionPending && dialog.ConfirmedWorkspaceId is { } workspace && ReferenceEquals(session, managed) && managed.IsRunning)
            { OpenWorkspace(workspace); }
            else if (!desktopTransitionPending && dialog.ConfirmedCameraPosition is { } position && ReferenceEquals(session, managed) && managed.IsRunning)
            {
                if (!managed.NavigateWorkspace(position, DateTimeOffset.UtcNow, WindowEventListener.ForegroundWindow,
                    animate: overviewAnimation.Checked && System.Windows.SystemParameters.ClientAreaAnimation)) notice = managed.Message;
                navigationTimer.Start();
            }
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
        if (session is null || layout is null || !CanUsePanelCommands || externalRecoveryRunning || recoveryAttention) return;
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
    internal void StartOnLaunch()
    {
        if (FindForm() is MainForm { IsTransparentFixture: true }) return;
        QueueLaunchStart();
    }
    private void QueueLaunchStart()
    {
        launch.Request();
        // OnShown can still be inside native/WPF initialization. Start on the UI
        // loop, retaining the request across short reads and disabled-owner work.
        launchTimer.Start();
    }
    private void ContinueLaunchStart(bool inputReady)
    {
        if (IsDisposed || Disposing)
        { CancelLaunchStart(); return; }
        var decision = launch.Advance(session?.Status ?? CanvasSessionStatus.Idle, InteractionState, inputReady, session is not null, displayFailure is not null);
        if (decision is LaunchStartDecision.None or LaunchStartDecision.Wait) return;
        CancelLaunchStart();
        if (decision == LaunchStartDecision.Cancel) return;
        if (decision == LaunchStartDecision.InputUnavailable)
        { CancelLaunchStart(); notice = "输入接管未就绪，未自动开始画布。"; RefreshState(); return; }
        // An empty selection is an active canvas awaiting future windows.
        Begin();
    }
    private void CancelLaunchStart() { launch.Cancel(); launchTimer.Stop(); }

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
    { CancelWorkspaceRestore(); CancelLaunchStart(); CloseTouchNavigation(discard: true, refresh: false); InvalidateOverviewPreparation(); notice = null; overviewRequested = false; overview?.CancelOverview(); gestures?.Cancel(); verify.Stop(); session?.Pause(reason); RefreshState(); }
    private void RefreshState()
    {
        ConfigurePointerScreens();
        RefreshPinMenuTargets();
        var running = session?.IsRunning == true;
        if (!running || desktopTransitionPending || externalRecoveryRunning || FindForm() is MainForm { AllowsCanvasCommands: false })
            CloseTouchNavigation(discard: true, refresh: false);
        if (lastSnapshot is not null) RenderCandidates(lastSnapshot);
        UpdateAdmissionState();
        var available = CanCaptureGlobalInput;
        if (globalInput is not null && (inputAvailable != available || globalInput.State.Available != available))
        { inputAvailable = available; globalInput.SetAvailable(available); }
        if (!running) { navigationTimer.Stop(); feedbackTimer.Stop(); hud?.Hide(); gestures?.Cancel(); }
        overview?.UpdateWindows(OverviewWindows(), running, session?.Message ?? "");
        overview?.SetPreviewSources(OverviewPreviewSources());
        candidates.Enabled = !reading && overview is null && !externalRecoveryRunning && !desktopTransitionPending;
        start.Enabled = !running && !externalRecoveryRunning && !desktopTransitionPending && (layout is not null || recoveryAttention);
        start.Text = recoveryAttention ? "恢复" : "开始";
        start.AccessibleName = recoveryAttention ? "恢复窗口" : "开始画布";
        start.Visible = !running; pause.Visible = running; pause.Enabled = running;
        commandDeck.SetRunning(running && !reading && !desktopTransitionPending && layout is not null);
        selector.Visible = selector.Parent != this;
        if (running && session!.HasPending && !desktopTransitionPending) verify.Start(); else verify.Stop();
        RefreshNavigationPresentation();
        StatusChanged?.Invoke();
        SchedulePinToggle();
        ScheduleOverview();
        ScheduleOverviewPreparation();
    }
    private void RefreshNavigationPresentation()
    {
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; if (preview.Visible) preview.Invalidate(); }
        var running = session?.IsRunning == true;
        var attention = recoveryAttention ? "待恢复 · 点击“恢复”处理旧会话记录，完成后再开始画布。"
            : displayFailure ?? notice ?? (session?.Status is CanvasSessionStatus.RecoveryRequired or CanvasSessionStatus.Paused ? session.Message : null) ?? environmentWarning ?? preferences.Warning;
        var text = attention ?? (running ? $"画布 {session?.ManagedCount ?? 0} · 便签 {fixedWindows.Count} · 桌面空白处移动视角" : "开始后接管所有可用窗口 · 便签保持独立");
        if (status.Text != text) { status.Text = text; status.FitText(attention is null ? 42 : 0); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        var hint = $"{state} · 离屏 {session?.ParkedCount ?? 0} · 相机 ({camera?.Position.X:0}, {camera?.Position.Y:0})\n{session?.Message}";
        if (hints.GetToolTip(status) != hint) hints.SetToolTip(status, hint);
    }
    internal (bool Running, int Count, PointD Camera) Diagnostics() =>
        (session?.IsRunning == true, session?.Workspace.Windows.Count ?? 0, session?.Workspace.Camera.Position ?? PointD.Zero);
    internal void EndNavigationForEditing()
    { CloseTouchNavigation(discard: true); gestures?.Accept(new(NavigationInputKind.Exit, PointD.Zero), DateTimeOffset.UtcNow); globalInput?.Exit(); hud?.Hide(); }
    internal string GoToBookmark(Guid id)
    {
        if (bookmarks.Collection.Items.FirstOrDefault(b => b.Id == id)?.Layout is not null)
        { OpenWorkspace(id); return notice ?? "正在复原工作区。"; }
        if (!CanJumpToBookmark) return "请先在窗口画布页开始管理，再跳转位置标签。";
        var bookmark = bookmarks.Collection.Items.FirstOrDefault(entry => entry.Id == id);
        if (bookmark is null) return "标签已不存在。";
        var accepted = false;
        Act(() => accepted = session!.NavigateWorkspace(bookmark.CameraPosition, DateTimeOffset.UtcNow, WindowEventListener.ForegroundWindow,
            animate: overviewAnimation.Checked && System.Windows.SystemParameters.ClientAreaAnimation));
        if (accepted) navigationTimer.Start();
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
        if (disposing) { DisposeWorkspaceRestore(); CancelLaunchStart(); launchTimer.Dispose(); inputHealthTimer.Dispose(); feedbackTimer.Dispose(); }
        if (disposing) { DisposeInputStatus(); CloseTouchNavigation(discard: true, refresh: false); DisposePinControls(); DisposeOverviewPreparation(); overviewRequested = false; overview?.CancelOverview(); moreMenu.Dispose(); hints.Dispose(); admissionTimer.Dispose(); navigationTimer.Dispose(); globalInput?.Dispose(); crosshairOverlay?.Dispose(); hud?.Dispose(); gestures?.Cancel(); verify.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
    private void ConfigurePointerScreens()
    {
        if (globalInput is null) return;
        var screens = layout?.Monitors.Select(m => m.Bounds).ToArray() ?? [];
        var areas = layout?.Monitors.Select(m => m.WorkArea).ToArray() ?? [];
        if (configuredPointerScreens.SequenceEqual(screens) && configuredTouchAreas.SequenceEqual(areas)) return;
        configuredPointerScreens = screens; configuredTouchAreas = areas; globalInput.ConfigureScreens(screens, areas);
    }
    private PixelRect[] configuredTouchAreas = [];
}
