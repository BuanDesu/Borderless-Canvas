using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    private readonly bool animateEntrance;
    private readonly System.Windows.Forms.Timer entranceTimer = new() { Interval = 16 };
    private Bitmap? entranceBackground, entranceScene;
    private bool entering;
    private double entranceProgress;
    private long entranceStarted;
    private RectD? entranceStart, entranceEnd, entranceContentEnd;
    private PreviewSource? entranceSource;
    private TransitionCover? transitionCover;
    private PreviewCard[]? animationCards;
    private bool exiting;
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); PerformLayout(); map.Refit();
        if (animateEntrance && SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled != 0) PrepareEntrance();
    }
    private void PrepareEntrance()
    {
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
        try
        {
            entranceScene?.Dispose(); entranceScene = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(entranceScene, ClientRectangle);
        }
        catch (Exception error) when (error is OutOfMemoryException or ArgumentException or ExternalException)
        { entranceScene?.Dispose(); entranceScene = null; return; }
        // Keep all child HWNDs and thumbnail relationships alive beneath one buffered cover.
        entering = true; entranceProgress = 0;
        transitionCover = new TransitionCover(this) { Bounds = ClientRectangle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(transitionCover); transitionCover.BringToFront();
        _ = transitionCover.Handle;
        RefreshPreviews();
        Update(); DwmFlush();
    }
    private void AdvanceEntrance()
    {
        if (closing || IsDisposed) { entranceTimer.Stop(); return; }
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(entranceStarted).TotalMilliseconds;
        if (exiting)
        {
            Opacity = Math.Max(.01, 1 - OverviewMotion.Ease(Math.Clamp(elapsed / 120, 0, 1)));
            previews?.Animate(displayCards, (byte)Math.Round(Opacity * 255));
            if (elapsed >= 120) { entranceTimer.Stop(); closing = true; Close(); }
            return;
        }
        entranceProgress = Math.Clamp(elapsed / 220, 0, 1);
        if (entranceProgress >= 1) { CompleteEntrance(); return; }
        AnimatePreviews(); transitionCover?.Invalidate();
    }
    private void CompleteEntrance()
    {
        entranceTimer.Stop(); entranceProgress = 1; AnimatePreviews(); DwmFlush(); entering = false;
        animationCards = null;
        transitionCover?.Dispose(); transitionCover = null;
        entranceBackground?.Dispose(); entranceBackground = null; entranceScene?.Dispose(); entranceScene = null;
        RefreshPreviews(); search.Focus();
    }
    private bool BeginExit()
    {
        if (exiting) return true;
        if (!Visible || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled == 0) return false;
        if (entering) CompleteEntrance();
        exiting = true; entranceStarted = System.Diagnostics.Stopwatch.GetTimestamp(); entranceTimer.Start(); return true;
    }
    private void AnimatePreviews()
    {
        if (animationCards is null || previews is null) return;
        var opacity = (byte)Math.Clamp((int)Math.Round(OverviewMotion.Ease(entranceProgress) * 255), 0, 255);
        var selected = EntrancePreviewCard();
        previews.Animate(animationCards.Where(card => selected is null || card.Id != selected.Id), opacity);
        if (selected is not null) previews.Animate([selected]);
    }
    private PreviewCard? EntrancePreviewCard()
    {
        if (entranceSource is null || entranceStart is not { } from || entranceContentEnd is not { } to) return null;
        var r = OverviewMotion.Between(from, to, entranceProgress);
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
