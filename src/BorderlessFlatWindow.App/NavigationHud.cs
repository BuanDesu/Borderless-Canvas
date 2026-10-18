using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.App;

internal sealed class NavigationHud : Form
{
    private readonly Label text = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    internal NavigationHud()
    {
        Text = "BFW 导航状态"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(25, 42, 52); ForeColor = Color.FromArgb(140, 250, 212);
        Font = new Font("Microsoft YaHei UI", 11); Size = new(980, 96); Controls.Add(text);
    }
    internal void Present(string hotkey, PointD camera, NavigationInputOptions? options = null)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + 12);
        options ??= new();
        var enabled = string.Join(" · ", new[] { options.VerticalWheel ? "滚轮纵移" : null,
            options.HorizontalWheel ? "Shift 横移" : null, options.TwoButtonDrag ? "左右双键拖动" : null }.Where(item => item is not null));
        text.Text = $"导航中 · 相机 ({camera.X:0}, {camera.Y:0})\n{enabled}\nHome 原点 · Backspace 上一视角 · Esc 或 {hotkey} 退出";
        if (!Visible) Show();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; } }
}
