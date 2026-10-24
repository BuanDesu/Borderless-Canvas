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
    private readonly CheckBox edge = new() { Text = "屏幕外沿向外移动鼠标推进视角", AutoSize = true };
    private readonly NumericUpDown pointerSpeed = new() { Name = "PointerSpeed", Minimum = .25m, Maximum = 4, Increment = .25m, DecimalPlaces = 2, Width = 110 };
    private readonly NumericUpDown distance = new() { Name = "WheelDistance", Minimum = 30, Maximum = 600, Increment = 30, Width = 110 };
    private readonly List<(ComboBox Modifier, ComboBox Key)> bindingFields = [];
    private readonly Label error = new() { AutoSize = false, Height = 70, Dock = DockStyle.Bottom, Padding = new(18, 6, 18, 6), ForeColor = Color.Firebrick, Visible = false };
    private sealed record KeyOption(uint Key, string Label) { public override string ToString() => Label; }
    internal NavigationKeyBindings Bindings
    {
        get
        {
            var values = bindingFields.Select(pair => new KeyChord(pair.Modifier.SelectedIndex switch { 0 => 0u, 1 => 3u, _ => 7u }, ((KeyOption)pair.Key.SelectedItem!).Key)).ToArray();
            return new() { Overview = values[0], Crosshair = values[1], Touch = values[2], Pin = values[3], Home = values[4], Back = values[5] };
        }
    }
    internal NavigationInputOptions Options => new(vertical.Checked, horizontal.Checked, drag.Checked, (int)distance.Value, edge.Checked, (double)pointerSpeed.Value);
    internal void CheckKeyBindings(string output)
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Synthetic settings checks only.");
        var tabs = Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
        var save = (ChromeButton)AcceptButton!;
        var original = bindingFields[2].Key.SelectedItem;
        bindingFields[2].Key.SelectedItem = bindingFields[2].Key.Items.Cast<KeyOption>().Single(value => value.Key == Bindings.Crosshair.Key);
        save.PerformClick();
        if (DialogResult == DialogResult.OK || string.IsNullOrEmpty(error.Text) || !Visible) throw new InvalidOperationException("Duplicate key settings closed or silently accepted.");
        bindingFields[2].Key.SelectedItem = original; error.Text = ""; error.Visible = false; System.Windows.Forms.Application.DoEvents();
        if (bindingFields.Any(pair => pair.Key.Right > pair.Key.Parent!.ClientSize.Width || pair.Modifier.Bottom > pair.Modifier.Parent!.ClientSize.Height)) throw new InvalidOperationException("Key controls are clipped.");
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(output, "navigation-keys-smoke.png"));
        Console.WriteLine("PASS Key settings retain duplicate errors without closing and show all six commands in a synthetic nonactivating fixture");
    }
    internal DialogResult ShowFor(Control anchor)
    {
        var host = anchor.FindForm(); TopMost = host?.TopMost == true;
        return ShowDialog(host ?? (IWin32Window)anchor);
    }
    internal NavigationSettingsDialog(NavigationInputOptions options, NavigationKeyBindings? bindings = null, bool showWithoutActivation = false, Func<NavigationKeyBindings, string?>? tryBindings = null)
    {
        this.showWithoutActivation = showWithoutActivation;
        Text = "导航与键位设置"; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new(570, 570); bindings ??= NavigationKeyBindings.Default;
        vertical.Checked = options.VerticalWheel; horizontal.Checked = options.HorizontalWheel; drag.Checked = options.TwoButtonDrag;
        distance.Value = options.WheelDistance;
        edge.Checked = options.EdgePan; pointerSpeed.Value = (decimal)options.PointerSpeed;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        content.Controls.AddRange([vertical, horizontal, drag, edge, new Label { Text = "边缘速度 / 准星灵敏度", AutoSize = true }, pointerSpeed,
            new Label { Text = "每格滚轮移动距离（画布像素）", AutoSize = true }, distance,
            new Label { Text = "关闭某项后，对应输入直接交给应用。\n设置保存在本机，下次启动保留。", AutoSize = true, Margin = new(0, 12, 0, 0) }]);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var gestures = new TabPage("手势"); gestures.Controls.Add(content); tabs.TabPages.Add(gestures);
        var keys = new TabPage("快捷键") { AutoScroll = true }; tabs.TabPages.Add(keys);
        var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(14), ColumnCount = 3 };
        rows.ColumnStyles.Add(new(SizeType.Absolute, 100)); rows.ColumnStyles.Add(new(SizeType.Absolute, 205)); rows.ColumnStyles.Add(new(SizeType.Absolute, 160));
        var names = new[] { "空间总览", "准星模式", "触控模式", "切换便签", "返回原点", "上一视角" };
        var chords = new[] { bindings.Overview, bindings.Crosshair, bindings.Touch, bindings.Pin, bindings.Home, bindings.Back };
        for (var row = 0; row < chords.Length; row++)
        {
            var modifier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 195, Margin = new(3, 6, 3, 6), Name = "BindingModifier" + row };
            modifier.Items.AddRange(["无（仅原点 / 返回）", "Ctrl+Alt", "Ctrl+Alt+Shift"]); modifier.SelectedIndex = chords[row].Modifiers switch { 0 => 0, 3 => 1, _ => 2 };
            var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new(3, 6, 3, 6), Name = "BindingKey" + row };
            foreach (var code in Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x70, 11).Where(code => code != 0x73)).Concat([0x24, 0x08]))
                key.Items.Add(new KeyOption((uint)code, GlobalNavigationInput.Describe(new(0, (uint)code))));
            key.SelectedItem = key.Items.Cast<KeyOption>().Single(option => option.Key == chords[row].Key);
            rows.Controls.Add(new Label { Text = names[row], AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(3, 6, 3, 6) }, 0, row);
            rows.Controls.Add(modifier, 1, row); rows.Controls.Add(key, 2, row); bindingFields.Add((modifier, key));
        }
        keys.Controls.Add(new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new(520, 0), Padding = new(18, 8, 18, 8), Text = "全局组合：Ctrl+Alt（可加 Shift）＋字母、数字或功能键。\n避开 Win、Alt+Tab、编辑键、F4 / F12 等系统组合。\nHome / Backspace 仅在画布导航时生效。\nTab 总览与 Esc 退出保留。" });
        keys.Controls.Add(rows);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new(8), FlowDirection = FlowDirection.RightToLeft };
        var save = new ChromeButton { Text = "保存", AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                var value = Bindings; value.Validate();
                if (tryBindings?.Invoke(value) is { } failure) { error.Text = failure; error.Visible = true; tabs.SelectedTab = keys; return; }
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException failure) { error.Text = failure.Message; error.Visible = true; tabs.SelectedTab = keys; }
        };
        var cancel = new ChromeButton { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.AddRange([save, cancel]); Controls.Add(tabs); Controls.Add(error); Controls.Add(actions); AcceptButton = save; CancelButton = cancel;
        ChromeTheme.Apply(this);
    }
}
