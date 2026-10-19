using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class WorkspaceStrip : UserControl
{
    private readonly BookmarkLibrary? library;
    private readonly PointD camera;
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new(12, 0, 12, 0) };
    private readonly Label message = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new(12, 2, 0, 0) };
    private readonly TextBox filter = new() { Width = 180, PlaceholderText = "筛选工作区", Margin = new(12, 8, 3, 3) };
    internal event Action<PointD>? JumpRequested;
    internal WorkspaceStrip(BookmarkLibrary? library, PointD camera)
    {
        this.library = library; this.camera = camera;
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(12, 2, 12, 0), WrapContents = false };
        header.Controls.Add(new Label { Text = "WORKSPACES / 工作区", AutoSize = true, Padding = new(0, 10, 20, 0) });
        var add = new Button { Text = "+ 保存当前视角", AutoSize = true, Enabled = library?.CanWrite == true };
        add.Click += (_, _) =>
        {
            var name = AskName("保存工作区", "工作区");
            if (name is null) return;
            library!.Add(name, camera); Render(true);
        };
        var manage = new Button { Text = "管理 / 存储…", AutoSize = true, Enabled = library is not null };
        manage.Click += (_, _) =>
        {
            using var dialog = new Form { Text = "工作区管理", ClientSize = new(820, 580), StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, Font = Font };
            var editor = new BookmarkControl(library!, () => camera, () => false, _ => "请关闭编辑器后选择总览中的工作区卡片。", () => { }) { Dock = DockStyle.Fill };
            dialog.Controls.Add(editor); ChromeTheme.Apply(dialog);
            dialog.TopMost = (TopLevelControl as Form)?.TopMost == true;
            dialog.ShowDialog(TopLevelControl as Form); Render(true);
        };
        filter.TextChanged += (_, _) => Render();
        header.Controls.AddRange([add, manage, filter]); Controls.Add(cards); Controls.Add(message); Controls.Add(header);
        Render(); ChromeTheme.Apply(this);
    }
    private void Render(bool showStorageMessage = false)
    {
        foreach (Control child in cards.Controls.Cast<Control>().ToArray()) child.Dispose();
        cards.Controls.Clear();
        AddCard("HOME / 原点", PointD.Zero, null);
        if (library is not null) foreach (var bookmark in library.Collection.Items.Where(b => b.Name.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            AddCard(bookmark.Name, bookmark.CameraPosition, bookmark.Id);
        message.Text = library is not null && (showStorageMessage || !library.CanWrite) ? library.Message : "工作区记录视角；窗口始终位于同一画布。右击卡片可重命名或删除。";
    }
    private void AddCard(string name, PointD position, Guid? id)
    {
        var button = new Button { Text = $"{name}\n{position.X:0} , {position.Y:0}", Size = new(180, 64), Margin = new(4), AccessibleName = "工作区 " + name };
        button.Click += (_, _) => JumpRequested?.Invoke(position);
        if (id is { } bookmarkId && library is not null)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("重命名", null, (_, _) => { var renamed = AskName("重命名工作区", name); if (renamed is not null) { library.Rename(bookmarkId, renamed); Render(true); } });
            menu.Items.Add("删除工作区", null, (_, _) => { library.Remove(bookmarkId); Render(true); });
            foreach (ToolStripItem item in menu.Items) item.Enabled = library.CanWrite;
            button.ContextMenuStrip = menu;
            button.Disposed += (_, _) => menu.Dispose();
        }
        ChromeTheme.Apply(button); button.FlatAppearance.BorderColor = position == camera ? ChromeTheme.Lime : ChromeTheme.Cyan;
        cards.Controls.Add(button);
    }
    private string? AskName(string title, string current)
    {
        using var dialog = new Form { Text = title, ClientSize = new(400, 136), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font };
        var field = new TextBox { Text = current, Location = new(16, 16), Width = 368, MaxLength = 80 };
        var save = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new(280, 72), Size = new(104, 40) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new(168, 72), Size = new(104, 40) };
        dialog.Controls.AddRange([field, save, cancel]); dialog.AcceptButton = save; dialog.CancelButton = cancel; ChromeTheme.Apply(dialog);
        dialog.TopMost = (TopLevelControl as Form)?.TopMost == true;
        dialog.Shown += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog(TopLevelControl as Form) == DialogResult.OK && !string.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
    }
}
