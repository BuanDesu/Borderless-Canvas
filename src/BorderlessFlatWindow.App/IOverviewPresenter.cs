using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed record OverviewPreviewSource(Guid Id, NativeWindowIdentity Identity, bool FullyOnscreen, RectD? ScreenBounds = null);

/// <summary>Presentation boundary; every handle/visibility value describes the actual top-level surface.</summary>
internal interface IOverviewPresenter : IDisposable
{
    event EventHandler? Shown;
    event EventHandler? Activated;
    event EventHandler? Closed;
    nint Handle { get; }
    bool Visible { get; }
    bool IsDisposed { get; }
    bool IsTransitioning { get; }
    bool WantsPresentation { get; }
    bool RequiresDesktopSnapshot { get; }
    bool ShowInTaskbar { get; set; }
    double IdlePreparationMilliseconds { get; set; }
    CanvasWindow? ConfirmedTarget { get; }
    PointD? ConfirmedCameraPosition { get; }
    double PrepareHiddenHost();
    void SetInitialSelection(Guid? selected);
    void SetPreviewSources(IEnumerable<OverviewPreviewSource> sources);
    void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message);
    void SetOpenTimings(long requested, double reconcile, double construction, double dispatch = 0, double queue = 0);
    void SetHostTimings(double hide, double show, double? desktopFlush = null);
    void PreparePresentation();
    void Show(bool activate);
    void ToggleOverview(long capturedTimestamp = 0);
    void CancelOverview();
    bool HandleCommand(Keys keys);
}

internal static class OverviewRuntimeOptions
{
    internal static bool UseNative;
    internal static bool ForceSolid;
    internal static string? MetricsPath;
}

internal sealed class FormOverviewPresenter : IOverviewPresenter
{
    private readonly SpatialOverviewForm form;
    public FormOverviewPresenter(SpatialOverviewForm form)
    {
        this.form = form;
        form.Shown += (_, _) => Shown?.Invoke(this, EventArgs.Empty);
        form.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        form.FormClosed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? Shown;
    public event EventHandler? Activated;
    public event EventHandler? Closed;
    public nint Handle => form.IsDisposed ? 0 : form.Handle;
    public bool Visible => form.Visible;
    public bool IsDisposed => form.IsDisposed;
    public bool IsTransitioning => form.IsTransitioning;
    public bool WantsPresentation => form.WantsPresentation;
    public bool RequiresDesktopSnapshot => form.RequiresDesktopSnapshot;
    public bool ShowInTaskbar { get => form.ShowInTaskbar; set => form.ShowInTaskbar = value; }
    public double IdlePreparationMilliseconds { get => form.IdlePreparationMilliseconds; set => form.IdlePreparationMilliseconds = value; }
    public CanvasWindow? ConfirmedTarget => form.ConfirmedTarget;
    public PointD? ConfirmedCameraPosition => form.ConfirmedCameraPosition;
    public double PrepareHiddenHost() => form.PrepareHiddenHost();
    public void SetInitialSelection(Guid? selected) => form.SetInitialSelection(selected);
    public void SetPreviewSources(IEnumerable<OverviewPreviewSource> sources) => form.SetPreviewSources(sources);
    public void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message) => form.UpdateWindows(windows, running, message);
    public void SetOpenTimings(long requested, double reconcile, double construction, double dispatch = 0, double queue = 0) => form.SetOpenTimings(requested, reconcile, construction, dispatch, queue);
    public void SetHostTimings(double hide, double show, double? desktopFlush = null) => form.SetHostTimings(hide, show, desktopFlush);
    public void PreparePresentation() => form.CaptureBackdrop();
    public void Show(bool activate) { form.Show(); if (activate && !form.IsDisposed && form.Visible) form.Activate(); }
    public void ToggleOverview(long capturedTimestamp = 0) => form.ToggleOverview(capturedTimestamp);
    public void CancelOverview() => form.CancelOverview();
    public bool HandleCommand(Keys keys) => form.HandleCommand(keys);
    public void Dispose() { form.Dispose(); Shown = null; Activated = null; Closed = null; }
}
