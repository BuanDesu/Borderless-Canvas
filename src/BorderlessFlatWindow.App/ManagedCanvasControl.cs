using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.App;

internal sealed class ManagedCanvasControl : UserControl
{
    private readonly ITrackedDesktopWindowCatalog catalog;
    private readonly string journalPath;
    private readonly CheckedListBox candidates = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly Panel map = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 112, Padding = new(10) };
    private readonly Button start = new() { Text = "开始 / 恢复画布", AutoSize = true };
    private readonly Button pause = new() { Text = "暂停并恢复原位", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer verify = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer navigationTimer = new() { Interval = 16 };
    private readonly Label navigationStatus = new() { AutoSize = true, Padding = new(4) };
    private readonly ComboBox navigationShortcut = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    private GlobalNavigationInput? globalInput;
    private NavigationGestureSession? gestures;
    private NavigationHud? hud;
    private GlobalNavigationState? navigationState;
    private bool? inputAvailable;
    private NavigationInputOptions inputOptions = new();
    private ManagedCanvasSession? session;
    private DisplayLayout? layout;
    private CanvasPreviewControl? preview;
    private bool reading;
    private string? notice;
    private sealed record Candidate(Guid Id, string Label, bool Supported)
    { public override string ToString() => Label; }
    internal event Action? StatusChanged;
    internal event Action? RecoveryRequested;

    internal ManagedCanvasControl(ITrackedDesktopWindowCatalog catalog, string? journalPath)
    {
        this.catalog = catalog; this.journalPath = journalPath ?? JsonRecoveryJournalStore.DefaultPath;
        var selector = new Panel { Dock = DockStyle.Left, Width = 270, Padding = new(6) };
        selector.Controls.Add(candidates);
        selector.Controls.Add(new Label { Text = "勾选要管理的窗口", Dock = DockStyle.Top, Height = 34 });
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, Padding = new(6), AutoScroll = true };
        toolbar.Controls.AddRange([start, pause]);
        Add(toolbar, "←", () => session?.Pan(new(-240, 0), DateTimeOffset.UtcNow));
        Add(toolbar, "↑", () => session?.Pan(new(0, -180), DateTimeOffset.UtcNow));
        Add(toolbar, "↓", () => session?.Pan(new(0, 180), DateTimeOffset.UtcNow));
        Add(toolbar, "→", () => session?.Pan(new(240, 0), DateTimeOffset.UtcNow));
        Add(toolbar, "返回原点", () => session?.Home(DateTimeOffset.UtcNow));
        Add(toolbar, "上一视角", () => session?.Back(DateTimeOffset.UtcNow));
        Add(toolbar, "适配全部", () => preview?.FitToContent());
        Add(toolbar, "刷新窗口", RefreshCandidates);
        Add(toolbar, "恢复日志", () => RecoveryRequested?.Invoke());
        Add(toolbar, "进入导航（Esc 退出）", () => globalInput?.Toggle());
        navigationShortcut.Items.AddRange([NavigationHotkey.Default,
            new NavigationHotkey(3, 0x4E, "Ctrl+Alt+N"), new NavigationHotkey(3, 0x4D, "Ctrl+Alt+M")]);
        navigationShortcut.DisplayMember = nameof(NavigationHotkey.Label); navigationShortcut.SelectedIndex = 0;
        navigationShortcut.SelectedIndexChanged += (_, _) =>
        {
            if (navigationShortcut.SelectedItem is NavigationHotkey shortcut) globalInput?.ChangeHotkey(shortcut);
        };
        toolbar.Controls.Add(navigationShortcut);
        Add(toolbar, "导航设置…", () =>
        {
            globalInput?.Exit();
            using var dialog = new NavigationSettingsDialog(inputOptions);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            gestures?.End(DateTimeOffset.UtcNow); inputOptions = dialog.Options;
            if (gestures is not null) gestures.Options = inputOptions;
            globalInput?.Configure(inputOptions);
        });
        toolbar.Controls.Add(navigationStatus);
        start.Click += (_, _) => Begin();
        pause.Click += (_, _) => Pause();
        candidates.ItemCheck += (_, e) =>
        {
            if (candidates.Items[e.Index] is Candidate { Supported: false }) e.NewValue = CheckState.Unchecked;
        };
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
        if (navigationState is not null) navigationStatus.Text = navigationState.Registered ? navigationState.Message
            : $"快捷键 {navigationState.Hotkey.Label} 未注册：{navigationState.Message}";
        if (events.Any(item => item.Input.Kind == NavigationInputKind.Fault))
        {
            gestures?.Cancel(); Pause("导航输入异常或响应超时，已暂停恢复。"); return;
        }
        foreach (var item in events)
        {
            if (item.Input.Kind is NavigationInputKind.Home or NavigationInputKind.Back)
            {
                gestures?.Accept(new(NavigationInputKind.Exit, PointD.Zero), item.At);
                if (item.Input.Kind == NavigationInputKind.Home) session?.Home(item.At); else session?.Back(item.At);
            }
            else gestures?.Accept(item.Input, item.At);
        }
        gestures?.Tick(DateTimeOffset.UtcNow);
        if (navigationState?.Active == true && session?.IsRunning == true)
        {
            navigationTimer.Start(); hud ??= new();
            var camera = session.Workspace.Camera.Position;
            hud.Present(navigationState.Hotkey.Label, camera, inputOptions);
        }
        else
        {
            hud?.Hide();
            if (gestures?.HasGesture == true) navigationTimer.Start(); else navigationTimer.Stop();
        }
        RefreshState();
    }

    internal void SetDisplay(DisplayLayout current)
    {
        if (session is not null && session.MatchesDisplay(current)) return;
        if (session?.IsRunning == true) Pause("显示配置已变化，已暂停恢复；请重新选择窗口开始。");
        session?.Dispose(); layout = current;
        session = new(() => new RecoverySession(new JsonRecoveryJournalStore(journalPath), new WindowsRecoveryWindowPort()),
            new NativeWindowMotionPort(), current);
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
        map.Controls.Add(preview); RefreshState();
    }

    internal void Observe(WindowCatalogSnapshot snapshot, nint foreground)
    {
        if (reading || IsDisposed) return;
        session?.Observe(snapshot, foreground, DateTimeOffset.UtcNow);
        if (session?.IsRunning != true && layout is not null)
        {
            var selected = candidates.CheckedItems.OfType<Candidate>().Select(item => item.Id).ToHashSet();
            var previous = candidates.SelectedItem is Candidate selectedItem ? selectedItem.Id : (Guid?)null;
            var next = snapshot.Entries.Where(entry => entry.Admission.IsAdmitted && entry.Id is not null).Select(entry =>
            {
                var reason = ManagedCanvasSession.Unsupported(entry, layout);
                return new Candidate(entry.Id!.Value, entry.Observation.Title + (reason is null ? "" : " · " + reason), reason is null);
            }).ToArray();
            if (!candidates.Items.OfType<Candidate>().SequenceEqual(next))
            {
                candidates.BeginUpdate();
                try
                {
                    candidates.Items.Clear();
                    foreach (var item in next)
                    {
                        var index = candidates.Items.Add(item, selected.Contains(item.Id) && item.Supported);
                        if (item.Id == previous) candidates.SelectedIndex = index;
                    }
                }
                finally { candidates.EndUpdate(); }
            }
        }
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
            var entries = snapshot.Entries.Where(entry => entry.Id is { } id && selected.Contains(id)).ToArray();
            if (entries.Length != selected.Count) { notice = "窗口已经改变，请刷新并重新选择。"; return; }
            session.Start(entries, DateTimeOffset.UtcNow);
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
    { gestures?.Cancel(); verify.Stop(); session?.Pause(reason); RefreshState(); }
    private void RefreshState()
    {
        var running = session?.IsRunning == true;
        if (globalInput is not null && inputAvailable != running)
        { inputAvailable = running; globalInput.SetAvailable(running); }
        if (!running) { navigationTimer.Stop(); hud?.Hide(); gestures?.Cancel(); }
        candidates.Enabled = !running; start.Enabled = !running;
        pause.Enabled = running;
        if (running && session!.HasPending) verify.Start(); else verify.Stop();
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; preview.Invalidate(); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        status.Text = $"状态：{state} · 窗口 {session?.Workspace.Windows.Count ?? 0} · 离屏 {session?.ParkedCount ?? 0} · 相机 ({camera?.Position.X:0}, {camera?.Position.Y:0})\n"
            + (notice ?? session?.Message ?? "") + "\n地图内滚轮或进入全局导航后平移；Esc 退出导航。当前仅开放单屏普通窗口；暂停恢复原位。";
        StatusChanged?.Invoke();
    }
    internal (bool Running, int Count, PointD Camera) Diagnostics() =>
        (session?.IsRunning == true, session?.Workspace.Windows.Count ?? 0, session?.Workspace.Camera.Position ?? PointD.Zero);
    internal bool ProcessNavigationKey(Keys key) => key switch
    {
        Keys.Home => Key(() => session?.Home(DateTimeOffset.UtcNow)),
        Keys.Back => Key(() => session?.Back(DateTimeOffset.UtcNow)),
        _ => false
    };
    private bool Key(Action action) { if (session?.IsRunning != true) return false; Act(action); return true; }
    private static bool Expected(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidOperationException
        or Win32Exception or ArgumentException or OverflowException;
    protected override void Dispose(bool disposing)
    {
        if (disposing) { navigationTimer.Dispose(); globalInput?.Dispose(); hud?.Dispose(); gestures?.Cancel(); verify.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
}
