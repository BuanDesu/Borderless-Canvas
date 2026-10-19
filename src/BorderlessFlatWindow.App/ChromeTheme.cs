using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace BorderlessFlatWindow.App;

internal static class ChromeTheme
{
    private static readonly ConditionalWeakTable<Control, object> styled = new();
    internal static readonly Color Ink = Color.FromArgb(16, 14, 29);
    internal static readonly Color Panel = Color.FromArgb(29, 27, 48);
    internal static readonly Color Text = Color.FromArgb(231, 237, 246);
    internal static readonly Color Cyan = Color.FromArgb(81, 246, 243);
    internal static readonly Color Pink = Color.FromArgb(255, 103, 209);
    internal static readonly Color Lime = Color.FromArgb(209, 255, 112);
    internal static void Apply(Control root)
    {
        root.ForeColor = Text; root.BackColor = root is TabPage ? Ink : Panel;
        var first = !styled.TryGetValue(root, out _);
        if (first) styled.Add(root, new());
        switch (root)
        {
            case Button button:
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(105, 110, 138);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(59, 59, 85);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(71, 69, 100);
                button.Padding = new(8, 4, 8, 4); button.MinimumSize = new(0, 36); button.Cursor = Cursors.Hand;
                if (first) button.Paint += DrawButton;
                break;
            case TextBox box: box.BackColor = Ink; box.BorderStyle = BorderStyle.FixedSingle; break;
            case ListView list: list.BackColor = Ink; list.BorderStyle = BorderStyle.None; break;
            case CheckedListBox list: list.BackColor = Ink; list.BorderStyle = BorderStyle.None; list.IntegralHeight = false; break;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.SizeMode = TabSizeMode.Fixed;
                tabs.ItemSize = new(155, 44); tabs.Padding = new(12, 8);
                if (first) tabs.DrawItem += DrawTab;
                break;
        }
        foreach (Control child in root.Controls) Apply(child);
    }
    private static void DrawButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button || button.Width < 2 || button.Height < 2) return;
        var bounds = button.ClientRectangle;
        using var foil = new LinearGradientBrush(bounds, button.Enabled ? Color.FromArgb(72, 73, 96) : Color.FromArgb(39, 38, 55), Ink, 90);
        e.Graphics.FillRectangle(foil, bounds);
        using var border = new Pen(button.Enabled ? Color.FromArgb(145, 151, 177) : Color.FromArgb(74, 71, 92));
        e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        using var accent = new Pen(button.Enabled ? Cyan : Color.FromArgb(90, 91, 113));
        e.Graphics.DrawLine(accent, 3, 3, Math.Min(bounds.Width - 4, 18), 3);
        TextRenderer.DrawText(e.Graphics, button.Text, button.Font, Rectangle.Inflate(bounds, -7, -3), button.Enabled ? Text : Color.FromArgb(140, 139, 161),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (button.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), Lime, Panel);
    }
    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var selected = tabs.SelectedIndex == e.Index;
        using var fill = new LinearGradientBrush(e.Bounds, selected ? Color.FromArgb(72, 73, 96) : Panel, Ink, 90);
        e.Graphics.FillRectangle(fill, e.Bounds);
        using var line = new Pen(selected ? Cyan : Color.FromArgb(69, 67, 91), selected ? 3 : 1);
        e.Graphics.DrawLine(line, e.Bounds.Left + 8, e.Bounds.Bottom - 3, e.Bounds.Right - 8, e.Bounds.Bottom - 3);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? Cyan : Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    internal static void Grid(Graphics g, Rectangle bounds)
    {
        using var fill = new LinearGradientBrush(bounds, Ink, Color.FromArgb(37, 30, 58), 30);
        g.FillRectangle(fill, bounds);
        using var pen = new Pen(Color.FromArgb(28, Cyan));
        for (var x = 0; x < bounds.Width; x += 32) g.DrawLine(pen, x, 0, x, bounds.Height);
        for (var y = 0; y < bounds.Height; y += 32) g.DrawLine(pen, 0, y, bounds.Width, y);
    }
}

internal sealed class CanvasHeader : Control
{
    internal CanvasHeader() { DoubleBuffered = true; Height = 86; Dock = DockStyle.Top; }
    protected override void OnPaint(PaintEventArgs e)
    {
        ChromeTheme.Grid(e.Graphics, ClientRectangle);
        using var title = new Font("Consolas", 22, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "BFW // INFINITE DESKTOP", title, new Point(20, 10), ChromeTheme.Cyan);
        TextRenderer.DrawText(e.Graphics, "连续空间   /   原生窗口   /   桌面空白处自动导航", Font, new Point(22, 53), ChromeTheme.Text);
        using var pen = new Pen(ChromeTheme.Pink, 2);
        e.Graphics.DrawLine(pen, 20, Height - 3, Width - 20, Height - 3);
    }
}
