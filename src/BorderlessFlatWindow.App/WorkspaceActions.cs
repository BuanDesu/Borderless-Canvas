using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class WorkspaceActions(BookmarkLibrary library, Func<PointD, WorkspaceSnapshot> capture,
    Func<IReadOnlyList<WorkspaceCandidate>> candidates, Action<Guid, Guid> bind)
{
    internal void Save(IWin32Window owner, string name, PointD camera, Guid? replace = null)
    {
        try
        {
            var snapshot = capture(camera);
            var detail = $"保存当前视区内 {snapshot.Windows.Count} 个画布窗口的位置和尺寸。固定便签不包含在内。\n\n只保存程序启动项，不保存未保存文档、内存或浏览器标签；可从卡片右键查看并调整成员。"
                + (replace is not null ? "\n这会明确替换原有布局快照。" : "");
            if (MessageBox.Show(owner, detail, "保存窗口布局", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            if (library.SaveWorkspace(name, camera, snapshot, replace) is null) MessageBox.Show(owner, library.Message, "未保存");
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(owner, error.Message, "无法保存窗口布局"); }
    }
    internal Form MemberEditor(Guid id)
    {
        var bookmark = library.Collection.Items.First(e => e.Id == id);
        var dialog = new Form { Text = "工作区成员与启动项 · " + bookmark.Name, ClientSize = new(760, 520), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        if (bookmark.Layout is null)
        {
            dialog.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "这是旧版仅位置工作区。\n请从卡片右键选择“更新布局”明确保存窗口成员。" });
            return dialog;
        }
        var list = new ListBox { Dock = DockStyle.Left, Width = 245, DisplayMember = nameof(WorkspaceMember.Label) };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new(12) };
        var program = new TextBox { Dock = DockStyle.Fill };
        var arguments = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 85 };
        var automatic = new CheckBox { Text = "缺失时允许启动这个 EXE", AutoSize = true };
        var windows = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(WindowChoice.Caption) };
        var assign = new ChromeButton { Text = "使用所选当前窗口", AutoSize = true };
        var save = new ChromeButton { Text = "保存启动项", AutoSize = true, Enabled = library.CanWrite };
        var remove = new ChromeButton { Text = "从快照移除此成员（不关闭软件）", AutoSize = true, Enabled = library.CanWrite };
        var message = new WrapLabel { AutoSize = true, Text = "参数每行一项，按原样传递。不会记录进程命令行。\n同程序多窗口身份不明确时，选择对应当前窗口后再复原。" };
        panel.Controls.Add(new Label { Text = "程序 EXE（完整路径）", AutoSize = true }); panel.Controls.Add(program);
        panel.Controls.Add(new Label { Text = "启动参数（每行独立一项）", AutoSize = true }); panel.Controls.Add(arguments);
        panel.Controls.Add(automatic); panel.Controls.Add(windows); panel.Controls.Add(assign); panel.Controls.Add(save); panel.Controls.Add(remove); panel.Controls.Add(message);
        var close = new ChromeButton { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true }; panel.Controls.Add(close); dialog.CancelButton = close;
        dialog.Controls.Add(panel); dialog.Controls.Add(list);
        void Render(Guid? selected = null)
        {
            list.Items.Clear(); foreach (var member in library.Collection.Items.First(e => e.Id == id).Layout!.Windows) list.Items.Add(member);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, list.Items.Cast<WorkspaceMember>().ToList().FindIndex(m => m.Id == selected));
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceMember member) return;
            program.Text = member.Executable ?? ""; automatic.Checked = member.Launch is not null;
            arguments.Text = string.Join(Environment.NewLine, member.Launch?.Arguments ?? []);
            windows.Items.Clear();
            foreach (var candidate in candidates().Where(c => c.Eligible && string.Equals(c.Executable, member.Executable, StringComparison.OrdinalIgnoreCase))) windows.Items.Add(new WindowChoice(candidate));
            if (windows.Items.Count > 0) windows.SelectedIndex = 0;
            assign.Enabled = windows.Items.Count > 0;
        };
        assign.Click += (_, _) =>
        {
            if (list.SelectedItem is WorkspaceMember member && windows.SelectedItem is WindowChoice choice)
            { bind(member.Id, choice.Candidate.Id); message.Text = "已明确选择此管理器会话中的窗口；关闭后不自动改绑其他窗口。"; }
        };
        save.Click += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceMember member) return;
            try
            {
                var args = arguments.Text.Length == 0 ? [] : arguments.Text.Replace("\r", "").Split('\n');
                var executable = string.IsNullOrWhiteSpace(program.Text) ? null : program.Text.Trim();
                var launch = automatic.Checked ? new WorkspaceLaunch(executable ?? "", Array.AsReadOnly(args)) : null;
                var changed = member with { Executable = executable, Launch = launch }; changed.Validate();
                var members = library.Collection.Items.First(e => e.Id == id).Layout!.Windows.Select(m => m.Id == member.Id ? changed : m).ToArray();
                library.UpdateWorkspaceMembers(id, members); message.Text = library.Message; Render(member.Id);
            }
            catch (ArgumentException error) { message.Text = error.Message; }
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is WorkspaceMember member)
            { library.UpdateWorkspaceMembers(id, library.Collection.Items.First(e => e.Id == id).Layout!.Windows.Where(m => m.Id != member.Id).ToArray()); message.Text = library.Message; Render(); }
        };
        Render(); ChromeTheme.Apply(dialog); return dialog;
    }
    private sealed record WindowChoice(WorkspaceCandidate Candidate)
    { public string Caption => Path.GetFileName(Candidate.Executable) + " · " + Candidate.Label; }
}
