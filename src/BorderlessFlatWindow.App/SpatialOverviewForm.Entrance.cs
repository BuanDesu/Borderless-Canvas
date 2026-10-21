using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    private readonly bool animateEntrance;
    private Bitmap? entranceBackground, entranceScene;
    private bool entering;
    private double entranceProgress;
    private long entranceStarted;
    private RectD? entranceStart, entranceEnd, entranceContentEnd;
    private OverviewPreviewSource? entranceSource;
    private TransitionCover? transitionCover;
    private CompositionSurface? compositionSurface;
    private PreviewCard[]? animationCards;
    private bool exiting;
    private bool overviewShown, desiredOpen = true, reversibleClose, sceneDirty, reusedScene;
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); PerformLayout();
        if (scene is not null) { PrepareSceneEntrance(); return; }
        map.Refit();
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
                if (focusFrame is not null && entranceStart is { } from && EntrancePreviewCard(1)?.ContentBounds is { } to)
                {
                    compositionSurface.SetMovingImage(focusFrame, new((float)from.Position.X, (float)from.Position.Y, (float)from.Size.Width, (float)from.Size.Height),
                        new(to.Left, to.Top, to.Width, to.Height));
                    compositorFocus = true;
                }
                focusFrame?.Dispose(); focusFrame = null;
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
        sceneDirty = false;
    }
    private void AdvanceComposition(double progress)
    {
        if (closing || IsDisposed) { compositionSurface?.Stop(); return; }
        var frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        entranceProgress = progress;
        if (exiting)
        {
            AnimatePreviews();
            if (progress <= 0) { compositionSurface?.Stop(); closing = true; Close(); }
            return;
        }
        if (motionTimes.Count == 0) firstCallbackTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        motionTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(entranceStarted).TotalMilliseconds);
        if (entranceProgress >= 1) { CompleteEntrance(); return; }
        AnimatePreviews();
        motionSamples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds);
    }
    private void CompleteEntrance()
    {
        if (scene is not null)
        {
            scene.Seek(1); scene.ShowFocusImage(false); entranceProgress = 1; entering = false; animationCards = null;
            RefreshPreviews(); WriteMotionMetrics(); if (!showWithoutActivation) scene.FocusSearch(); return;
        }
        compositionSurface?.Seek(1); compositionSurface?.Stop(); entranceProgress = 1; entering = false; AnimatePreviews(); DwmFlush();
        animationCards = null;
        transitionCover?.Dispose(); transitionCover = null;
        if (compositionSurface is not null) compositionSurface.Visible = false;
        entranceScene?.Dispose(); entranceScene = null;
        WriteMotionMetrics();
        RefreshPreviews(); search.Focus();
    }
    private bool BeginExit()
    {
        if (scene is not null) return BeginSceneExit();
        if (exiting) return true;
        if (!Visible || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled == 0) return false;
        if (compositionSurface is null || CompositionSurface.RenderingTier == 0) return false;
        try
        {
            hints.Active = false; map.PauseTitles(true);
            var returnImage = compositorFocus && canConfirm && ConfirmedTarget is null && ConfirmedCameraPosition is null
                && entranceSource is { } source && previewSources.ContainsKey(source.Id);
            if (!entering && returnImage) returnImage = RefreshReturnImage();
            if (!entering && sceneDirty)
            {
                using var scene = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
                DrawToBitmap(scene, ClientRectangle); compositionSurface.ReplaceTarget(scene); sceneDirty = false;
            }
            animationCards ??= displayCards;
            entering = false; exiting = true;
            compositionSurface.ShowMovingImage(returnImage);
            compositionSurface.Visible = true; compositionSurface.BringToFront();
            AnimatePreviews();
            compositionSurface.Retarget(false, 180); return true;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or OutOfMemoryException)
        { compositionSurface?.Dispose(); compositionSurface = null; return false; }
    }
    internal void ToggleOverview(long capturedTimestamp = 0)
    {
        if (!canConfirm || model.Status != OverviewStatus.Open) return;
        requestTimestamp = capturedTimestamp > 0 ? capturedTimestamp : System.Diagnostics.Stopwatch.GetTimestamp();
        desiredOpen = !desiredOpen;
        if (!overviewShown) return;
        if (!desiredOpen) { reversibleClose = true; Close(); return; }
        if (scene is not null)
        {
            if (!exiting || !reversibleClose) return;
            exiting = false; entering = true; reusedScene = true;
            entranceStarted = System.Diagnostics.Stopwatch.GetTimestamp(); motionSamples.Clear(); motionTimes.Clear();
            scene.Retarget(true, 220); return;
        }
        if (!exiting || !reversibleClose || compositionSurface is null) return;
        exiting = false; entering = true; reusedScene = true;
        hints.Active = true; map.PauseTitles(false);
        compositionSurface.ShowMovingImage(compositorFocus && entranceSource is { } source && previewSources.ContainsKey(source.Id));
        entranceStarted = System.Diagnostics.Stopwatch.GetTimestamp(); motionSamples.Clear(); motionTimes.Clear();
        compositionSurface.Retarget(true, 220);
    }
    private bool RefreshReturnImage()
    {
        if (entranceSource is not { } source || (compositionSurface is null && scene is null) || previews?.States.GetValueOrDefault(source.Id) != PreviewState.Preview
            || displayCards.FirstOrDefault(card => card.Id == source.Id)?.ContentBounds is not { } area) return false;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var identity = new BorderlessFlatWindow.Application.Discovery.NativeWindowIdentity(Handle, (uint)Environment.ProcessId, process.StartTime.ToFileTimeUtc());
        var expected = new PixelRect(Left, Top, Right, Bottom);
        var crop = new PixelRect(Left + area.Left, Top + area.Top, Left + area.Right, Top + area.Bottom);
        if (!DesktopCropEligibility.IsUnobscuredRegion(identity, expected, crop)) return false;
        using var image = FrostedBackdrop.CaptureRaw(new(crop.Left, crop.Top, crop.Width, crop.Height));
        if (image is null || !DesktopCropEligibility.IsUnobscuredRegion(identity, expected, crop)) return false;
        if (scene is not null) scene.ReplaceFocusImage(image); else compositionSurface!.ReplaceMovingImage(image); return true;
    }
    private void AnimatePreviews()
    {
        if (scene is not null) { AnimateScenePreviews(); return; }
        if (animationCards is null || previews is null) return;
        var opacity = (byte)Math.Clamp((int)Math.Round(entranceProgress * 255), 0, 255);
        var selected = exiting && (ConfirmedTarget is not null || ConfirmedCameraPosition is not null) ? null : EntrancePreviewCard(entranceProgress);
        previews.Animate(animationCards.Where(card => selected is null || card.Id != selected.Id), opacity);
        if (selected is not null) previews.Animate([selected], IsTransitioning && compositionSurface?.HasMovingImage == true ? (byte)0 : (byte)255);
    }
    private PreviewCard? EntrancePreviewCard(double progress)
    {
        if (entranceSource is null || entranceStart is not { } from || entranceContentEnd is not { } to) return null;
        var r = new RectD(new(from.Position.X + (to.Position.X - from.Position.X) * progress, from.Position.Y + (to.Position.Y - from.Position.Y) * progress),
            new(from.Size.Width + (to.Size.Width - from.Size.Width) * progress, from.Size.Height + (to.Size.Height - from.Size.Height) * progress));
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
        if (entering && !char.IsControl(e.KeyChar)) { CompleteEntrance(); if (scene is not null) scene.AppendSearch(e.KeyChar.ToString()); else search.AppendText(e.KeyChar.ToString()); e.Handled = true; }
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
        if (!entering) PrepareEntrance(); entranceProgress = OverviewMotion.Ease(progress); AnimatePreviews(); transitionCover?.Refresh();
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
