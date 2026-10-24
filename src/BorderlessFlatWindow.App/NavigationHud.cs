using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.App;

internal sealed class NavigationHud : Form
{
    private readonly Label text = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    internal NavigationHud()
    {
        Text = "BFW 导航状态"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(25, 42, 52); ForeColor = Color.FromArgb(140, 250, 212);
        Font = PixelFont.Create(12); Size = new(720, 60); Opacity = .88; Controls.Add(text);
    }
    internal void Present(NavigationInputOptions? options = null, NavigationKeyBindings? bindings = null)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Width = Math.Min(720, Math.Max(260, area.Width - 24));
        Location = new(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + 12);
        options ??= new();
        var enabled = string.Join(" · ", new[] { options.VerticalWheel ? "滚轮纵移" : null,
            options.HorizontalWheel ? "Shift 横移" : null, options.TwoButtonDrag ? "左右双键拖动" : null }.Where(item => item is not null));
        bindings ??= NavigationKeyBindings.Default;
        text.Text = $"视角移动 · {enabled}\n{BorderlessFlatWindow.Windows.GlobalNavigationInput.Describe(bindings.Home)} 原点 · {BorderlessFlatWindow.Windows.GlobalNavigationInput.Describe(bindings.Back)} 返回 · Tab 总览";
        if (!Visible) Show();
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); if (Width <= 0 || Height <= 0) return;
        using var round = ChromeTheme.Round(new Rectangle(0, 0, Width, Height), 18); var previous = Region; Region = new(round); previous?.Dispose();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x080800A0; return value; } }
    protected override void WndProc(ref Message message)
    { if (message.Msg == 0x84) { message.Result = -1; return; } base.WndProc(ref message); }
}
