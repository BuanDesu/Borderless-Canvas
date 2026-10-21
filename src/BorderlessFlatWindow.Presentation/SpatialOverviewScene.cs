using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Presentation;

/// <summary>WinForms adapter. The complete scene is also usable by a native WPF top-level host.</summary>
public sealed class SpatialOverviewScene : ElementHost
{
    private readonly SpatialOverviewView view;
    private bool released;
    public SpatialOverviewScene(SpatialOverviewSession model, PointD camera, SizeD viewport, RectD[] monitorOffsets, Color[] colors, bool synthetic = false)
    { view = new(model, camera, viewport, monitorOffsets, colors, synthetic); TabStop = true; Child = view; }
    public event Action? GeometryChanged { add => view.GeometryChanged += value; remove => view.GeometryChanged -= value; }
    public event Action? ConfirmRequested { add => view.ConfirmRequested += value; remove => view.ConfirmRequested -= value; }
    public event Action<PointD>? WorkspaceRequested { add => view.WorkspaceRequested += value; remove => view.WorkspaceRequested -= value; }
    public event Action<Guid, Point>? WorkspaceMenuRequested { add => view.WorkspaceMenuRequested += value; remove => view.WorkspaceMenuRequested -= value; }
    public event Action? SaveRequested { add => view.SaveRequested += value; remove => view.SaveRequested -= value; }
    public event Action? ManageRequested { add => view.ManageRequested += value; remove => view.ManageRequested -= value; }
    public event Action? OptionsRequested { add => view.OptionsRequested += value; remove => view.OptionsRequested -= value; }
    public event Action? CancelRequested { add => view.CancelRequested += value; remove => view.CancelRequested -= value; }
    public event Func<Keys, bool>? Command { add => view.Command += value; remove => view.Command -= value; }
    public event Action<double>? Frame { add => view.Frame += value; remove => view.Frame -= value; }
    public double Position => view.Position;
    public bool HasFocusImage => view.HasFocusImage;
    public bool IsAnimating => view.IsAnimating;
    public void EnsureLayout() { if (!released) view.SetViewport(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), DeviceDpi / 96d); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (view is not null) EnsureLayout(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); EnsureLayout(); }
    public void RefreshWindows() => view.RefreshWindows();
    public void ShowViewports(bool value) => view.ShowViewports(value);
    public void SetStatus(string? text) => view.SetStatus(text);
    public void FocusSearch(bool selectAll = false) => view.FocusSearch(selectAll);
    public void AppendSearch(string text) => view.AppendSearch(text);
    public void SetQuery(string text) => view.SetQuery(text);
    public byte[] RenderSyntheticPng() { EnsureLayout(); return view.RenderSyntheticPng(); }
    public void SetWorkspaces(IEnumerable<SceneWorkspace> workspaces) => view.SetWorkspaces(workspaces);
    public void SetWallpaper(Bitmap bitmap, RectD? fraction = null) => view.SetWallpaper(bitmap, fraction);
    public IReadOnlyList<ScenePreview> PreviewLayout() => view.PreviewLayout();
    public PixelRect? FocusDestination(Guid id) => view.FocusDestination(id);
    public void SetBackdrop(Bitmap bitmap) => view.SetBackdrop(bitmap);
    public void SetFocusImage(Bitmap bitmap, RectD from, PixelRect to) => view.SetFocusImage(bitmap, from, to);
    public void ReplaceFocusImage(Bitmap bitmap) => view.ReplaceFocusImage(bitmap);
    public void ShowFocusImage(bool value) => view.ShowFocusImage(value);
    public void Retarget(bool open, double milliseconds) => view.Retarget(open, milliseconds);
    public void Seek(double value) => view.Seek(value);
    public void StopMotion() => view.StopMotion();
    protected override void Dispose(bool disposing) { if (disposing && !released) { released = true; view.Dispose(); Child = null; } base.Dispose(disposing); }
    private static bool warmed;
    public static void WarmUp()
    {
        if (warmed) return; warmed = true;
        var camera = new Camera2D(PointD.Zero, new(800, 600));
        var model = new SpatialOverviewSession([new(Guid.NewGuid(), "", new(PointD.Zero, new(300, 200)))], camera, new(800, 600));
        using var scene = new SpatialOverviewScene(model, camera.Position, camera.ViewportSize, [new(PointD.Zero, camera.ViewportSize)],
            [Color.MediumPurple, Color.DeepSkyBlue, Color.HotPink, Color.Turquoise], synthetic: true) { Size = new(800, 600) };
        _ = scene.Handle; scene.SetWorkspaces([new(null, "", PointD.Zero)]); scene.EnsureLayout();
    }
}
