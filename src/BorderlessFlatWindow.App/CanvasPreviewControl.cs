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

    protected override void OnResize(EventArgs e) { base.OnResize(e); projection = null; }
    private static readonly Color[] Palette =
        [Color.FromArgb(47, 90, 115), Color.FromArgb(67, 78, 122), Color.FromArgb(94, 76, 112), Color.FromArgb(49, 101, 90)];

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
            new SizeD(Width, Height), padding: 48);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(120, 170, 195));
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var rect = DrawingRect(projection.ToMap(window.Bounds));
            using var fill = new SolidBrush(Palette[index % Palette.Length]);
            e.Graphics.FillRectangle(fill, rect);
            e.Graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            if (window.Id == ActiveWindowId)
            {
                using var active = new Pen(Color.Gold, 3);
                e.Graphics.DrawRectangle(active, rect.X, rect.Y, rect.Width, rect.Height);
            }
            TextRenderer.DrawText(e.Graphics, window.Title, Font,
                Rectangle.Round(rect), Color.White, TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        var viewport = DrawingRect(projection.ToMap(workspace.Camera.Bounds));
        using var viewportPen = new Pen(Color.FromArgb(111, 231, 196), 3) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawRectangle(viewportPen, viewport.X, viewport.Y, viewport.Width, viewport.Height);
        var origin = projection.ToMap(PointD.Zero);
        using var originPen = new Pen(Color.Gold, 2);
        e.Graphics.DrawLine(originPen, (float)origin.X - 7, (float)origin.Y, (float)origin.X + 7, (float)origin.Y);
        e.Graphics.DrawLine(originPen, (float)origin.X, (float)origin.Y - 7, (float)origin.X, (float)origin.Y + 7);
        TextRenderer.DrawText(e.Graphics, "虚线：当前视口    十字：画布原点", Font,
            new Point(12, 10), Color.FromArgb(190, 210, 220));
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
}
