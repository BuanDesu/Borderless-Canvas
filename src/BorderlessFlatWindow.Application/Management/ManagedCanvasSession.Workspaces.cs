using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

public sealed partial class ManagedCanvasSession
{
    private readonly Dictionary<NativeWindowIdentity, Guid> workspacePrepared = [];
    private bool workspaceRestoring;
    private long workspaceEpoch;
    private bool workspaceEndRequested;
    public void BeginWorkspaceRestore() { workspaceEpoch++; workspaceRestoring = true; SuspendNavigation(true); }
    public void EndWorkspaceRestore()
    {
        workspaceEpoch++; workspaceRestoring = false;
        if (busy) { workspaceEndRequested = true; return; }
        CancelWorkspacePrepared(); SuspendNavigation(false);
    }
    public bool RestoreWorkspaceMinimized(InspectedWindow entry)
    {
        if (!IsRunning || busy || recovery is null || entry.Id is not { } id || entry.Observation.Identity is not { } identity
            || entry.Admission.Reason != WindowExclusionReason.Minimized || entry.Observation.IsTopMost || entry.Observation.IsMaximized) return false;
        busy = true;
        var epoch = workspaceEpoch;
        try
        {
            var recoveryId = managed.TryGetValue(id, out var owned) && owned.Identity == identity ? owned.RecoveryId : recovery.Register(identity).Id;
            if (owned is null) workspacePrepared[identity] = recoveryId;
            ThrowIfClosing(); if (!workspaceRestoring || epoch != workspaceEpoch) return false;
            return motion.RequestRestore(recovery, recoveryId);
        }
        catch (Exception error) when (IsExpected(error)) { Message = "无法登记或还原最小化窗口。"; return false; }
        finally { ExitBusy(); }
    }
    public void CancelWorkspacePrepared()
    {
        if (busy || recovery is null) return;
        foreach (var pair in workspacePrepared.ToArray())
        {
            try
            {
                if (!recovery.RestoreEntry(pair.Value).IsTerminal) { PauseInternal("工作区取消后仍有窗口待恢复，请检查恢复中心。"); break; }
                workspacePrepared.Remove(pair.Key);
            }
            catch (Exception error) when (IsExpected(error)) { PauseInternal("工作区取消恢复失败；记录已保留。"); break; }
        }
        workspacePrepared.Clear();
    }
    public bool ApplyWorkspaceMember(InspectedWindow entry, RectD worldBounds, DateTimeOffset now)
    {
        if (!IsRunning || busy || recovery is null || !workspaceRestoring || entry.Id is not { } id || entry.Observation.Identity is not { } identity
            || Unsupported(entry, layout) is not null || !planner.TryPlan(worldBounds, Workspace.Camera.Position, out var plan)) return false;
        busy = true;
        var epoch = workspaceEpoch;
        try
        {
            var actualBounds = entry.Observation.Bounds!.Value;
            var actual = new PixelRect(checked((int)actualBounds.Position.X), checked((int)actualBounds.Position.Y), checked((int)actualBounds.Right), checked((int)actualBounds.Bottom));
            if (!managed.TryGetValue(id, out var state))
            {
                var original = recovery.Register(identity); // The same durable original survives a minimized -> normal handoff.
                workspacePrepared[identity] = original.Id;
                ThrowIfClosing(); if (!workspaceRestoring || epoch != workspaceEpoch) return false;
                state = new(id, identity, original.Id, actual);
            }
            if (state.Identity != identity || !recovery.CanMove(state.RecoveryId)) return false;
            ThrowIfClosing(); if (!workspaceRestoring || epoch != workspaceEpoch) return false;
            state.Suspended = false; state.Observed = actual; state.Desired = plan!.PhysicalBounds; state.Parked = plan.IsParked;
            state.Expected.Clear(); state.Expected.Add(actual); state.Expected.Add(plan.PhysicalBounds);
            state.Deadline = actual == plan.PhysicalBounds ? null : now.AddMilliseconds(1500);
            if (state.Deadline is not null && motion.RequestBatch(recovery, [new(state.RecoveryId, plan.PhysicalBounds, Resize: true)]) is { Accepted: false })
            { PauseInternal("工作区位置或尺寸被拒绝，已暂停恢复。"); return false; }
            ThrowIfClosing();
            if (!workspaceRestoring || epoch != workspaceEpoch) { PauseInternal("工作区提交期间已取消，已暂停并恢复原位。"); return false; }
            managed[id] = state; workspacePrepared.Remove(identity);
            Workspace.UpsertWindow(new(id, entry.Observation.Title, worldBounds));
            InvalidateArrangementFor(id); activation = cycleTarget = null;
            Message = "正在核验工作区窗口布局。";
            return true;
        }
        catch (Exception error) when (IsExpected(error)) { PauseInternal("工作区复原未完成，已暂停恢复。"); return false; }
        finally { ExitBusy(); }
    }
    public WorkspacePlacement CheckWorkspaceMember(Guid id, NativeWindowIdentity identity)
        => !IsRunning || !managed.TryGetValue(id, out var state) || state.Identity != identity ? WorkspacePlacement.Failed
            : state.Deadline is not null ? WorkspacePlacement.Pending : WorkspacePlacement.Confirmed;

    public RectD ProjectWorkspaceMember(LocationBookmark bookmark, WorkspaceMember member)
    {
        var scale = (layout.Primary.EffectiveDpi ?? 96) / (double)(bookmark.Layout?.Dpi ?? 96);
        var size = new SizeD(member.Bounds.Size.Width * scale, member.Bounds.Size.Height * scale);
        var delta = member.Bounds.Position - bookmark.CameraPosition;
        var position = bookmark.CameraPosition + new PointD(delta.X * scale, delta.Y * scale);
        var areas = layout.Monitors.Select(m => new RectD(bookmark.CameraPosition + m.WorkArea.Position - layout.CanvasWorkArea.Position, m.WorkArea.Size)).ToArray();
        var target = new RectD(position, size);
        if (areas.Any(a => target.Right > a.Position.X && target.Position.X < a.Right && target.Bottom > a.Position.Y && target.Position.Y < a.Bottom)) return target;
        var primary = layout.Primary.WorkArea;
        return new(bookmark.CameraPosition + primary.Position - layout.CanvasWorkArea.Position + new PointD(24, 24), size);
    }
}
