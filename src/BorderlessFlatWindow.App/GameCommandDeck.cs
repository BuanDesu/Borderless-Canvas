using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class GameCommandDeck : Panel
{
    private readonly Button start, pause, overview, more, back;
    private readonly Dictionary<Button, Point> pad = [];
    internal IEnumerable<Button> NavigationButtons => pad.Keys.Append(back);
    internal GameCommandDeck(Button start, Button pause, Button overview, Button more, Action<PointD> pan, Action home, Action previous)
    {
        this.start = start; this.pause = pause; this.overview = overview; this.more = more;
        DoubleBuffered = true; ResizeRedraw = true; Dock = DockStyle.Bottom; Height = 168;
        foreach (var button in new[] { start, pause, overview, more }) { button.AutoSize = false; Controls.Add(button); }
        start.Text = "开始"; pause.Text = "暂停"; overview.Text = "总览";
        start.AccessibleName = "开始画布"; pause.AccessibleName = "暂停并恢复"; overview.AccessibleName = "空间总览";
        ChromeTheme.Game(start, ChromeTheme.Purple); ChromeTheme.Game(pause, ChromeTheme.Pink); ChromeTheme.Game(overview, ChromeTheme.Cyan);
        void Direction(string text, string name, Point slot, Action action)
        {
            var button = new ChromeButton { Text = text, AccessibleName = name, TabStop = true };
            button.Click += (_, _) => action(); pad.Add(button, slot); Controls.Add(button); ChromeTheme.Game(button, square: true);
        }
        Direction("↑", "向上移动视角", new(1, 0), () => pan(new(0, -120)));
        Direction("←", "向左移动视角", new(0, 1), () => pan(new(-120, 0)));
        Direction("●", "返回原点", new(1, 1), home);
        Direction("→", "向右移动视角", new(2, 1), () => pan(new(120, 0)));
        Direction("↓", "向下移动视角", new(1, 2), () => pan(new(0, 120)));
        back = new ChromeButton { Text = "上一视角", AccessibleName = "上一视角" }; back.Click += (_, _) => previous(); Controls.Add(back);
        more.Text = "更多操作";
    }
    internal void SetRunning(bool running) { foreach (var button in pad.Keys) button.Enabled = running; back.Enabled = running; }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (start is null || back is null) return;
        var s = DeviceDpi / 96f;
        if (Height != (int)(168 * s)) Height = (int)(168 * s);
        // Two equal groups; both primary keys share the same size, baseline and spacing.
        var leftCenter = Width * .25f; var rightCenter = Width * .75f;
        var cell = 32 * s;
        foreach (var (button, slot) in pad) button.Bounds = new((int)(leftCenter - cell * 1.5f + slot.X * cell), (int)(12 * s + slot.Y * cell), (int)cell, (int)cell);
        var key = 64 * s; var gap = 8 * s;
        start.Bounds = pause.Bounds = new((int)(rightCenter - key - gap / 2), (int)(28 * s), (int)key, (int)key);
        overview.Bounds = new((int)(rightCenter + gap / 2), (int)(28 * s), (int)key, (int)key);
        var secondary = Math.Min(128 * s, Width / 2f - 24 * s);
        back.Bounds = new((int)(leftCenter - secondary / 2), (int)(120 * s), (int)secondary, (int)(36 * s));
        more.Bounds = new((int)(rightCenter - secondary / 2), (int)(120 * s), (int)secondary, (int)(36 * s));
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(ChromeTheme.Ink);
        var r = new RectangleF(8, 4, Width - 16, Height - 8);
        if (r.Width < 1 || r.Height < 1) return;
        ChromeTheme.Glass(e.Graphics, r, ChromeTheme.Purple);
        using var line = new Pen(Color.FromArgb(30, ChromeTheme.Purple));
        e.Graphics.DrawLine(line, Width / 2f, 24, Width / 2f, Height - 24);
    }
}
