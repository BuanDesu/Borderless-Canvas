using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

public enum CanvasSessionStatus { Idle, Running, Paused, RecoveryRequired }

/// <summary>Owns the logical layout. Native parked rectangles never replace world coordinates.</summary>
public sealed class ManagedCanvasSession : IDisposable
{
    private readonly Func<RecoverySession> createRecovery;
    private readonly IWindowMotionPort motion;
    private readonly DisplayLayout layout;
    private readonly WindowPlacementPlanner planner;
    private readonly Dictionary<Guid, Managed> managed = [];
    private RecoverySession? recovery;
    private Guid? activation;
    private nint lastForeground;
    private bool busy;
    private bool disposed, disposeRequested;
    private sealed class Managed(Guid id, NativeWindowIdentity identity, Guid recoveryId, PixelRect observed)
    {
        internal readonly Guid Id = id;
        internal readonly NativeWindowIdentity Identity = identity;
        internal readonly Guid RecoveryId = recoveryId;
        internal PixelRect Observed = observed;
        internal PixelRect? Desired;
        internal bool Parked;
        internal DateTimeOffset? Deadline;
        internal readonly HashSet<PixelRect> Expected = [];
    }

    public ManagedCanvasSession(Func<RecoverySession> createRecovery, IWindowMotionPort motion, DisplayLayout layout, BookmarkCollection? bookmarks = null)
    {
        this.createRecovery = createRecovery; this.motion = motion; this.layout = layout;
        planner = new(layout.Primary.WorkArea, layout.Monitors.Select(monitor => monitor.Bounds));
        Workspace = new(layout.Primary.WorkArea.Size, bookmarks);
    }
    public CanvasWorkspace Workspace { get; }
    public CanvasSessionStatus Status { get; private set; }
    public string Message { get; private set; } = "尚未开始接管。";
    public bool IsRunning => Status == CanvasSessionStatus.Running;
    public bool IsBusy => busy;
    public bool HasPending => managed.Values.Any(window => window.Deadline is not null);
    public Guid? ActiveWindowId { get; private set; }
    public IReadOnlyList<nint> PendingHandles => managed.Values.Where(window => window.Deadline is not null).Select(window => window.Identity.Handle).ToArray();
    public IReadOnlyList<nint> ManagedHandles => managed.Values.Select(window => window.Identity.Handle).ToArray();
    public IReadOnlyList<Guid> ManagedIds => managed.Keys.ToArray();
    public int ParkedCount => managed.Values.Count(window => window.Parked);
    public bool NavigationSuspended { get; private set; }
    public void SuspendNavigation(bool suspended) { NavigationSuspended = suspended; if (suspended) activation = null; }

    public static string? Unsupported(InspectedWindow entry, DisplayLayout layout)
    {
        if (!entry.Admission.IsAdmitted || entry.Id is null || entry.Observation.Identity is null) return entry.Admission.Description;
        var window = entry.Observation;
        if (window.IsMaximized || window.IsTopMost) return "请先取消最大化或置顶。";
        if (window.Bounds is { } bounds && layout.Monitors.Any(monitor => bounds.Position.X <= monitor.Bounds.Left
            && bounds.Position.Y <= monitor.Bounds.Top && bounds.Right >= monitor.Bounds.Right && bounds.Bottom >= monitor.Bounds.Bottom))
            return "全屏或覆盖整块屏幕的窗口暂不接管。";
        return null;
    }

