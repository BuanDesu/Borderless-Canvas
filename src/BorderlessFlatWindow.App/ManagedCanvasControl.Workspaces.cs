using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private readonly Guid workspaceSession = Guid.NewGuid();
    private readonly System.Windows.Forms.Timer workspaceRestoreTimer = new() { Interval = 100 };
    private readonly Dictionary<(uint Process, long Created), string?> workspaceExecutables = [];
    private readonly Dictionary<Guid, Guid> workspaceBindings = [];
    private WorkspaceRestorer? workspaceRestorer;
    private WorkspaceProgressForm? workspaceProgress;
    private string? workspaceProgressKey;
    private bool workspaceOperation;
    private bool workspaceStarting, workspaceAdvancing;
    private Guid? queuedWorkspace;
    private bool WorkspaceRestoring => workspaceRestorer?.Active == true;
    private void InitializeWorkspaceRestore() => workspaceRestoreTimer.Tick += (_, _) => AdvanceWorkspaceRestore();
    private string? WorkspaceExecutable(NativeWindowIdentity identity)
    {
        var key = (identity.ProcessId, identity.ProcessCreationFileTime);
        if (!workspaceExecutables.TryGetValue(key, out var value))
        {
            value = NativeWindowIdentityGuard.Executable(identity);
            if (workspaceExecutables.Count >= 1024) workspaceExecutables.Clear();
            workspaceExecutables[key] = value;
        }
        return value;
    }
    private WorkspaceSnapshot CaptureWorkspace(PointD camera)
    {
        if (session?.IsRunning != true || layout is null || session.IsBusy || session.HasPending || WorkspaceRestoring)
            throw new InvalidOperationException("请先等待窗口移动完成，再保存布局。");
        lastSnapshot = RefreshManagedSnapshot(session);
        session.Observe(lastSnapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
        if (!session.IsRunning || session.HasPending) throw new InvalidOperationException("窗口状态已变化，请等待核验后重试。");
        var areas = layout.Monitors.Select(m => new RectD(camera + m.WorkArea.Position - layout.CanvasWorkArea.Position, m.WorkArea.Size)).ToArray();
        var entries = (lastSnapshot ?? catalog.Current).Entries.Where(e => e.Id is not null).ToDictionary(e => e.Id!.Value);
        var members = new List<WorkspaceMember>();
        foreach (var window in session.Workspace.Windows)
        {
            if (!areas.Any(a => window.Bounds.Right > a.Position.X && window.Bounds.Position.X < a.Right && window.Bounds.Bottom > a.Position.Y && window.Bounds.Position.Y < a.Bottom)
                || !entries.TryGetValue(window.Id, out var entry) || !entry.Admission.IsAdmitted || entry.Observation.Identity is not { } identity
                || fixedWindows.Contains(identity)) continue;
            var executable = WorkspaceExecutable(identity);
            members.Add(new(Guid.NewGuid(), window.Id, window.Title.Length > 512 ? window.Title[..512] : window.Title, executable,
                entry.Observation.ClassName, window.Bounds, WorkspaceLauncher.Default(executable)));
        }
        var snapshot = new WorkspaceSnapshot(workspaceSession, layout.Primary.EffectiveDpi ?? 96, Array.AsReadOnly(members.ToArray()));
        snapshot.Validate(); return snapshot;
    }
    private WorkspaceActions WorkspaceCommands() => new(bookmarks, CaptureWorkspace, () => WorkspaceCandidates(catalog.Current),
        (member, window) => workspaceBindings[member] = window);
    internal void OpenWorkspace(Guid id)
    {
        if (IsDisposed || Disposing) return;
        if (workspaceStarting || workspaceAdvancing || reading || session?.IsBusy == true)
        { queuedWorkspace = id; workspaceRestoreTimer.Start(); return; }
        workspaceStarting = true;
        try { OpenWorkspaceCore(id); }
        catch (Exception error) when (Expected(error))
        { CancelWorkspaceRestore(); session?.SuspendNavigation(false); notice = "工作区未能打开；请检查窗口状态、启动项或恢复中心。"; }
        finally
        {
            workspaceStarting = false;
            if (!IsDisposed && !Disposing)
            { RefreshState(); if (WorkspaceRestoring || queuedWorkspace is not null) AdvanceWorkspaceRestore(); }
        }
    }
    private void OpenWorkspaceCore(Guid id)
    {
        var bookmark = bookmarks.Collection.Items.FirstOrDefault(b => b.Id == id);
        if (bookmark is null || session is null || session.IsBusy || reading || desktopTransitionPending || externalRecoveryRunning || layout is null || recoveryAttention) return;
        if (!session.IsRunning && !session.Start([], DateTimeOffset.UtcNow)) { notice = session.Message; RefreshState(); return; }
        if (WorkspaceRestoring && workspaceRestorer!.WorkspaceId == id) { workspaceProgress?.Present(); return; }
        CancelWorkspaceRestore(); EndNavigationForEditing(); InvalidateOverviewPreparation();
        gestures?.Cancel(); touchInputs.Clear(); touchInputOverflow = false; navigationTimer.Stop(); session.StopCameraTransition();
        globalInput?.SetAvailable(false); inputAvailable = false;
        session.SuspendNavigation(true);
        try { lastSnapshot = catalog.Inspect(); session.Observe(lastSnapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow); }
        finally { session.SuspendNavigation(false); }
        if (!session.IsRunning || !session.NavigateWorkspace(bookmark.CameraPosition, DateTimeOffset.UtcNow, WindowEventListener.ForegroundWindow, animate: false)) return;
        if (bookmark.Layout is null) { notice = "已打开旧版位置工作区；未保存窗口成员。"; RefreshState(); return; }
        if (bookmark.Layout.Windows.Count == 0) { notice = "已打开空布局工作区；没有需要启动或移动的成员。"; RefreshState(); return; }
        workspaceRestorer ??= new(new WorkspacePort(this));
        workspaceRestorer.Begin(bookmark, workspaceSession, DateTimeOffset.UtcNow, member => session.ProjectWorkspaceMember(bookmark, member), new Dictionary<Guid, Guid>(workspaceBindings));
        session.BeginWorkspaceRestore(); workspaceOperation = true;
        workspaceProgress?.Dispose(); workspaceProgress = new(bookmark.Name, FindForm() is MainForm { IsTransparentFixture: true });
        workspaceProgress.CancelRequested += () => { CancelWorkspaceRestore(); RefreshState(); };
        workspaceProgress.Present(); workspaceProgressKey = null;
        workspaceRestoreTimer.Start(); RefreshState(); AdvanceWorkspaceRestore();
    }
    private IReadOnlyList<WorkspaceCandidate> WorkspaceCandidates(WindowCatalogSnapshot snapshot) => snapshot.Entries
        .Where(e => e.Observation.IsAlive && !e.Observation.IsChildWindow && !e.Observation.IsToolWindow && e.Observation.OwnerHandle == 0
            && !string.IsNullOrWhiteSpace(e.Observation.Title) && e.Admission.Reason is not (WindowExclusionReason.OwnProcess or WindowExclusionReason.DesktopShell)).Select(e =>
        {
            var identity = e.Observation.Identity ?? new(e.Observation.Handle, e.Observation.ProcessId, 0);
            var retryable = e.Admission.Reason is WindowExclusionReason.NotVisible or WindowExclusionReason.MetadataUnavailable
                or WindowExclusionReason.IdentityUnavailable or WindowExclusionReason.Cloaked or WindowExclusionReason.CloakingUnknown or WindowExclusionReason.VirtualDesktopUnknown;
            return new WorkspaceCandidate(e.Id ?? Guid.Empty, identity, identity.IsValid ? WorkspaceExecutable(identity) : null, e.Observation.ClassName, e.Observation.IsMinimized,
                identity.IsValid && (e.Admission.IsAdmitted || e.Admission.Reason == WindowExclusionReason.Minimized) && !e.Observation.IsMaximized && !e.Observation.IsTopMost && !fixedWindows.Contains(identity), e.Observation.Title,
                retryable && !fixedWindows.Contains(identity));
        }).ToArray();
    private void AdvanceWorkspaceRestore()
    {
        if (workspaceStarting || workspaceAdvancing || reading || session?.IsBusy == true) return;
        if (queuedWorkspace is { } next) { queuedWorkspace = null; OpenWorkspace(next); return; }
        if (!WorkspaceRestoring) { workspaceRestoreTimer.Stop(); return; }
        workspaceAdvancing = true;
        try { AdvanceWorkspaceRestoreCore(); }
        finally { workspaceAdvancing = false; if (queuedWorkspace is not null && !IsDisposed && !Disposing) workspaceRestoreTimer.Start(); }
    }
    private void AdvanceWorkspaceRestoreCore()
    {
        if (session?.IsRunning != true || desktopTransitionPending || externalRecoveryRunning || layout is null)
        { CancelWorkspaceRestore(); RefreshState(); return; }
        workspaceRestorer!.Advance(WorkspaceCandidates(catalog.Current), DateTimeOffset.UtcNow);
        var results = workspaceRestorer.Results; var key = string.Join('|', results.Select(r => r.State));
        if (key != workspaceProgressKey)
        {
            workspaceProgressKey = key; workspaceProgress?.SetResults(results, WorkspaceRestoring);
            notice = WorkspaceRestoring ? $"正在复原工作区 {results.Count(r => r.Success)} / {results.Count}；可在进度窗口取消。"
                : $"工作区复原完成 {results.Count(r => r.Success)} / {results.Count}；部分失败可在进度窗口查看。";
            RefreshState();
        }
        if (!WorkspaceRestoring)
        {
            workspaceRestoreTimer.Stop(); session.EndWorkspaceRestore(); workspaceOperation = false;
            if (!session.IsRunning) notice = "工作区未能完整复原；请检查恢复中心中的待恢复记录。";
            RefreshState();
        }
    }
    private void CancelWorkspaceRestore()
    {
        var cancelled = WorkspaceRestoring;
        workspaceRestoreTimer.Stop(); queuedWorkspace = null; workspaceRestorer?.Cancel();
        if (workspaceOperation) { workspaceOperation = false; session?.EndWorkspaceRestore(); }
        if (workspaceRestorer is not null) workspaceProgress?.SetResults(workspaceRestorer.Results, false);
        if (cancelled) notice = "已取消工作区复原；已启动的软件保持打开，已提交位置继续核验。";
    }
    private void DisposeWorkspaceRestore()
    { CancelWorkspaceRestore(); workspaceRestoreTimer.Dispose(); workspaceProgress?.Dispose(); workspaceProgress = null; }
    private sealed class WorkspacePort(ManagedCanvasControl owner) : IWorkspaceRestorePort
    {
        public bool Available => owner.session?.IsRunning == true && owner.workspaceOperation && !owner.desktopTransitionPending && !owner.externalRecoveryRunning && !owner.IsDisposed && !owner.Disposing;
        public void Launch(WorkspaceLaunch launch)
        {
            if (!Available) throw new InvalidOperationException("工作区操作已停止。");
            WorkspaceLauncher.Start(launch);
        }
        private InspectedWindow? Current(WorkspaceCandidate candidate)
        {
            owner.catalog.RefreshWindow(candidate.Identity.Handle);
            return owner.catalog.Current.Entries.FirstOrDefault(e => e.Id == candidate.Id && e.Observation.Identity == candidate.Identity);
        }
        public bool RestoreMinimized(WorkspaceCandidate candidate) => Current(candidate) is { } entry && owner.session?.RestoreWorkspaceMinimized(entry) == true;
        public bool Apply(WorkspaceCandidate candidate, RectD bounds) => Current(candidate) is { } entry && !owner.fixedWindows.Contains(candidate.Identity)
            && owner.session?.ApplyWorkspaceMember(entry, bounds, DateTimeOffset.UtcNow) == true;
        public WorkspacePlacement Check(WorkspaceCandidate candidate) => owner.session?.CheckWorkspaceMember(candidate.Id, candidate.Identity) ?? WorkspacePlacement.Failed;
        public void CancelPrepared() => owner.session?.CancelWorkspacePrepared();
    }
}
