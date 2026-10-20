using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

/// <summary>Short, interruptible page crossfades. Captures only this application's tab content.</summary>
internal sealed class TabPageTransition : IDisposable
{
    private readonly TabControl tabs;
    private CompositionSurface? cover;
    private Bitmap? from, to;
    private TabPage? requested;
    private bool committing, priming, disposed;
    internal bool IsActive { get; private set; }
    internal event Action? Finished;
    internal TabPageTransition(TabControl tabs)
    {
        this.tabs = tabs; tabs.Selecting += Selecting; tabs.SizeChanged += GeometryChanged;
        tabs.MouseDown += TabMouseDown;
        tabs.VisibleChanged += VisibilityChanged; tabs.HandleDestroyed += GeometryChanged;
    }
    private void TabMouseDown(object? sender, MouseEventArgs e)
    {
        if (!IsActive || !priming || e.Button != MouseButtons.Left || tabs.SelectedIndex < 0) return;
        if (tabs.GetTabRect(tabs.SelectedIndex).Contains(e.Location)) Finish(false);
    }
    private void Selecting(object? sender, TabControlCancelEventArgs e)
    {
        if (disposed || committing || !tabs.Visible || e.TabPage is null || e.TabPage == tabs.SelectedTab
            || CompositionSurface.RenderingTier == 0 || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled == 0) return;
        e.Cancel = true; requested = e.TabPage;
        if (priming) return;
        try
        {
            var snapshot = IsActive && from is not null && to is not null ? Blend(from, to, cover!.Position) : Capture(tabs.SelectedTab!);
            from?.Dispose(); to?.Dispose(); from = snapshot; to = null;
            if (cover is null)
            {
                cover = new(snapshot, snapshot); cover.Frame += Advance;
                tabs.Parent!.Controls.Add(cover);
            }
            else { cover.Stop(); cover.ReplaceBackground(snapshot); cover.ReplaceTarget(snapshot); cover.Seek(0); }
            var host = tabs.Parent!;
            cover.Bounds = new(host.PointToClient(tabs.PointToScreen(tabs.DisplayRectangle.Location)), tabs.DisplayRectangle.Size);
            cover.TabStop = false; cover.Visible = true; cover.BringToFront(); _ = cover.Handle;
            priming = IsActive = true;
            // Keep the old page visible until the covering texture has actually been presented.
            cover.Start(1000);
        }
        catch (Exception error) when (Expected(error)) { Finish(commit: true); }
    }
    private void Advance(double position)
    {
        if (disposed || cover is null) return;
        try
        {
            if (priming)
            {
                priming = false; cover.Stop();
                var target = requested; requested = null;
                if (target is null || target.IsDisposed) { Finish(false); return; }
                committing = true;
                try { tabs.SelectedTab = target; tabs.PerformLayout(); target.PerformLayout(); }
                finally { committing = false; }
                if (!IsActive || disposed || !tabs.Visible) return;
                to = Capture(target); cover.ReplaceTarget(to); cover.Start(140); return;
            }
            if (position >= 1) Finish(false);
        }
        catch (Exception error) when (Expected(error)) { Finish(true); }
    }
    private void Finish(bool commit)
    {
        cover?.Stop(); if (cover is not null) cover.Visible = false;
        IsActive = priming = false;
        if (commit && requested is { IsDisposed: false } target && !tabs.IsDisposed)
        {
            committing = true;
            try { tabs.SelectedTab = target; } finally { committing = false; }
        }
        requested = null; from?.Dispose(); from = null; to?.Dispose(); to = null;
        Finished?.Invoke();
    }
    private void GeometryChanged(object? sender, EventArgs e) { if (IsActive) Finish(true); }
    private void VisibilityChanged(object? sender, EventArgs e) { if (!tabs.Visible && IsActive) Finish(true); }
    private static Bitmap Capture(Control page)
    {
        var bitmap = new Bitmap(Math.Max(1, page.Width), Math.Max(1, page.Height), PixelFormat.Format32bppPArgb);
        try { page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
    private static Bitmap Blend(Bitmap a, Bitmap b, double position)
    {
        var bitmap = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap); g.DrawImageUnscaled(a, 0, 0);
        using var alpha = new ImageAttributes(); alpha.SetColorMatrix(new ColorMatrix { Matrix33 = (float)position });
        g.DrawImage(b, new Rectangle(Point.Empty, bitmap.Size), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, alpha);
        return bitmap;
    }
    private static bool Expected(Exception e) => e is ArgumentException or ExternalException or OutOfMemoryException or InvalidOperationException;
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        tabs.Selecting -= Selecting; tabs.SizeChanged -= GeometryChanged; tabs.VisibleChanged -= VisibilityChanged; tabs.HandleDestroyed -= GeometryChanged;
        tabs.MouseDown -= TabMouseDown;
        Finish(false); cover?.Dispose(); cover = null;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}
