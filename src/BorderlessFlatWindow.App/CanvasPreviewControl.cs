using System.Drawing.Drawing2D;
using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class CanvasPreviewControl : Control
{
    private readonly CanvasWorkspace workspace;
    private OverviewProjection? projection;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool AutoFit { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Guid? ActiveWindowId { get; set; }
    internal void FitToContent() { projection = null; Invalidate(); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e); projection = null;
        if (Width > 0 && Height > 0)
        {
            using var shape = ChromeTheme.Round(ClientRectangle, 24);
            var old = Region; Region = new Region(shape); old?.Dispose();
        }
    }

    internal CanvasPreviewControl(CanvasWorkspace workspace)
    {
        this.workspace = workspace;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(20, 25, 35);
    }

    internal event Action<Guid>? WindowChosen;
    internal event Action<PointD>? CameraPanRequested;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 64 || Height <= 64)
            return;
        ChromeTheme.Grid(e.Graphics, ClientRectangle);
        var windows = DrawWindows();
        if (AutoFit || projection is null) projection = OverviewProjection.Fit(windows.Select(window => window.Bounds)
            .Append(workspace.Camera.Bounds).Append(new RectD(PointD.Zero, new SizeD(1, 1))),
            new SizeD(Width, Height), padding: Math.Min(48, Math.Min(Width, Height) / 4d));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var rect = DrawingRect(projection.ToMap(window.Bounds));
            if (rect.Width < 2 || rect.Height < 2) continue;
            ChromeTheme.Glass(e.Graphics, rect, ChromeTheme.Candy[index % ChromeTheme.Candy.Length], window.Id == ActiveWindowId);
            TextRenderer.DrawText(e.Graphics, window.Title, Font,
                Rectangle.Round(rect), ChromeTheme.Text, TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        if (windows.Count > 0)
        {
            var viewport = DrawingRect(projection.ToMap(workspace.Camera.Bounds));
            using var viewportPen = new Pen(Color.FromArgb(75, ChromeTheme.Purple), 1) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(viewportPen, viewport.X, viewport.Y, viewport.Width, viewport.Height);
            var origin = projection.ToMap(PointD.Zero);
            using var originPen = new Pen(Color.FromArgb(95, ChromeTheme.Purple), 1);
            e.Graphics.DrawLine(originPen, (float)origin.X - 7, (float)origin.Y, (float)origin.X + 7, (float)origin.Y);
            e.Graphics.DrawLine(originPen, (float)origin.X, (float)origin.Y - 7, (float)origin.X, (float)origin.Y + 7);
        }
        if (windows.Count == 0)
        {
            var center = new Point(Width / 2, Height / 2);
            if (Height >= 140) ChromeTheme.Mark(e.Graphics, new(center.X - 28, center.Y - 56, 56, 56));
            TextRenderer.DrawText(e.Graphics, "为窗口留一点自由", Font, new Rectangle(0, Height >= 140 ? center.Y + 16 : center.Y - 18, Width, 36), ChromeTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || projection is null)
            return;
        var world = projection.ToWorld(new PointD(e.X, e.Y));
        var hit = DrawWindows().LastOrDefault(window => window.Bounds.Contains(world));
        if (hit is not null)
            WindowChosen?.Invoke(hit.Id);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var delta = -e.Delta;
        CameraPanRequested?.Invoke((ModifierKeys & Keys.Shift) != 0 ? new(delta, 0) : new(0, delta));
    }

    private static RectangleF DrawingRect(RectD rect) => new((float)rect.Position.X, (float)rect.Position.Y,
        (float)rect.Size.Width, (float)rect.Size.Height);

    private IReadOnlyList<CanvasWindow> DrawWindows() =>
        workspace.Windows.OrderBy(window => window.Id == ActiveWindowId).ToArray();

    internal static void CheckCompactSurfaces()
    {
        var workspace = new CanvasWorkspace(new(1280, 720));
        foreach (var populated in new[] { false, true })
        {
            if (populated) workspace.UpsertWindow(new(Guid.NewGuid(), "Synthetic compact map", new(new(-500, 30), new(640, 480))));
            foreach (var size in new[] { new Size(65, 65), new(96, 72), new(72, 96), new(432, 80), new(80, 432), new(432, 64), new(432, 300) })
            {
                using var control = new CanvasPreviewControl(workspace) { Size = size };
                using var bitmap = new Bitmap(size.Width, size.Height); using var graphics = Graphics.FromImage(bitmap);
                // Direct paint keeps a regression exception on the test call stack instead of opening a framework error dialog.
                control.OnPaint(new PaintEventArgs(graphics, control.ClientRectangle));
                if (size.Width > 64 && size.Height > 64 && (control.projection is null || !double.IsFinite(control.projection.Scale)))
                    throw new InvalidOperationException("Compact map projection is unavailable.");
            }
        }
        Console.WriteLine("PASS Compact empty/populated maps preserve a valid uniform projection with adaptive padding");
    }
}
