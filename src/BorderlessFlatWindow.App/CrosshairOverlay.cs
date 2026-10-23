using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class CrosshairOverlay : Form
{
    internal CrosshairOverlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Size = new(25, 25); BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Cursor = Cursors.Cross;
        DoubleBuffered = true; Text = "BFW 准星";
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; } }
    internal void Present(PointD center)
    { Location = new((int)center.X - 12, (int)center.Y - 12); if (!Visible) Show(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var border = new Pen(Color.FromArgb(230, 32, 30, 48), 3);
        using var light = new Pen(Color.White, 1);
        foreach (var stroke in new[] { border, light })
        { e.Graphics.DrawLine(stroke, 4, 12, 20, 12); e.Graphics.DrawLine(stroke, 12, 4, 12, 20); }
    }
}
