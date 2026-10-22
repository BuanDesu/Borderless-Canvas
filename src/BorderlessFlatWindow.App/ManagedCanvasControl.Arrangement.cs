using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private bool editingArrangement;
    private readonly ToolStripMenuItem arrangeWindows = new("平铺窗口…");
    private readonly ToolStripMenuItem undoArrangement = new("撤销上次整理");
    private bool CanArrange => !reading && !editingArrangement && !openingOverview && !overviewRequested && overview is null
        && session is { IsRunning: true, IsBusy: false, HasPending: false };
    private void InitializeArrangementMenu()
    {
        var menu = new ToolStripMenuItem("整理");
        menu.DropDownItems.AddRange([arrangeWindows, undoArrangement]); ChromeTheme.StyleMenu(menu.DropDown); moreMenu.Items.Add(menu);
        moreMenu.Opening += (_, _) => { arrangeWindows.Enabled = CanArrange; undoArrangement.Enabled = CanArrange && session!.CanUndoArrangement; };
        arrangeWindows.Click += (_, _) => EditArrangement(); undoArrangement.Click += (_, _) => EditArrangement(undo: true);
    }
    private void EditArrangement(bool undo = false)
    {
        if (!CanArrange || layout is null) return;
        var owner = session!;
        try
        {
            EndNavigationForEditing(); InvalidateOverviewPreparation(); editingArrangement = true; RefreshState(); reading = true;
            owner.SuspendNavigation(true);
            var snapshot = RefreshManagedSnapshot(owner); owner.Observe(snapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            if (!owner.IsRunning || owner.HasPending) { notice = owner.Message; return; }
            if (undo)
            {
                owner.SuspendNavigation(false); owner.UndoArrangement(DateTimeOffset.UtcNow); notice = owner.Message; return;
            }
            var source = owner.Workspace.Windows;
            var viewports = layout.Monitors.Select(m => new RectD(owner.Workspace.Camera.Position + m.WorkArea.Position - layout.CanvasWorkArea.Position, m.WorkArea.Size)).ToArray();
            var region = new RectD(owner.Workspace.Camera.Position + layout.Primary.WorkArea.Position - layout.CanvasWorkArea.Position, layout.Primary.WorkArea.Size);
            using var dialog = new CanvasArrangementDialog(source, region, viewports);
            if (dialog.ShowFor(this) != DialogResult.OK) return;
            if (!ReferenceEquals(owner, session) || !owner.IsRunning || source.Count != owner.Workspace.Windows.Count
                || source.Any(w => !owner.Workspace.Windows.Any(current => current.Id == w.Id && current.Bounds == w.Bounds)))
            { notice = "窗口布局已经改变，请重新预览整理。"; return; }
            var plan = owner.PreviewArrangement(dialog.SelectedIds, region);
            if (plan is null) { notice = owner.Message; return; }
            // The modal preview never moves windows. Revalidate all managed sources before committing its frozen plan.
            snapshot = RefreshManagedSnapshot(owner); owner.Observe(snapshot, WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            owner.SuspendNavigation(false);
            owner.ApplyArrangement(plan, DateTimeOffset.UtcNow); notice = owner.Message;
        }
        catch (Exception error) when (Expected(error)) { owner.Pause($"整理未完成，已暂停恢复：{error.Message}"); notice = owner.Message; }
        finally
        {
            owner.SuspendNavigation(false); reading = false; editingArrangement = false;
            // Navigation queued before editing is superseded; faults still require recovery.
            if (!IsDisposed && !Disposing)
            {
                if (globalInput?.Drain().Any(e => e.Input.Kind == NavigationInputKind.Fault) == true) owner.Pause("编辑期间导航异常，已暂停恢复。");
                RefreshState();
            }
        }
    }
}
