using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

internal sealed class InputStatusForm : Form
{
    private readonly bool passive;
    private readonly TextBox content = new()
    {
        Name = "InputStatusReport", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AccessibleName = "输入状态详情"
    };
    internal InputStatusForm(bool passive)
    {
        this.passive = passive;
        Text = "输入状态"; Name = "InputStatus"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new(440, 480); MinimumSize = new(380, 400); ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false; Padding = new(16);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
        var close = new ChromeButton { Text = "关闭", Size = new(100, 44) };
        var copy = new ChromeButton { Text = "复制报告", Name = "CopyInputStatus", Size = new(120, 44) };
        close.Click += (_, _) => Close();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(content.Text); copy.Text = "已复制"; }
            catch (ExternalException) { copy.Text = "复制重试"; }
        };
        actions.Controls.AddRange([close, copy]); Controls.Add(content); Controls.Add(actions);
        ChromeTheme.Apply(this);
        if (passive) Opacity = 0;
    }
    internal void SetReport(string report) { content.Text = report; content.SelectAll(); }
    protected override bool ShowWithoutActivation => passive;
    protected override CreateParams CreateParams
    { get { var value = base.CreateParams; if (passive) value.ExStyle |= 0x08000000; return value; } }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    { if (keys == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref message, keys); }
}
