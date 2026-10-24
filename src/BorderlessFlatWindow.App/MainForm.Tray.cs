using BorderlessFlatWindow.Application.Management;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    private enum ManagerTarget { Canvas, Recovery, Navigation, TouchNavigation, Appearance, LocalSettings, InputStatus, Exit }
    private ManagerTarget? pendingManagerTarget;
    private string? pendingInputReport;
    private bool managerActionScheduled;
    private CanvasSessionStatus? lastRecoveryStatus;
    private TrayPresentation? lastTrayPresentation;
    private ToolStripMenuItem? trayState, trayOpen, trayOverview, trayTouch, trayPause, trayRecovery, traySettings, trayInputStatus, trayExit;
    private TrayPresentation TrayState => TrayPresentation.Create(management.SessionStatus, management.Diagnostics().Count,
        management.HasOverviewWork, !management.CanUsePanelCommands || nativeOwnerDisabled || OwnedForms.Any(form => form.Visible && form.Modal),
        recovery?.RequiresAttention == true, recovery?.IsRunning == true);

    private void InitializeTray()
    {
        if (tray is not null) return;
        trayMenu = new();
        trayState = new() { Enabled = false };
        trayOpen = new("打开窗口画布", null, (_, _) => ShowManager());
        trayOverview = new("空间总览", null, (_, _) => { if (TrayState.CanOverview) management.ShowOverview(); UpdateTrayState(); });
        trayTouch = new("触控导航 · 两指滚动", null, (_, _) => RequestManager(ManagerTarget.TouchNavigation));
        trayPause = new("暂停并恢复窗口", null, (_, _) => { if (TrayState.CanPause) management.Pause(); });
        trayRecovery = new("恢复中心", null, (_, _) => RequestManager(ManagerTarget.Recovery));
        trayInputStatus = new("输入状态…", null, (_, _) => RequestManager(ManagerTarget.InputStatus));
        traySettings = new("设置");
        traySettings.DropDownItems.Add("导航与键位设置…", null, (_, _) => RequestManager(ManagerTarget.Navigation));
        traySettings.DropDownItems.Add("取色与动画", null, (_, _) => RequestManager(ManagerTarget.Appearance));
        traySettings.DropDownItems.Add("本地设置", null, (_, _) => RequestManager(ManagerTarget.LocalSettings));
        trayExit = new("退出", null, (_, _) => ExitFromTray());
        trayMenu.Items.AddRange([trayState, new ToolStripSeparator(), trayOpen, trayOverview, trayTouch, trayPause,
            trayRecovery, trayInputStatus, traySettings, new ToolStripSeparator(), trayExit]);
        trayMenu.Opening += (_, _) => UpdateTrayState();
        ChromeTheme.StyleMenu(trayMenu); ChromeTheme.StyleMenu(traySettings.DropDown);
        tray = new() { Icon = trayIcon, ContextMenuStrip = trayMenu, Visible = true };
        lastRecoveryStatus = management.SessionStatus; lastTrayPresentation = null; UpdateTrayState();
        tray.DoubleClick += (_, _) => ShowManager();
    }
    private void UpdateTrayState()
    {
        var next = TrayState;
        cameraStatus.Text = next.Caption;
        if (tray is null || next == lastTrayPresentation) return;
        // StatusChanged also runs on camera frames. Avoid sending unchanged tooltip updates to Explorer.
        lastTrayPresentation = next;
        tray.Text = next.Tooltip; trayState!.Text = next.Caption;
        trayOpen!.Enabled = trayRecovery!.Enabled = next.CanOpen;
        trayOverview!.Text = next.OverviewCaption; trayOverview.Enabled = next.CanOverview;
        trayTouch!.Enabled = next.CanOverview && !management.HasOverviewWork;
        trayPause!.Enabled = next.CanPause; traySettings!.Enabled = trayInputStatus!.Enabled = next.CanConfigure;
        trayExit!.Text = next.ExitCaption; trayExit.Enabled = next.CanExit;
    }
    private void RecoveryStateChanged()
    {
        if (IsDisposed || Disposing) return;
        management.SetExternalRecoveryRunning(recovery.IsRunning);
        management.SetRecoveryAttention(recovery.RequiresAttention);
        UpdateTrayState(); TryScheduleManagerAction();
    }
    private void ShowManager() { EnsureSessionNotifications(); RequestManager(ManagerTarget.Canvas); }
    private void RequestManager(ManagerTarget target)
    {
        if (IsDisposed || Disposing || !TrayState.CanOpen || target is not (ManagerTarget.Canvas or ManagerTarget.Recovery) && !TrayState.CanConfigure) return;
        pendingManagerTarget = target;
        // Capture before panel entry releases a navigation mode; the user needs
        // the state that led them here, rather than the modal's own pause.
        pendingInputReport = target == ManagerTarget.InputStatus ? management.CaptureRuntimeInformation() : null;
        management.SetPanelActionPending(true);
        TryScheduleManagerAction();
    }
    private void TryScheduleManagerAction()
    {
        if (pendingManagerTarget is null || managerActionScheduled || !IsHandleCreated || IsDisposed || Disposing
            || management.HasOverviewWork || !TrayState.CanOpen) return;
        managerActionScheduled = true;
        // FinishOverview may still restore the old foreground after StatusChanged. Open the panel on the next UI turn.
        BeginInvoke(() =>
        {
            managerActionScheduled = false;
            if (IsDisposed || Disposing || pendingManagerTarget is not { } target || management.HasOverviewWork || !TrayState.CanOpen) return;
            pendingManagerTarget = null;
            var inputReport = pendingInputReport; pendingInputReport = null;
            if (target == ManagerTarget.Exit)
            {
                management.SetPanelActionPending(false); exitRequested = true;
                if (management.Diagnostics().Running) management.Pause();
                Close(); return;
            }
            if (!showWithoutActivation) DockPopup(Screen.FromPoint(Cursor.Position));
            Show(); WindowState = FormWindowState.Normal;
            tabs.SelectedTab = target == ManagerTarget.Recovery ? (TabPage)recovery.Parent! : (TabPage)management.Parent!;
            if (target == ManagerTarget.Recovery && !recovery.IsRunning) recovery.RefreshJournal();
            management.SetPanelActionPending(false);
            if (!showWithoutActivation) Activate();
            if (target == ManagerTarget.Navigation) management.ShowNavigationSettings();
            else if (target == ManagerTarget.TouchNavigation) management.ShowTouchNavigation();
            else if (target == ManagerTarget.InputStatus) management.ShowRuntimeInformation(inputReport);
            else if (target is ManagerTarget.Appearance or ManagerTarget.LocalSettings) management.ShowSettingsMenu(target == ManagerTarget.Appearance);
        });
    }
    private void ExitFromTray()
    {
        if (TrayState.CanExit) RequestManager(ManagerTarget.Exit);
    }
}
