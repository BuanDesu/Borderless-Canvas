using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using BorderlessFlatWindow.Core.Appearance;
namespace BorderlessFlatWindow.App;

internal static class ChromeTheme
{
    private sealed class Surface { internal bool Hover, Pressed, Applied, Game; internal Color? Accent; }
    private static readonly ConditionalWeakTable<Control, Surface> styled = new();
    private static Color[] colors = [ColorOf(CandyPalette.Default.Primary), ColorOf(CandyPalette.Default.Secondary), ColorOf(CandyPalette.Default.Tertiary), ColorOf(CandyPalette.Default.Quaternary)];
    internal static Color Ink => Mix(Color.White, Purple, .16f);
    internal static Color Panel => Mix(Color.White, Purple, .07f);
    internal static readonly Color Text = Color.FromArgb(35, 30, 49), Muted = Color.FromArgb(89, 82, 107);
    internal static Color Purple => colors[0];
    internal static Color Cyan => colors[1];
    internal static Color Pink => colors[2];
    internal static Color Lime => colors[3];
    internal static Color[] Candy => colors;
    private static Color ColorOf(Rgb c) => Color.FromArgb(c.R, c.G, c.B);
    internal static void SetPalette(CandyPalette next)
    {
        var old = colors; colors = [ColorOf(next.Primary), ColorOf(next.Secondary), ColorOf(next.Tertiary), ColorOf(next.Quaternary)];
        foreach (var pair in styled)
            if (pair.Value.Accent is { } accent && Array.IndexOf(old, accent) is var index && index >= 0) pair.Value.Accent = colors[index];
    }
    internal static Color OnAccent(Color color)
    {
        var rgb = new Rgb(color.R, color.G, color.B);
        return rgb.Contrast(new(255, 255, 255)) >= rgb.Contrast(new(0, 0, 0)) ? Color.White : Color.Black;
    }
    internal static void Game(Button button, Color? accent = null) { var surface = styled.GetOrCreateValue(button); surface.Game = true; surface.Accent = accent; }
    internal static void Accent(Button button, Color color) => styled.GetOrCreateValue(button).Accent = color;
    internal static void Apply(Control root)
    {
        root.ForeColor = Text; root.BackColor = Ink;
        var surface = styled.GetOrCreateValue(root); var first = !surface.Applied; surface.Applied = true;
        switch (root)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                b.Padding = new(12, 7, 12, 7); b.MinimumSize = new(36, 36); b.Cursor = Cursors.Hand;
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
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.SizeMode = TabSizeMode.Fixed; tabs.Padding = new(12, 8);
                if (tabs is ChromeTabs chrome) chrome.FitLabels(); else tabs.ItemSize = new(156, Math.Max(44, tabs.Font.Height + 18));
                if (first) tabs.DrawItem += DrawTab; break;
            case ToolStrip strip: StyleMenu(strip); break;
        }
        foreach (Control child in root.Controls) Apply(child);
        if (root is GameCommandDeck) foreach (Control child in root.Controls) child.BackColor = Color.Transparent;
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
        public override Color MenuItemSelected => Mix(Color.White, Purple, .12f);
        public override Color MenuItemBorder => Mix(Color.White, Purple, .25f);
        public override Color MenuBorder => Mix(Color.White, Purple, .2f);
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
        if (s.Game) { DrawGameButton(g, b, s); return; }
        var r = new RectangleF(4, s.Pressed ? 5 : s.Hover ? 1 : 2, b.Width - 8, b.Height - 9); Glass(g, r, s.Accent ?? Purple, b.Focused || s.Hover, s.Pressed);
        if (s.Accent is { } accent && b.Enabled) { using var shape = Round(r); using var fill = new LinearGradientBrush(r, accent, accent == Pink ? Purple : Cyan, 25); g.FillPath(fill, shape); using var shine = new Pen(Color.FromArgb(170, Color.White)); g.DrawArc(shine, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4, 190, 120); }
        TextRenderer.DrawText(g, b.Text, b.Font, Rectangle.Round(r), b.Enabled ? s.Accent is null ? Text : OnAccent(Mix(s.Accent.Value, s.Accent == Pink ? Purple : Cyan, .5f)) : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
    }
    private static void DrawGameButton(Graphics g, Button button, Surface state)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(button.Width, button.Height) - 10;
        var r = new RectangleF((button.Width - size) / 2f, state.Pressed ? 7 : 3, size, size);
        using var socket = new SolidBrush(Mix(Purple, Color.Black, .28f)); g.FillEllipse(socket, r.X - 3, r.Y + 2, r.Width + 6, r.Height + 5);
        var accent = state.Accent ?? Mix(Color.White, Purple, .75f);
        using var fill = new LinearGradientBrush(r, Mix(accent, Color.White, .4f), accent, 80); g.FillEllipse(fill, r);
        using var rim = new Pen(Color.FromArgb(210, Color.White), button.Focused || state.Hover ? 2.5f : 1); g.DrawEllipse(rim, r);
        using var highlight = new Pen(Color.FromArgb(145, Color.White), 2); g.DrawArc(highlight, r.X + 4, r.Y + 3, r.Width - 8, r.Height - 8, 200, 95);
        TextRenderer.DrawText(g, button.Text, button.Font, Rectangle.Round(r), button.Enabled ? OnAccent(Mix(accent, Color.White, .15f)) : Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
        using var fill = new LinearGradientBrush(bounds, Mix(Color.White, Cyan, .22f), Mix(Color.White, Pink, .23f), 30); g.FillRectangle(fill, bounds);
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
    private bool fitting;
    internal void FitLabels()
    {
        if (fitting || TabCount == 0 || IsDisposed) return;
        fitting = true;
        try
        {
            var scale = DeviceDpi / 96f;
            var width = TabPages.Cast<TabPage>().Max(page => TextRenderer.MeasureText(page.Text, Font).Width) + (int)(24 * scale);
            var size = new Size(Math.Max(72, width), Math.Max((int)(38 * scale), Font.Height + (int)(16 * scale)));
            if (!Multiline) Multiline = true;
            if (ItemSize != size) ItemSize = size;
        }
        finally { fitting = false; }
    }
    protected override void OnLayout(LayoutEventArgs e) { FitLabels(); base.OnLayout(e); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitLabels(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitLabels(); }
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
    internal CanvasHeader() { DoubleBuffered = true; ResizeRedraw = true; Height = 78; Dock = DockStyle.Top; }
    protected override void OnLayout(LayoutEventArgs e)
    {
        var height = (int)(78 * DeviceDpi / 96f); if (Height != height) Height = height;
        base.OnLayout(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(ChromeTheme.Ink); g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = DeviceDpi / 96f;
        var body = new RectangleF(10 * s, 5 * s, Width - 20 * s, Height - 18 * s);
        ChromeTheme.Glass(g, body, ChromeTheme.Pink);
        using var shell = ChromeTheme.Round(body, 27); using var color = new LinearGradientBrush(body, ChromeTheme.Mix(Color.White, ChromeTheme.Pink, .55f), ChromeTheme.Mix(Color.White, ChromeTheme.Cyan, .55f), 8); g.FillPath(color, shell);
        using var rim = new Pen(Color.FromArgb(220, Color.White), 2); g.DrawPath(rim, shell);
        // Recessed display and speaker perforations evoke a pocket music player.
        var lcd = new RectangleF(56 * s, 14 * s, Math.Max(140 * s, Width - 124 * s), 40 * s);
        using var screen = ChromeTheme.Round(lcd, 13); using var tint = new LinearGradientBrush(lcd, ChromeTheme.Panel, ChromeTheme.Mix(Color.White, ChromeTheme.Cyan, .2f), 95); g.FillPath(tint, screen);
        using var inset = new Pen(Color.FromArgb(100, ChromeTheme.Purple), 2); g.DrawPath(inset, screen);
        using var title = new Font("Segoe UI", 13, FontStyle.Bold);
        TextRenderer.DrawText(g, "BORDERLESS", title, Rectangle.Round(lcd), ChromeTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        ChromeTheme.Sticker(g, new((int)(22 * s), (int)(27 * s)), "heart", Math.Max(2, (int)(3 * s)));
        using var holes = new SolidBrush(Color.FromArgb(115, ChromeTheme.Purple));
        if (Controls.Count == 0) for (var x = 0; x < 3; x++) for (var y = 0; y < 4; y++) g.FillEllipse(holes, Width - 52 * s + x * 7 * s, (22 + y * 7) * s, 3 * s, 3 * s);
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
