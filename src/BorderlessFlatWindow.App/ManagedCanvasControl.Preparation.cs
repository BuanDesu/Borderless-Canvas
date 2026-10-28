using System.Diagnostics;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private const int PreparationIdleMilliseconds = 350;
    private readonly System.Windows.Forms.Timer preparationTimer = new() { Interval = PreparationIdleMilliseconds };
    private readonly OverviewPreparationSlot<IOverviewPresenter> preparedOverview = new();
    private OverviewPreparationSnapshot? failedPreparation, settlingPreparation;
    private bool preparingOverviewHost, preparationDisposed;
    private long lastCanvasInput;

    private bool CanPrepareOverview => !preparationDisposed && !IsDisposed && !Disposing && IsHandleCreated && !reading && !editingArrangement && !editingSettings && !panelActionPending && !desktopTransitionPending
        && !WorkspaceRestoring && !workspaceStarting && !workspaceOperation && !preparingOverviewHost && !overviewRequested && !openingOverview && overview is null && touchNavigation is null
        && session is { IsRunning: true, IsBusy: false, HasPending: false } && layout?.SupportsCanvas == true
        && gestures?.HasGesture != true && !moreMenu.Visible
        && FindForm() is MainForm { AllowsOverviewPreparation: true } host
        && !host.OwnedForms.Any(form => form.Visible && form.Modal);

    private OverviewPreparationSnapshot PreparationSnapshot(ManagedCanvasSession owner, MainForm host)
        => new(owner, owner.Workspace.Camera, OverviewWindows(), layout!.Monitors, bookmarks.Collection.Items,
            bookmarks.CanWrite, bookmarks.Message, ChromeTheme.Candy.Select(c => c.ToArgb()),
            overviewAnimation.Checked, host.OverviewShowsInTaskbar);

    private IOverviewPresenter CreateOverview(OverviewPreparationSnapshot snapshot)
    {
        var display = new DisplayLayout(snapshot.Monitors); var area = display.Primary.WorkArea;
        var viewports = snapshot.Monitors.Select(m => new RectD(snapshot.Camera.Position + m.WorkArea.Position - display.CanvasWorkArea.Position, m.WorkArea.Size)).ToArray();
        IOverviewPresenter result = OverviewRuntimeOptions.UseNative
            ? new NativeOverviewPresenter(snapshot.Windows, snapshot.Camera, area, bookmarks, viewports, snapshot.Animate, snapshot.ShowInTaskbar)
            : new FormOverviewPresenter(new(snapshot.Windows, snapshot.Camera, new(area.Left, area.Top, area.Width, area.Height),
            bookmarks: bookmarks, viewports: snapshot.Monitors.Select(m => new RectD(
                snapshot.Camera.Position + m.WorkArea.Position - display.CanvasWorkArea.Position, m.WorkArea.Size)),
            animate: snapshot.Animate) { ShowInTaskbar = snapshot.ShowInTaskbar });
        result.SetWorkspaceActions(WorkspaceCommands()); return result;
    }

    private void ScheduleOverviewPreparation()
    {
        if (preparationDisposed) return;
        // Keep an already prepared resource until OpenOverview can validate and take
        // it. A request during handle creation is deferred by ScheduleOverview.
        if (overviewRequested || openingOverview || preparingOverviewHost) { preparationTimer.Stop(); return; }
        if (!CanPrepareOverview) { InvalidateOverviewPreparation(); return; }
        if (!preparationTimer.Enabled)
        {
            settlingPreparation = PreparationSnapshot(session!, (MainForm)FindForm()!);
            preparationTimer.Start();
        }
    }

    private void PrepareOverviewWhileIdle()
    {
        preparationTimer.Stop();
        if (!CanPrepareOverview) { InvalidateOverviewPreparation(); return; }
        if (lastCanvasInput > 0 && Stopwatch.GetElapsedTime(lastCanvasInput).TotalMilliseconds < PreparationIdleMilliseconds)
        { preparationTimer.Start(); return; }
        var owner = session!; var host = (MainForm)FindForm()!;
        var key = PreparationSnapshot(owner, host);
        if (preparedOverview.Contains(key) || key.Matches(failedPreparation)) return;
        // Application-driven drags/title changes do not pass through our canvas
        // gesture router. Require the actual model to settle across the idle gap.
        if (!key.Matches(settlingPreparation))
        { settlingPreparation = key; preparationTimer.Start(); return; }
        settlingPreparation = null;
        preparingOverviewHost = true;
        IOverviewPresenter? candidate = null;
        try
        {
            var ticket = preparedOverview.Begin(); var started = Stopwatch.GetTimestamp();
            candidate = CreateOverview(key);
            candidate.PrepareHiddenHost();
            candidate.IdlePreparationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // HWND/WPF creation may pump UI messages. Never publish after pause,
            // disposal, display/model changes, or a request to show the overview.
            if (IsDisposed || Disposing || !host.AllowsOverviewPreparation || !ReferenceEquals(owner, session) || !owner.IsRunning || overviewRequested
                || openingOverview || overview is not null || reading || owner.IsBusy || owner.HasPending
                || !key.Matches(PreparationSnapshot(owner, host))) return;
            var resource = candidate; candidate = null;
            preparedOverview.Publish(ticket, key, resource);
            failedPreparation = null;
        }
        catch (Exception error) when (Expected(error))
        {
            // Speculative work cannot prevent a normal open. Retry only after an
            // input changes; a stable failure must not create a recurring idle loop.
            failedPreparation = key;
        }
        finally
        {
            candidate?.Dispose(); preparingOverviewHost = false;
            if (!IsDisposed && !Disposing) { ScheduleOverview(); StatusChanged?.Invoke(); }
        }
    }

    private void InvalidateOverviewPreparation()
    {
        if (preparationDisposed) return;
        preparationTimer.Stop(); preparedOverview.Invalidate(); failedPreparation = settlingPreparation = null;
    }
    private void DisposeOverviewPreparation()
    {
        if (preparationDisposed) return;
        // Cancelling a visible overview can synchronously reenter RefreshState
        // before Control.Dispose has set Disposing. Do not restart a dead timer.
        preparationDisposed = true; preparationTimer.Dispose(); preparedOverview.Dispose(); failedPreparation = settlingPreparation = null;
    }
}