    public bool Start(IReadOnlyList<InspectedWindow> selection, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (busy || IsRunning) return false;
        if (layout.Monitors.Count != 1 || layout.Primary.EffectiveDpi is null)
        { Message = "原型仅在 DPI 已知的单显示器配置上开放管理。"; return false; }
        if (selection.Count == 0 || selection.Select(entry => entry.Id).Distinct().Count() != selection.Count)
        { Message = "请选择至少一个不同的普通窗口。"; return false; }
        var unsupported = selection.Select(entry => Unsupported(entry, layout)).FirstOrDefault(reason => reason is not null);
        if (unsupported is not null) { Message = unsupported; return false; }
        busy = true;
        var registering = false;
        try
        {
            var resume = selection.Count == managed.Count && selection.All(entry => managed.TryGetValue(entry.Id!.Value, out var prior)
                && prior.Identity == entry.Observation.Identity);
            var previous = Workspace.Windows.ToDictionary(window => window.Id);
            var camera = resume ? Workspace.Camera.Position : PointD.Zero;
            recovery?.Dispose(); recovery = createRecovery();
            if (recovery.Journal.Entries.Count > 0)
            {
                recovery.Dispose(); recovery = null;
                Status = CanvasSessionStatus.RecoveryRequired; Message = "检测到旧会话记录，请先在恢复日志页处理。";
                return false;
            }
            registering = true;
            managed.Clear();
            if (!resume) Workspace.ResetWindowsAndNavigation();
            foreach (var entry in selection)
            {
                var identity = entry.Observation.Identity!.Value;
                var original = recovery.Register(identity);
                ThrowIfClosing();
                if (original.Original.Placement.ShowCommand != 1) throw new InvalidOperationException("窗口已改变显示状态，请恢复普通窗口后重试。");
                var id = entry.Id!.Value;
                var physical = original.Original.PhysicalBounds;
                var position = resume ? previous[id].Bounds.Position : physical.Position - layout.Primary.WorkArea.Position;
                Workspace.UpsertWindow(new(id, entry.Observation.Title, new(position, physical.Size)));
                managed.Add(id, new(id, identity, original.Id, physical));
            }
            Status = CanvasSessionStatus.Running;
            Message = resume ? "画布已恢复，保留上次世界布局与相机。" : "已接管所选窗口。";
            return ApplyCamera(camera, now, recordHistory: false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (registering) PauseInternal($"接管失败：{exception.Message}");
            else { recovery?.Dispose(); recovery = null; Status = CanvasSessionStatus.RecoveryRequired; Message = $"无法开始接管：{exception.Message}"; }
            return false;
        }
        finally { ExitBusy(); }
    }

    public bool Navigate(PointD camera, DateTimeOffset now, bool recordHistory = true)
    {
        if (!IsRunning || busy || NavigationSuspended) return false;
        busy = true;
        activation = null;
        try { return ApplyCamera(camera, now, recordHistory); }
        catch (Exception exception) when (IsExpected(exception)) { PauseInternal($"平移失败：{exception.Message}"); return false; }
        finally { ExitBusy(); }
    }
    public bool AddWindows(IReadOnlyList<InspectedWindow> selection, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsRunning || busy || NavigationSuspended || HasPending)
        { Message = "请等待当前移动或总览结束后再接入窗口。"; return false; }
        if (selection.Count == 0 || selection.Any(entry => entry.Id is null || entry.Id == Guid.Empty || entry.Observation.Identity is not { IsValid: true })
            || selection.Select(entry => entry.Id).Distinct().Count() != selection.Count
            || selection.Select(entry => entry.Observation.Handle).Distinct().Count() != selection.Count
            || selection.Any(entry => managed.ContainsKey(entry.Id!.Value) || managed.Values.Any(window => window.Identity.Handle == entry.Observation.Handle)))
        { Message = "请选择尚未接入的不同普通窗口。"; return false; }
        var unsupported = selection.Select(entry => Unsupported(entry, layout)).FirstOrDefault(reason => reason is not null);
        if (unsupported is not null) { Message = unsupported; return false; }
        busy = true; var accepted = false; var published = new List<Guid>();
        try
        {
            var occupied = Workspace.Windows.Select(window => window.Bounds).ToList();
            var staged = new List<(CanvasWindow Window, Managed State, WindowPlacementPlan Plan)>();
            // Register every new identity before the first native move. Existing windows are never re-registered or replanned.
            foreach (var entry in selection)
            {
                var original = recovery!.Register(entry.Observation.Identity!.Value);
                ThrowIfClosing();
                if (original.Original.Placement.ShowCommand != 1) throw new InvalidOperationException("新窗口已改变显示状态，请恢复普通窗口后重试。");
                if (!NearbyWindowPlacement.TryPlace(original.Original.PhysicalBounds.Size, Workspace.Camera.Bounds, occupied, out var bounds)
                    || !planner.TryPlan(bounds, Workspace.Camera.Position, out var plan))
                    throw new InvalidOperationException("无法为新窗口规划可恢复的空闲位置。");
                var id = entry.Id!.Value;
                staged.Add((new(id, entry.Observation.Title, bounds), new(id, entry.Observation.Identity.Value, original.Id, original.Original.PhysicalBounds), plan!));
                occupied.Add(bounds);
            }
            var translations = new List<WindowTranslation>();
            ThrowIfClosing();
            foreach (var (window, state, plan) in staged)
            {
                Workspace.UpsertWindow(window); managed.Add(window.Id, state); published.Add(window.Id);
                state.Desired = plan.PhysicalBounds; state.Parked = plan.IsParked;
                if (state.Observed != plan.PhysicalBounds)
                {
                    state.Expected.Add(state.Observed); state.Expected.Add(plan.PhysicalBounds); state.Deadline = now.AddMilliseconds(1500);
                    translations.Add(new(state.RecoveryId, plan.PhysicalBounds));
                }
            }
            if (translations.Count > 0 && motion.RequestBatch(recovery!, translations) is { Accepted: false } failure)
            { PauseInternal($"新窗口拒绝接入，已暂停恢复：{failure.Detail}"); return false; }
            accepted = true;
            var outside = staged.Count(item => !NearbyWindowPlacement.FitsInside(item.Window.Bounds, Workspace.Camera.Bounds));
            Message = $"已接入 {staged.Count} 个窗口；既有布局、相机和标签保持。"
                + (outside > 0 ? $" {outside} 个空位位于视口附近，可用总览定位。" : "");
            return true;
        }
        catch (Exception exception) when (IsExpected(exception)) { PauseInternal($"新窗口接入失败，已暂停恢复：{exception.Message}"); return false; }
        finally
        {
            if (!accepted) foreach (var id in published) { Workspace.RemoveWindow(id); managed.Remove(id); }
            ExitBusy();
        }
    }
    public bool GoToBookmark(Guid id, DateTimeOffset now)
    {
        var bookmark = Workspace.Bookmarks.FirstOrDefault(entry => entry.Id == id);
        return bookmark is not null && Navigate(bookmark.CameraPosition, now);
    }
    public bool Pan(PointD delta, DateTimeOffset now) => Navigate(Workspace.Camera.Position + delta, now);
    public bool Home(DateTimeOffset now) => Navigate(PointD.Zero, now);
    public bool Back(DateTimeOffset now)
    {
        if (Workspace.PreviousView is not { } previous || !IsRunning || busy) return false;
        if (!Navigate(previous, now, recordHistory: false)) return false;
        Workspace.ReturnToPreviousView();
        return true;
    }
    public bool Focus(Guid id, DateTimeOffset now, bool activate = true)
    {
        if (!IsRunning || busy || !managed.ContainsKey(id)) return false;
        var target = Workspace.Windows.Single(window => window.Id == id);
        if (!Navigate(Workspace.Camera.CenterOn(target.Bounds).Position, now)) return false;
        activation = activate ? id : null;
        if (managed[id].Deadline is null) CompleteActivation();
        return true;
    }

