using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
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
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new(6), AutoScroll = true };
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
        start.Click += (_, _) => Begin();
        pause.Click += (_, _) => Pause();
        candidates.ItemCheck += (_, e) =>
        {
            if (candidates.Items[e.Index] is Candidate { Supported: false }) e.NewValue = CheckState.Unchecked;
        };
        verify.Tick += (_, _) => VerifyPending();
        Controls.Add(map); Controls.Add(selector); Controls.Add(status); Controls.Add(toolbar);
    }

    internal void SetDisplay(DisplayLayout current)
    {
        if (session is not null && session.MatchesDisplay(current)) return;
        if (session?.IsRunning == true) Pause("显示配置已变化，已暂停恢复；请重新选择窗口开始。");
        session?.Dispose(); layout = current;
        session = new(() => new RecoverySession(new JsonRecoveryJournalStore(journalPath), new WindowsRecoveryWindowPort()),
            new NativeWindowMotionPort(), current);
        if (preview is not null) { map.Controls.Remove(preview); preview.Dispose(); }
        preview = new(session.Workspace) { Dock = DockStyle.Fill, AutoFit = false };
        preview.WindowChosen += id => Act(() => session.Focus(id, DateTimeOffset.UtcNow));
        preview.CameraPanRequested += delta => Act(() => session.Pan(delta, DateTimeOffset.UtcNow));
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
        try { notice = null; action(); }
        catch (Exception exception) when (Expected(exception)) { session?.Pause($"导航失败：{exception.Message}"); }
        RefreshState();
    }
    private void Add(FlowLayoutPanel toolbar, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => Act(action); toolbar.Controls.Add(button);
    }
    internal void Pause(string reason = "已暂停并恢复原始位置；再次开始可恢复画布布局。")
    { verify.Stop(); session?.Pause(reason); RefreshState(); }
    private void RefreshState()
    {
        var running = session?.IsRunning == true;
        candidates.Enabled = !running; start.Enabled = !running;
        pause.Enabled = running;
        if (running && session!.HasPending) verify.Start(); else verify.Stop();
        if (preview is not null) { preview.ActiveWindowId = session?.ActiveWindowId; preview.Invalidate(); }
        var camera = session?.Workspace.Camera;
        var state = session?.Status switch { CanvasSessionStatus.Running => "管理中", CanvasSessionStatus.Paused => "已暂停", CanvasSessionStatus.RecoveryRequired => "待恢复", _ => "未开始" };
        status.Text = $"状态：{state} · 窗口 {session?.Workspace.Windows.Count ?? 0} · 离屏 {session?.ParkedCount ?? 0} · 相机 ({camera?.Position.X:0}, {camera?.Position.Y:0})\n"
            + (notice ?? session?.Message ?? "") + "\n地图内滚轮纵移，Shift 横移；点击卡片聚焦。当前仅开放单屏普通窗口；暂停恢复原位。";
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
        if (disposing) { verify.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
}
