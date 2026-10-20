using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    private readonly bool animateEntrance;
    private Bitmap? entranceBackground, entranceScene;
    private bool entering;
    private double entranceProgress;
    private long entranceStarted;
    private RectD? entranceStart, entranceEnd, entranceContentEnd;
    private PreviewSource? entranceSource;
    private TransitionCover? transitionCover;
    private CompositionSurface? compositionSurface;
    private PreviewCard[]? animationCards;
    private bool exiting;
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); PerformLayout(); map.Refit();
        if (!showWithoutActivation) _ = workspaces.LoadWallpaperAsync();
        if (animateEntrance && CompositionSurface.RenderingTier > 0 && SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled != 0) PrepareEntrance();
    }
    private void PrepareEntrance()
    {
        var prepareStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var selected = model.Windows.FirstOrDefault(w => w.Id == model.SelectedId);
        if (selected is null) return;
        var end = model.Projection.ToMap(selected.Bounds);
        entranceEnd = end;
        entranceStart = new(new(end.Center.X - end.Size.Width * .65, end.Center.Y - end.Size.Height * .65), new(end.Size.Width * 1.3, end.Size.Height * 1.3));
        if (previewSources.TryGetValue(selected.Id, out var source) && source.FullyOnscreen && source.ScreenBounds is { } physical
            && physical.Position.X >= Bounds.Left && physical.Position.Y >= Bounds.Top && physical.Right <= Bounds.Right && physical.Bottom <= Bounds.Bottom)
        {
            entranceSource = source;
            entranceStart = new(physical.Position - new PointD(Bounds.Left, Bounds.Top), physical.Size);
        }
        _ = map.Handle;
        workspaces.CreateControl(); RefreshPreviews();
        var content = map.PreviewCards(previewSources, this).FirstOrDefault(c => c.Id == selected.Id)?.ContentBounds;
        entranceContentEnd = content is { IsValid: true } r ? new(r.Position, r.Size) : end;
        if (entranceBackground is not null && backdrop is not null && entranceSource is not null && entranceStart is { } initial)
        {
            var rect = Rectangle.FromLTRB((int)initial.Position.X, (int)initial.Position.Y, (int)initial.Right, (int)initial.Bottom);
            using var graphics = Graphics.FromImage(entranceBackground); graphics.DrawImage(backdrop, rect, rect, GraphicsUnit.Pixel);
        }
        var snapshotStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            entranceScene?.Dispose(); entranceScene = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            DrawToBitmap(entranceScene, ClientRectangle);
        }
        catch (Exception error) when (error is OutOfMemoryException or ArgumentException or ExternalException)
        { entranceScene?.Dispose(); entranceScene = null; return; }
        sceneSnapshotMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(snapshotStarted).TotalMilliseconds;
        // Keep child HWNDs and DWM relations alive. The compositor blends immutable textures;
        // the UI thread only submits thumbnail bounds/opacity on a composition frame callback.
        entering = true; entranceProgress = 0;
        if (!showWithoutActivation && entranceBackground is not null && CompositionSurface.RenderingTier > 0)
        {
            try
            {
                var uploadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                compositionSurface = new(entranceBackground, entranceScene) { Bounds = ClientRectangle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
                Controls.Add(compositionSurface); compositionSurface.BringToFront(); _ = compositionSurface.Handle;
                compositionSurface.Frame += AdvanceComposition;
                uploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;
            }
            catch (Exception error) when (error is ExternalException or InvalidOperationException or OutOfMemoryException)
            { compositionSurface?.Dispose(); compositionSurface = null; }
            if (compositionSurface is null) { entering = false; return; }
        }
        else if (showWithoutActivation)
        {
            transitionCover = new TransitionCover(this) { Bounds = ClientRectangle };
            Controls.Add(transitionCover); transitionCover.BringToFront(); _ = transitionCover.Handle;
        }
        else { entering = false; return; } // Reduced capability: open immediately, never animate in software.
        RefreshPreviews();
        animationPreviewCount = animationCards?.Length ?? 0;
        Update(); DwmFlush();
        prepareMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(prepareStarted).TotalMilliseconds;
    }
    private void AdvanceComposition(double progress)
    {
        if (closing || IsDisposed) { compositionSurface?.Stop(); return; }
        var frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (exiting)
        {
            var returning = ConfirmedTarget is null && ConfirmedCameraPosition is null ? EntrancePreviewCard(progress, reverse: true) : null;
            previews?.Animate(displayCards.Where(card => returning is null || card.Id != returning.Id), (byte)Math.Round((1 - OverviewMotion.Ease(progress)) * 255));
            if (returning is not null) previews?.Animate([returning]);
            if (progress >= 1) { compositionSurface?.Stop(); closing = true; Close(); }
            return;
        }
        entranceProgress = progress;
        motionTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(entranceStarted).TotalMilliseconds);
        if (entranceProgress >= 1) { CompleteEntrance(); return; }
        AnimatePreviews();
        motionSamples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds);
    }
    private void CompleteEntrance()
    {
        compositionSurface?.Seek(1); compositionSurface?.Stop(); entranceProgress = 1; AnimatePreviews(); DwmFlush(); entering = false;
        animationCards = null;
        transitionCover?.Dispose(); transitionCover = null;
        compositionSurface?.Dispose(); compositionSurface = null;
        entranceScene?.Dispose(); entranceScene = null;
        WriteMotionMetrics();
        RefreshPreviews(); search.Focus();
    }
    private bool BeginExit()
    {
        if (exiting) return true;
        if (!Visible || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled == 0) return false;
        if (entering) CompleteEntrance();
        if (entranceBackground is null || CompositionSurface.RenderingTier == 0) return false;
        try
        {
            using var scene = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            DrawToBitmap(scene, ClientRectangle);
            compositionSurface = new(entranceBackground, scene) { Bounds = ClientRectangle };
            Controls.Add(compositionSurface); compositionSurface.BringToFront(); _ = compositionSurface.Handle;
            compositionSurface.Frame += AdvanceComposition;
            exiting = true; compositionSurface.Start(140, reverse: true); return true;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or OutOfMemoryException)
        { compositionSurface?.Dispose(); compositionSurface = null; return false; }
    }
    private void AnimatePreviews()
    {
        if (animationCards is null || previews is null) return;
        var opacity = (byte)Math.Clamp((int)Math.Round(OverviewMotion.Ease(entranceProgress) * 255), 0, 255);
        var selected = EntrancePreviewCard(entranceProgress);
        previews.Animate(animationCards.Where(card => selected is null || card.Id != selected.Id), opacity);
        if (selected is not null) previews.Animate([selected]);
    }
    private PreviewCard? EntrancePreviewCard(double progress, bool reverse = false)
    {
        if (entranceSource is null || entranceStart is not { } from || entranceContentEnd is not { } to) return null;
        var r = reverse ? OverviewMotion.Between(to, from, progress) : OverviewMotion.Between(from, to, progress);
        var size = previews?.SourceSize(entranceSource.Id);
        var bounds = new PixelRect((int)Math.Round(r.Position.X), (int)Math.Round(r.Position.Y), (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));
        if (size is { IsPositive: true }) bounds = OverviewPreviewSession.Fit(size.Value, bounds);
        return new(entranceSource.Id, entranceSource.Identity, bounds, null);
    }
    private void PaintMovingFrame(Graphics graphics)
    {
        if (!entering || entranceStart is not { } from || entranceEnd is not { } to) return;
        var r = OverviewMotion.Between(from, to, entranceProgress);
        var rect = new RectangleF((float)r.Position.X, (float)r.Position.Y, (float)r.Size.Width, (float)r.Size.Height);
        ChromeTheme.Glass(graphics, rect, ChromeTheme.Purple, true);
        if (entranceSource is null || previews?.States.GetValueOrDefault(entranceSource.Id) != PreviewState.Preview)
            TextRenderer.DrawText(graphics, model.Windows.FirstOrDefault(w => w.Id == model.SelectedId)?.Title, Font, Rectangle.Round(rect), ChromeTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (entering && !char.IsControl(e.KeyChar)) { CompleteEntrance(); search.AppendText(e.KeyChar.ToString()); e.Handled = true; }
        base.OnKeyPress(e);
    }
    private static void DrawAlpha(Graphics graphics, Image image, Rectangle bounds, float opacity)
    {
        using var attributes = new ImageAttributes(); attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
        graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }
    internal void SetSmokeEntrance(double progress)
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Synthetic rendering only.");
        if (!entering) PrepareEntrance(); entranceProgress = progress; AnimatePreviews(); transitionCover?.Refresh();
    }
    internal void EndSmokeEntrance() => CompleteEntrance();
    private sealed class TransitionCover : Control
    {
        private readonly SpatialOverviewForm owner;
        internal TransitionCover(SpatialOverviewForm owner) { this.owner = owner; DoubleBuffered = true; SetStyle(ControlStyles.Selectable, false); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { owner.PaintScene(e.Graphics, this); owner.PaintMovingFrame(e.Graphics); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);
}
