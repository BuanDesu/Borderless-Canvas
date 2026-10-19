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
        var content = map.PreviewCards(previewSources, this).FirstOrDefault(c => c.Id == selected.Id)?.ContentBounds;
        entranceContentEnd = content is { IsValid: true } r ? new(r.Position, r.Size) : end;
        try
        {
            entranceScene?.Dispose(); entranceScene = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(entranceScene, ClientRectangle);
        }
        catch (Exception error) when (error is OutOfMemoryException or ArgumentException or ExternalException)
        { entranceScene?.Dispose(); entranceScene = null; return; }
        // Freeze final layout while only one buffered surface and one DWM relationship animate.
        var mapBounds = map.Bounds; var stripBounds = workspaces.Bounds;
        map.Dock = DockStyle.None; workspaces.Dock = DockStyle.None;
        map.Bounds = mapBounds; workspaces.Bounds = stripBounds;
        map.Hide(); workspaces.Hide(); entering = true; entranceProgress = 0;
    }
    private void AdvanceEntrance()
    {
        if (closing || IsDisposed) { entranceTimer.Stop(); return; }
        entranceProgress = Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(entranceStarted).TotalMilliseconds / 240, 0, 1);
        if (entranceProgress >= 1) { CompleteEntrance(); return; }
        RefreshPreviews(); Invalidate();
    }
    private void CompleteEntrance()
    {
        entranceTimer.Stop(); entering = false;
        SuspendLayout();
        try { map.Dock = DockStyle.Fill; workspaces.Dock = DockStyle.Bottom; map.Show(); workspaces.Show(); }
        finally { ResumeLayout(true); }
        entranceBackground?.Dispose(); entranceBackground = null; entranceScene?.Dispose(); entranceScene = null;
        RefreshPreviews(); Invalidate(true); search.Focus();
    }
    private PreviewCard? EntrancePreviewCard()
    {
        if (entranceSource is null || entranceStart is not { } from || entranceContentEnd is not { } to) return null;
        var r = OverviewMotion.Between(from, to, entranceProgress);
        var bounds = new PixelRect((int)Math.Round(r.Position.X), (int)Math.Round(r.Position.Y), (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));
        return new(entranceSource.Id, entranceSource.Identity, bounds, null);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!entering || entranceStart is not { } from || entranceEnd is not { } to) return;
        var r = OverviewMotion.Between(from, to, entranceProgress);
        var rect = new RectangleF((float)r.Position.X, (float)r.Position.Y, (float)r.Size.Width, (float)r.Size.Height);
        ChromeTheme.Glass(e.Graphics, rect, ChromeTheme.Purple, true);
        if (entranceSource is null || previews?.States.GetValueOrDefault(entranceSource.Id) != PreviewState.Preview)
            TextRenderer.DrawText(e.Graphics, model.Windows.FirstOrDefault(w => w.Id == model.SelectedId)?.Title, Font, Rectangle.Round(rect), ChromeTheme.Text,
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
        if (!entering) PrepareEntrance(); entranceProgress = progress; Invalidate();
    }
    internal void EndSmokeEntrance() => CompleteEntrance();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);
}
