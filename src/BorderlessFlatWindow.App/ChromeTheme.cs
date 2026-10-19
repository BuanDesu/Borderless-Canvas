using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
namespace BorderlessFlatWindow.App;

internal static class ChromeTheme
{
    private sealed class Surface { internal bool Hover, Pressed, Applied; internal Color? Accent; }
    private static readonly ConditionalWeakTable<Control, Surface> styled = new();
    internal static readonly Color Ink = Color.FromArgb(242, 245, 252), Panel = Color.FromArgb(248, 250, 255), Text = Color.FromArgb(37, 43, 66), Muted = Color.FromArgb(103, 111, 138);
    internal static readonly Color Cyan = Color.FromArgb(25, 160, 250), Pink = Color.FromArgb(255, 77, 151), Lime = Color.FromArgb(35, 187, 157), Purple = Color.FromArgb(114, 68, 239);
    internal static readonly Color[] Candy = [Purple, Cyan, Pink, Lime];
    internal static void Accent(Button button, Color color) => styled.GetOrCreateValue(button).Accent = color;
    internal static void Apply(Control root)
    {
        root.ForeColor = Text; root.BackColor = Ink;
        var surface = styled.GetOrCreateValue(root); var first = !surface.Applied; surface.Applied = true;
        switch (root)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                b.Padding = new(12, 7, 12, 7); b.MinimumSize = new(40, 42); b.Cursor = Cursors.Hand;
                if (first)
                {
                    b.Paint += DrawButton;
                    b.MouseEnter += (_, _) => { styled.GetOrCreateValue(b).Hover = true; b.Invalidate(); };
                    b.MouseLeave += (_, _) => { var s = styled.GetOrCreateValue(b); s.Hover = s.Pressed = false; b.Invalidate(); };
                    b.MouseDown += (_, _) => { styled.GetOrCreateValue(b).Pressed = true; b.Invalidate(); };
                    b.MouseUp += (_, _) => { styled.GetOrCreateValue(b).Pressed = false; b.Invalidate(); };
                }
                break;
            case Label label: label.BackColor = Color.Transparent; break;
            case TextBox box: box.BackColor = Color.White; box.BorderStyle = BorderStyle.FixedSingle; break;
            case ListView list: list.BackColor = Panel; list.BorderStyle = BorderStyle.None; break;
            case CheckedListBox list: list.BackColor = Panel; list.BorderStyle = BorderStyle.None; list.IntegralHeight = false; break;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.SizeMode = TabSizeMode.Fixed; tabs.ItemSize = new(128, 44); tabs.Padding = new(12, 8);
                if (first) tabs.DrawItem += DrawTab; break;
            case ToolStrip strip: StyleMenu(strip); break;
        }
        foreach (Control child in root.Controls) Apply(child);
    }
    internal static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new CandyMenuColors()); menu.BackColor = Panel; menu.ForeColor = Text;
        menu.Font = new Font("Microsoft YaHei UI", 9.5f); menu.Padding = new(6);
    }
    private sealed class CandyMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color MenuItemSelected => Color.FromArgb(231, 225, 255);
        public override Color MenuItemBorder => Color.FromArgb(213, 200, 255);
        public override Color MenuBorder => Color.FromArgb(217, 223, 239);
    }
    internal static GraphicsPath Round(RectangleF r, float radius = 14)
    {
        var p = new GraphicsPath(); var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    internal static void Glass(Graphics g, RectangleF r, Color accent, bool selected = false, bool pressed = false)
    {
        if (r.Width < 2 || r.Height < 2) return; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!pressed) for (var i = 5; i > 0; i--) { var s = r; s.Offset(0, i + 2); s.Inflate(i / 2f, i / 2f); using var path = Round(s); using var brush = new SolidBrush(Color.FromArgb(4 + (selected ? 2 : 0), accent)); g.FillPath(brush, path); }
        using var shape = Round(r); using var fill = new LinearGradientBrush(r, Color.FromArgb(220, Color.White), Color.FromArgb(145, Mix(Color.White, accent, .2f)), 90); g.FillPath(fill, shape);
        using var rim = new Pen(selected ? accent : Color.FromArgb(210, Color.White), selected ? 2 : 1.5f); g.DrawPath(rim, shape);
        using var shine = new Pen(Color.White); g.DrawLine(shine, r.X + 8, r.Y + 2, r.Right - 8, r.Y + 2);
    }
    internal static Color Mix(Color a, Color b, float t) => Color.FromArgb((int)(a.R * (1 - t) + b.R * t), (int)(a.G * (1 - t) + b.G * t), (int)(a.B * (1 - t) + b.B * t));
    private static void DrawButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button b || b.Width < 8 || b.Height < 8) return;
        var s = styled.GetOrCreateValue(b); var g = e.Graphics; g.Clear(b.BackColor);
        var r = new RectangleF(4, s.Pressed ? 5 : s.Hover ? 1 : 2, b.Width - 8, b.Height - 9); Glass(g, r, s.Accent ?? Purple, b.Focused || s.Hover, s.Pressed);
        if (s.Accent is { } accent && b.Enabled) { using var shape = Round(r); using var fill = new LinearGradientBrush(r, Mix(accent, Color.White, .08f), accent, 90); g.FillPath(fill, shape); using var shine = new Pen(Color.FromArgb(120, Color.White)); g.DrawArc(shine, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4, 190, 120); }
        TextRenderer.DrawText(g, b.Text, b.Font, Rectangle.Round(r), b.Enabled ? s.Accent is null ? Text : Color.White : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
    }
    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        using (var fill = new SolidBrush(Ink)) e.Graphics.FillRectangle(fill, e.Bounds);
        var selected = tabs.SelectedIndex == e.Index; var r = Rectangle.Inflate(e.Bounds, -4, -5); if (selected) Glass(e.Graphics, r, Purple);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, r, selected ? Purple : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    internal static void PaintNavigation(TabControl tabs, Graphics graphics)
    {
        using var background = new SolidBrush(Ink);
        graphics.FillRectangle(background, 0, 0, tabs.Width, Math.Max(1, tabs.DisplayRectangle.Top));
        for (var i = 0; i < tabs.TabCount; i++)
            DrawTab(tabs, new DrawItemEventArgs(graphics, tabs.Font, tabs.GetTabRect(i), i, DrawItemState.Default));
    }
    internal static void Grid(Graphics g, Rectangle bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1) return;
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(239, 245, 255), Color.FromArgb(248, 238, 250), 30); g.FillRectangle(fill, bounds);
        Glow(g, new(bounds.Width * .05f, -bounds.Height * .2f, bounds.Width * .85f, bounds.Height * .9f), Cyan);
        Glow(g, new(bounds.Width * .45f, bounds.Height * .2f, bounds.Width * .8f, bounds.Height * .9f), Pink);
    }
    private static void Glow(Graphics g, RectangleF rect, Color color)
    {
        using var path = new GraphicsPath(); path.AddEllipse(rect); using var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(42, color), SurroundColors = [Color.FromArgb(0, color)] }; g.FillPath(glow, path);
    }
    internal static void Mark(Graphics g, Rectangle rect)
    {
        Glass(g, rect, Purple); using var fill = new LinearGradientBrush(rect, Cyan, Purple, 40);
        using var back = Round(new RectangleF(rect.X + 6, rect.Y + 6, rect.Width - 16, rect.Height - 16), 7); g.FillPath(fill, back);
        using var front = Round(new RectangleF(rect.X + 13, rect.Y + 12, rect.Width - 18, rect.Height - 16), 6); using var white = new SolidBrush(Color.FromArgb(190, Color.White)); g.FillPath(white, front);
    }
}
internal sealed class ChromeTabs : TabControl
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x000F && IsHandleCreated && !IsDisposed)
        { using var graphics = Graphics.FromHwnd(Handle); ChromeTheme.PaintNavigation(this, graphics); }
        else if (message.Msg is 0x0317 or 0x0318 && message.WParam != 0 && !IsDisposed)
        { using var graphics = Graphics.FromHdc(message.WParam); ChromeTheme.PaintNavigation(this, graphics); }
    }
}
internal sealed class CanvasHeader : Control
{
    internal CanvasHeader() { DoubleBuffered = true; Height = 64; Dock = DockStyle.Top; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ChromeTheme.Ink); ChromeTheme.Mark(e.Graphics, new(20, 10, 40, 40)); using var title = new Font("Segoe UI", 17, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "Borderless", title, new Point(72, 13), ChromeTheme.Text);
        TextRenderer.DrawText(e.Graphics, "让桌面自由呼吸", Font, new Rectangle(280, 0, Math.Max(1, Width - 304), Height), ChromeTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