    private bool ApplyCamera(PointD camera, DateTimeOffset now, bool recordHistory)
    {
        var translations = new List<WindowTranslation>();
        var plans = new Dictionary<Guid, WindowPlacementPlan>();
        foreach (var window in Workspace.Windows)
        {
            if (!planner.TryPlan(window.Bounds, camera, out var plan))
            { Message = "目标位置无法安全投影；相机未改变。"; return false; }
            plans.Add(window.Id, plan!);
        }
        foreach (var pair in plans)
        {
            var plan = pair.Value;
            var state = managed[pair.Key];
            if (state.Desired != plan.PhysicalBounds || state.Deadline is not null)
            {
                state.Expected.Add(state.Observed);
                if (state.Desired is { } prior) state.Expected.Add(prior);
                state.Expected.Add(plan.PhysicalBounds);
                if (state.Expected.Count > 128) { PauseInternal("移动请求积压，已暂停恢复。"); return false; }
                state.Desired = plan.PhysicalBounds;
                state.Deadline ??= now.AddMilliseconds(1500);
                translations.Add(new(state.RecoveryId, plan.PhysicalBounds));
            }
            state.Parked = plan.IsParked;
        }
        if (translations.Count > 0)
        {
            var result = motion.RequestBatch(recovery!, translations);
            if (!result.Accepted) { PauseInternal($"窗口拒绝平移，已暂停：{result.Detail}"); return false; }
        }
        if (recordHistory) Workspace.NavigateTo(camera); else Workspace.SetCameraPosition(camera);
        return true;
    }

