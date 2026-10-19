using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
namespace BorderlessFlatWindow.App;

internal static class ChromeTheme
{
    private sealed class Surface { internal bool Hover, Pressed, Applied; internal Color? Accent; }
    private static readonly ConditionalWeakTable<Control, Surface> styled = new();
    internal static readonly Color Ink = Color.FromArgb(231, 226, 255), Panel = Color.FromArgb(245, 240, 255), Text = Color.FromArgb(43, 34, 78), Muted = Color.FromArgb(105, 93, 135);
    internal static readonly Color Cyan = Color.FromArgb(0, 178, 248), Pink = Color.FromArgb(255, 55, 157), Lime = Color.FromArgb(0, 199, 165), Purple = Color.FromArgb(125, 58, 244);
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
        using var shape = Round(r, 19); using var fill = new LinearGradientBrush(r, Color.FromArgb(188, Mix(Color.White, accent, .08f)), Color.FromArgb(150, Mix(Color.White, accent, .42f)), 65); g.FillPath(fill, shape);
        using var rim = new Pen(selected ? accent : Color.FromArgb(210, Color.White), selected ? 2 : 1.5f); g.DrawPath(rim, shape);
        using var shine = new Pen(Color.White); g.DrawLine(shine, r.X + 8, r.Y + 2, r.Right - 8, r.Y + 2);
    }
    internal static Color Mix(Color a, Color b, float t) => Color.FromArgb((int)(a.R * (1 - t) + b.R * t), (int)(a.G * (1 - t) + b.G * t), (int)(a.B * (1 - t) + b.B * t));
    private static void DrawButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button b || b.Width < 8 || b.Height < 8) return;
        var s = styled.GetOrCreateValue(b); var g = e.Graphics; if (b.BackColor != Color.Transparent) g.Clear(b.BackColor);
        var r = new RectangleF(4, s.Pressed ? 5 : s.Hover ? 1 : 2, b.Width - 8, b.Height - 9); Glass(g, r, s.Accent ?? Purple, b.Focused || s.Hover, s.Pressed);
        if (s.Accent is { } accent && b.Enabled) { using var shape = Round(r); using var fill = new LinearGradientBrush(r, accent, accent == Pink ? Purple : Cyan, 25); g.FillPath(fill, shape); using var shine = new Pen(Color.FromArgb(170, Color.White)); g.DrawArc(shine, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4, 190, 120); }
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
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(209, 236, 255), Color.FromArgb(255, 210, 236), 30); g.FillRectangle(fill, bounds);
        Glow(g, new(bounds.Width * .05f, -bounds.Height * .2f, bounds.Width * .85f, bounds.Height * .9f), Cyan);
        Glow(g, new(bounds.Width * .45f, bounds.Height * .2f, bounds.Width * .8f, bounds.Height * .9f), Pink);
    }
    private static void Glow(Graphics g, RectangleF rect, Color color)
    {
        using var path = new GraphicsPath(); path.AddEllipse(rect); using var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(105, color), SurroundColors = [Color.FromArgb(0, color)] }; g.FillPath(glow, path);
    }
    internal static void Mark(Graphics g, Rectangle rect)
    {
        Glass(g, rect, Purple); using var fill = new LinearGradientBrush(rect, Cyan, Purple, 40);
        using var back = Round(new RectangleF(rect.X + 6, rect.Y + 6, rect.Width - 16, rect.Height - 16), 7); g.FillPath(fill, back);
        using var front = Round(new RectangleF(rect.X + 13, rect.Y + 12, rect.Width - 18, rect.Height - 16), 6); using var white = new SolidBrush(Color.FromArgb(190, Color.White)); g.FillPath(white, front);
    }
    internal static void Sticker(Graphics g, Point at, string kind, int pixel = 3)
    {
        string[] pattern = kind switch
        {
            "heart" => [".aa.aa.", "abbbbba", "abbbbba", ".abbba.", "..aba..", "...a..."],
            "capsule" => ["..aaaaaa..", ".abbbccca.", "abbbbcccca", "abbbbcccca", ".abbbccca.", "..aaaaaa.."],
            _ => ["a.......a", "aa.aaa.aa", ".aabbbaa.", "..abbba..", ".aabbbaa.", "aa.aaa.aa", "a.......a"]
        };
        for (var y = 0; y < pattern.Length; y++) for (var x = 0; x < pattern[y].Length; x++)
        {
            var c = pattern[y][x]; if (c == '.') continue;
            using var shadow = new SolidBrush(Color.FromArgb(40, Purple)); g.FillRectangle(shadow, at.X + x * pixel + 1, at.Y + y * pixel + 2, pixel, pixel);
            using var fill = new SolidBrush(c == 'a' ? Purple : c == 'b' ? kind == "capsule" ? Color.White : Pink : Cyan);
            g.FillRectangle(fill, at.X + x * pixel, at.Y + y * pixel, pixel, pixel);
        }
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
    internal CanvasHeader() { DoubleBuffered = true; ResizeRedraw = true; Height = 108; Dock = DockStyle.Top; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(ChromeTheme.Ink); g.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new RectangleF(10, 5, Width - 20, Height - 18);
        ChromeTheme.Glass(g, body, ChromeTheme.Pink);
        using var shell = ChromeTheme.Round(body, 27); using var color = new LinearGradientBrush(body, Color.FromArgb(255, 137, 214), Color.FromArgb(133, 201, 255), 8); g.FillPath(color, shell);
        using var rim = new Pen(Color.FromArgb(220, Color.White), 2); g.DrawPath(rim, shell);
        // Recessed display and speaker perforations evoke a pocket music player.
        var lcd = new RectangleF(80, 18, Math.Max(250, Width - 290), 62);
        using var screen = ChromeTheme.Round(lcd, 18); using var tint = new LinearGradientBrush(lcd, Color.FromArgb(247, 239, 255), Color.FromArgb(199, 235, 255), 95); g.FillPath(tint, screen);
        using var inset = new Pen(Color.FromArgb(100, ChromeTheme.Purple), 2); g.DrawPath(inset, screen);
        using var title = new Font("Segoe UI", 17, FontStyle.Bold); using var micro = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        TextRenderer.DrawText(g, "BORDERLESS", title, new Point(96, 20), ChromeTheme.Text);
        TextRenderer.DrawText(g, "INFINITE DESKTOP   /   SPATIAL PLAYER", micro, new Point(98, 61), ChromeTheme.Muted);
        ChromeTheme.Sticker(g, new(30, 25), "capsule", 4);
        ChromeTheme.Sticker(g, new(Width - 190, 30), "heart", 4);
        ChromeTheme.Sticker(g, new(Width - 145, 29), "candy", 4);
        using var holes = new SolidBrush(Color.FromArgb(115, ChromeTheme.Purple));
        for (var x = 0; x < 4; x++) for (var y = 0; y < 5; y++) g.FillEllipse(holes, Width - 72 + x * 9, 26 + y * 8, 3, 3);
    }
}
internal sealed class PlayerScreenPanel : Panel
{
    internal PlayerScreenPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(ChromeTheme.Ink);
        var frame = new RectangleF(10, 0, Width - 20, Height - 6);
        if (frame.Width < 2 || frame.Height < 2) return;
        using var outline = ChromeTheme.Round(frame, 26);
        using var color = new LinearGradientBrush(frame, ChromeTheme.Purple, ChromeTheme.Pink, 25);
        e.Graphics.FillPath(color, outline);
        using var rim = new Pen(Color.FromArgb(220, Color.White), 2); e.Graphics.DrawPath(rim, outline);
    }
}
