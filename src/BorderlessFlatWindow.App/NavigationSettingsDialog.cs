using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed class NavigationSettingsDialog : Form
{
    private readonly bool showWithoutActivation;
    protected override bool ShowWithoutActivation => showWithoutActivation;
    private readonly CheckBox vertical = new() { Text = "滚轮纵向平移", AutoSize = true };
    private readonly CheckBox horizontal = new() { Text = "Shift＋滚轮 / 水平滚轮横向平移", AutoSize = true };
    private readonly CheckBox drag = new() { Text = "左右双键拖动画布", AutoSize = true };
    private readonly NumericUpDown distance = new() { Minimum = 30, Maximum = 600, Increment = 30, Width = 110 };
    private readonly ComboBox shortcut = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    internal NavigationHotkey Hotkey => (NavigationHotkey)shortcut.SelectedItem!;
    internal NavigationInputOptions Options => new(vertical.Checked, horizontal.Checked, drag.Checked, (int)distance.Value);
    internal NavigationSettingsDialog(NavigationInputOptions options, bool showWithoutActivation = false, NavigationHotkey? hotkey = null)
    {
        this.showWithoutActivation = showWithoutActivation;
        Text = "导航手势设置"; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new(550, 370);
        shortcut.DisplayMember = nameof(NavigationHotkey.Label);
        shortcut.Items.AddRange([NavigationHotkey.Default, new NavigationHotkey(3, 0x4E, "Ctrl+Alt+N"), new NavigationHotkey(3, 0x4D, "Ctrl+Alt+M")]);
        shortcut.SelectedItem = hotkey ?? NavigationHotkey.Default;
        vertical.Checked = options.VerticalWheel; horizontal.Checked = options.HorizontalWheel; drag.Checked = options.TwoButtonDrag;
        distance.Value = options.WheelDistance;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        content.Controls.AddRange([vertical, horizontal, drag, new Label { Text = "手动导航备用快捷键", AutoSize = true }, shortcut,
            new Label { Text = "每格滚轮移动距离（画布像素）", AutoSize = true }, distance,
            new Label { Text = "关闭某项后，对应输入直接交给应用。\n设置目前只保存在本次进程内。", AutoSize = true, Margin = new(0, 12, 0, 0) }]);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new(8), FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.AddRange([save, cancel]); Controls.Add(content); Controls.Add(actions); AcceptButton = save; CancelButton = cancel;
        ChromeTheme.Apply(this);
    }
}