    public void Observe(WindowCatalogSnapshot snapshot, nint foreground, DateTimeOffset now)
    {
        if (busy || disposed) return;
        busy = true;
        try
        {
            var byHandle = snapshot.Entries.ToDictionary(entry => entry.Observation.Handle);
            // Closing often emits hide first. That pauses management before the later destroy event.
            if (!IsRunning) { PruneClosed(byHandle); return; }
            foreach (var entry in snapshot.Entries)
            {
                var observation = entry.Observation;
                if (observation.IsVisible && !observation.IsToolWindow && !string.IsNullOrWhiteSpace(observation.Title)
                    && HasManagedOwner(observation.OwnerHandle, byHandle))
                { PauseInternal("检测到关联弹窗，已暂停并恢复窗口以便完成交互。"); return; }
            }
            // Foreground is handled before geometry: shells/apps may reposition an activated parked window.
            var active = managed.Values.FirstOrDefault(window => window.Identity.Handle == foreground);
            ActiveWindowId = active?.Id;
            if (!NavigationSuspended && foreground != lastForeground && active is { Parked: true })
            {
                var window = Workspace.Windows.Single(window => window.Id == active.Id);
                if (byHandle.TryGetValue(foreground, out var activated) && activated.Id == active.Id
                    && activated.Observation.Identity == active.Identity && activated.Admission.IsAdmitted
                    && !activated.Observation.IsMaximized && !activated.Observation.IsTopMost
                    && activated.Observation.Bounds is { } current && current.Size == window.Bounds.Size)
                {
                    // Shell activation may move an offscreen window before its foreground event arrives.
                    // Accept that physical intermediate location without changing the logical layout.
                    active.Observed = new(checked((int)current.Position.X), checked((int)current.Position.Y),
                        checked((int)current.Right), checked((int)current.Bottom));
                }
                if (!ApplyCamera(Workspace.Camera.CenterOn(window.Bounds).Position, now, recordHistory: true)) return;
                activation = null;
            }
            lastForeground = foreground;
            var mapper = new DesktopCoordinateMapper(Workspace.Camera.Position, layout.Primary.WorkArea);
            foreach (var state in managed.Values.ToArray())
            {
                if (!byHandle.TryGetValue(state.Identity.Handle, out var entry) || entry.Id != state.Id
                    || entry.Observation.Identity != state.Identity || !entry.Admission.IsAdmitted
                    || entry.Observation.IsMaximized || entry.Observation.IsTopMost)
                {
                    PruneClosed(byHandle);
                    PauseInternal("受管理窗口关闭、身份或状态改变，已暂停恢复。"); return;
                }
                var bounds = entry.Observation.Bounds!.Value;
                var actual = new PixelRect(checked((int)bounds.Position.X), checked((int)bounds.Position.Y), checked((int)bounds.Right), checked((int)bounds.Bottom));
                var world = Workspace.Windows.Single(window => window.Id == state.Id);
                if (state.Deadline is not null)
                {
                    if (actual == state.Desired)
                    { state.Deadline = null; state.Expected.Clear(); state.Observed = actual; }
                    else if (state.Expected.Contains(actual))
                    {
                        if (actual != state.Observed) { state.Observed = actual; state.Deadline = now.AddMilliseconds(1500); }
                        if (now >= state.Deadline) { PauseInternal("窗口移动未在期限内确认，已暂停恢复。"); return; }
                    }
                    else { PauseInternal("移动期间出现意外位置或尺寸，已暂停恢复。"); return; }
                }
                else if (actual != state.Observed)
                {
                    if (state.Parked) { PauseInternal("离屏窗口被外部移动，已暂停恢复。"); return; }
                    state.Observed = actual; state.Desired = actual;
                    state.Parked = !WindowPlacementPlanner.Intersects(actual, layout.Primary.WorkArea);
                    world = world with { Bounds = new(mapper.PhysicalToWorld(actual.Position), actual.Size) };
                }
                Workspace.UpsertWindow(world with { Title = entry.Observation.Title });
            }
            CompleteActivation();
        }
        catch (Exception exception) when (IsExpected(exception)) { PauseInternal($"同步失败：{exception.Message}"); }
        finally { ExitBusy(); }
    }

