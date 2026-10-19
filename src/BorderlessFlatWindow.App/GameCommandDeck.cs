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
        DoubleBuffered = true; ResizeRedraw = true; Dock = DockStyle.Bottom; Height = 172;
        foreach (var button in new[] { start, pause, overview, more }) { button.AutoSize = false; Controls.Add(button); }
        start.Text = "开始"; pause.Text = "暂停"; overview.Text = "总览";
        start.AccessibleName = "开始画布"; pause.AccessibleName = "暂停并恢复"; overview.AccessibleName = "空间总览";
        ChromeTheme.Game(start, ChromeTheme.Purple); ChromeTheme.Game(pause, ChromeTheme.Pink); ChromeTheme.Game(overview, ChromeTheme.Cyan);
        void Direction(string text, string name, Point slot, Action action)
        {
            var button = new Button { Text = text, AccessibleName = name, TabStop = true };
            button.Click += (_, _) => action(); pad.Add(button, slot); Controls.Add(button); ChromeTheme.Game(button);
        }
        Direction("↑", "向上移动视角", new(1, 0), () => pan(new(0, -120)));
        Direction("←", "向左移动视角", new(0, 1), () => pan(new(-120, 0)));
        Direction("●", "返回原点", new(1, 1), home);
        Direction("→", "向右移动视角", new(2, 1), () => pan(new(120, 0)));
        Direction("↓", "向下移动视角", new(1, 2), () => pan(new(0, 120)));
        back = new() { Text = "返回", AccessibleName = "上一视角" }; back.Click += (_, _) => previous(); Controls.Add(back);
    }
    internal void SetRunning(bool running) { foreach (var button in pad.Keys) button.Enabled = running; back.Enabled = running; }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (start is null || back is null) return;
        var s = DeviceDpi / 96f;
        if (Height != (int)(172 * s)) Height = (int)(172 * s);
        foreach (var (button, slot) in pad) button.Bounds = new((int)((18 + slot.X * 38) * s), (int)((10 + slot.Y * 38) * s), (int)(38 * s), (int)(38 * s));
        var right = Width - (int)(20 * s);
        start.Bounds = pause.Bounds = new(right - (int)(68 * s), (int)(18 * s), (int)(68 * s), (int)(68 * s));
        overview.Bounds = new(Math.Max((int)(152 * s), right - (int)(144 * s)), (int)(44 * s), (int)(68 * s), (int)(68 * s));
        more.Bounds = new(right - (int)(92 * s), (int)(120 * s), (int)(92 * s), (int)(40 * s));
        back.Bounds = new((int)(28 * s), (int)(130 * s), (int)(98 * s), (int)(36 * s));
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(ChromeTheme.Ink);
        var r = new RectangleF(4, 3, Width - 8, Height - 8);
        if (r.Width < 1 || r.Height < 1) return;
        ChromeTheme.Glass(e.Graphics, r, ChromeTheme.Pink);
        using var line = new Pen(Color.FromArgb(70, ChromeTheme.Purple));
        e.Graphics.DrawLine(line, Width / 2f, 24, Width / 2f, Height - 25);
        ChromeTheme.Sticker(e.Graphics, new(Width / 2 - 13, Height - 30), "capsule", 2);
    }
}
