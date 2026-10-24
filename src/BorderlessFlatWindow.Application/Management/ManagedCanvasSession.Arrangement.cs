using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

public sealed partial class ManagedCanvasSession
{
    private CanvasArrangementPlan? lastArrangement;
    private object arrangementOwner = new();
    public bool CanUndoArrangement => IsRunning && !busy && !HasPending && !NavigationSuspended && lastArrangement is { } plan
        && CanvasArrangementPlan.Matches(plan.Targets, Workspace.Windows, exactSet: false);

    public CanvasArrangementPlan? PreviewArrangement(IReadOnlyCollection<Guid> selection, RectD region)
    {
        if (!IsRunning || busy || HasPending) { Message = "请先开始画布，并等待当前移动完成。"; return null; }
        var source = Workspace.Windows;
        if (selection.Any(id => !managed.TryGetValue(id, out var state) || state.Suspended) || !RegionTiling.TryPlan(source, selection, region, out var targets))
        { Message = "请选择有效窗口，或调整整理起点后重试。"; return null; }
        if (targets.Any(w => !planner.TryPlan(w.Bounds, Workspace.Camera.Position, out _)))
        { Message = "整理位置无法安全投影；窗口未移动。"; return null; }
        return new(arrangementOwner, source, targets, region);
    }

    public bool ApplyArrangement(CanvasArrangementPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsRunning || busy || HasPending || NavigationSuspended) { Message = "请等待当前移动或总览结束后再整理。"; return false; }
        if (!ReferenceEquals(plan.Owner, arrangementOwner) || !CanvasArrangementPlan.Matches(plan.Source, Workspace.Windows, exactSet: true))
        { Message = "预览后窗口布局已经改变，请重新预览整理。"; return false; }
        if (plan.ChangedCount == 0) { Message = "所选窗口已经位于该整理布局。"; return false; }
        return MoveArrangement(plan.Targets, now, plan);
    }

    public bool UndoArrangement(DateTimeOffset now)
    {
        if (!CanUndoArrangement) { Message = "没有可撤销的整理，或相关窗口已经改变。"; return false; }
        return MoveArrangement(lastArrangement!.Before, now, null);
    }

    private bool MoveArrangement(IReadOnlyList<CanvasWindow> targets, DateTimeOffset now, CanvasArrangementPlan? undo)
    {
        // Preflight every target before altering desired state or submitting any native movement.
        var plans = new Dictionary<Guid, WindowPlacementPlan>();
        foreach (var window in targets)
        {
            if (!managed.ContainsKey(window.Id) || !planner.TryPlan(window.Bounds, Workspace.Camera.Position, out var plan))
            { Message = "目标位置无法安全投影；窗口未移动。"; return false; }
            plans.Add(window.Id, plan!);
        }
        busy = true;
        try
        {
            activation = cycleTarget = null;
            var translations = new List<WindowTranslation>();
            foreach (var (id, plan) in plans)
            {
                var state = managed[id]; state.Parked = plan.IsParked;
                if (state.Observed != plan.PhysicalBounds)
                {
                    state.Expected.Add(state.Observed); state.Expected.Add(plan.PhysicalBounds);
                    state.Deadline = now.AddMilliseconds(1500);
                    translations.Add(new(state.RecoveryId, plan.PhysicalBounds));
                }
                state.Desired = plan.PhysicalBounds;
            }
            if (translations.Count > 0 && motion.RequestBatch(recovery!, translations) is { Accepted: false } failure)
            { PauseInternal($"整理移动被拒绝，已暂停恢复：{failure.Detail}"); return false; }
            ThrowIfClosing();
            // Publish only after submission succeeds. Partial rejection leaves the prior logical layout intact.
            foreach (var target in targets)
            {
                var current = Workspace.Windows.Single(w => w.Id == target.Id);
                Workspace.UpsertWindow(current with { Bounds = target.Bounds });
            }
            lastArrangement = undo;
            Message = undo is null ? "已撤销上次整理，恢复整理前的位置。" : $"已整理 {targets.Count} 个窗口；可在更多菜单撤销。";
            return true;
        }
        catch (Exception exception) when (IsExpected(exception)) { PauseInternal($"整理失败，已暂停恢复：{exception.Message}"); return false; }
        finally { ExitBusy(); }
    }
    private void InvalidateArrangementFor(Guid id)
    { if (lastArrangement?.Targets.Any(w => w.Id == id) == true) lastArrangement = null; }
}