    private void PruneClosed(IReadOnlyDictionary<nint, InspectedWindow> entries)
    {
        foreach (var candidate in managed.Values.ToArray())
        {
            if (entries.TryGetValue(candidate.Identity.Handle, out var current) && current.Observation.IsAlive
                && (current.Observation.Identity is null || current.Observation.Identity == candidate.Identity)) continue;
            Workspace.RemoveWindow(candidate.Id); managed.Remove(candidate.Id);
            if (ActiveWindowId == candidate.Id) ActiveWindowId = null;
        }
    }

    private void CompleteActivation()
    {
        if (activation is not { } id || !managed.TryGetValue(id, out var target) || target.Deadline is not null) return;
        activation = null;
        Message = motion.RequestActivation(recovery!, target.RecoveryId)
            ? "已定位并请求激活目标窗口。" : "已定位目标；Windows 未允许激活，请点击窗口。";
    }

    private bool HasManagedOwner(nint owner, Dictionary<nint, InspectedWindow> entries)
    {
        var visited = new HashSet<nint>();
        while (owner != 0 && visited.Add(owner) && visited.Count <= 32)
        {
            if (managed.Values.Any(window => window.Identity.Handle == owner)) return true;
            owner = entries.TryGetValue(owner, out var entry) ? entry.Observation.OwnerHandle : 0;
        }
        return false;
    }
    public void Pause(string reason = "已暂停并恢复原始窗口位置。")
    {
        if (busy) return;
        busy = true; try { PauseInternal(reason); } finally { ExitBusy(); }
    }
    private void PauseInternal(string reason)
    {
        activation = null;
        foreach (var state in managed.Values) { state.Deadline = null; state.Expected.Clear(); }
        try
        {
            var result = recovery?.RecoverAll();
            Status = result is { IsComplete: false } ? CanvasSessionStatus.RecoveryRequired : CanvasSessionStatus.Paused;
            Message = reason + (result is { IsComplete: false } ? $" 仍有 {result.Remaining} 项待恢复，请打开恢复日志。" : "");
        }
        catch (Exception exception) when (IsExpected(exception))
        { Status = CanvasSessionStatus.RecoveryRequired; Message = $"{reason} 恢复日志已保留：{exception.Message}"; }
        finally { recovery?.Dispose(); recovery = null; }
    }
    public bool MatchesDisplay(DisplayLayout current) => layout.Monitors.SequenceEqual(current.Monitors);
    private static bool IsExpected(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or OverflowException;
    private void ThrowIfClosing()
    { if (disposeRequested) throw new InvalidOperationException("会话正在退出，已取消接管。"); }
    private void ExitBusy()
    { busy = false; if (disposeRequested) Dispose(); }
    public void Dispose()
    {
        if (disposed) return;
        if (busy) { disposeRequested = true; return; }
        disposeRequested = false;
        if (IsRunning || recovery is not null) Pause();
        disposed = true;
    }
}
