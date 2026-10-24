using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.App;

internal sealed class NavigationHud : NavigationOverlay
{
    private readonly Label text = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    internal NavigationHud() : base(mouseTransparent: true)
    {
        Text = "BFW 导航状态"; Size = new(720, 60); Controls.Add(text);
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
}
