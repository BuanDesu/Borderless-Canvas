using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class WorkspaceStrip : UserControl
{
    private readonly BookmarkLibrary? library;
    private readonly PointD camera;
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new(12, 0, 12, 0) };
    private readonly Label message = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new(24, 2, 0, 0), Visible = false };
    private readonly ToolTip hints = new();
    internal event Action<PointD>? JumpRequested;
    internal WorkspaceStrip(BookmarkLibrary? library, PointD camera)
    {
        this.library = library; this.camera = camera;
        cards.SizeChanged += (_, _) =>
        { foreach (Control card in cards.Controls) { var height = Math.Clamp(cards.ClientSize.Height - 26, 46, 96); if (card.Height != height) card.Height = height; } };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(24, 0, 12, 0), WrapContents = false };
        header.Controls.Add(new Label { Text = "工作区", AutoSize = true, Padding = new(0, 12, 16, 0) });
        var add = new Button { Text = "+", AccessibleName = "保存当前视角", Size = new(44, 44), Enabled = library?.CanWrite == true };
        add.Click += (_, _) =>
        {
            var name = AskName("保存工作区", "工作区");
            if (name is null) return;
            library!.Add(name, camera); Render(true);
        };
        var manage = new Button { Text = "•••", AccessibleName = "管理工作区", Size = new(44, 44), Enabled = library is not null };
        manage.Click += (_, _) =>
        {
            using var dialog = new Form { Text = "工作区管理", ClientSize = new(820, 580), StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, Font = Font };
            var editor = new BookmarkControl(library!, () => camera, () => false, _ => "请关闭编辑器后选择总览中的工作区卡片。", () => { }) { Dock = DockStyle.Fill };
            dialog.Controls.Add(editor); ChromeTheme.Apply(dialog);
            dialog.TopMost = (TopLevelControl as Form)?.TopMost == true;
            dialog.ShowDialog(TopLevelControl as Form); Render(true);
        };
        hints.SetToolTip(add, "将当前视角保存为工作区"); hints.SetToolTip(manage, "搜索、编辑与存储");
        header.Controls.AddRange([add, manage]); Controls.Add(cards); Controls.Add(message); Controls.Add(header);
        Render(); ChromeTheme.Apply(this);
    }
    private void Render(bool showStorageMessage = false)
    {
        foreach (Control child in cards.Controls.Cast<Control>().ToArray()) child.Dispose();
        cards.Controls.Clear();
        AddCard("原点", PointD.Zero, null);
        if (library is not null) foreach (var bookmark in library.Collection.Items)
            AddCard(bookmark.Name, bookmark.CameraPosition, bookmark.Id);
        message.Text = library is not null && (showStorageMessage || !library.CanWrite) ? library.Message : "工作区记录视角；窗口始终位于同一画布。右击卡片可重命名或删除。";
        message.Visible = library is { CanWrite: false };
    }
    private void AddCard(string name, PointD position, Guid? id)
    {
        var button = new Button { Text = name, Size = new(188, 86), Margin = new(8, 3, 8, 12), AccessibleName = "工作区 " + name };
        hints.SetToolTip(button, $"{name} · ({position.X:0}, {position.Y:0})\n右键重命名或删除");
        button.Click += (_, _) => JumpRequested?.Invoke(position);
        if (id is { } bookmarkId && library is not null)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("重命名", null, (_, _) => { var renamed = AskName("重命名工作区", name); if (renamed is not null) { library.Rename(bookmarkId, renamed); Render(true); } });
            menu.Items.Add("删除工作区", null, (_, _) => { library.Remove(bookmarkId); Render(true); });
            foreach (ToolStripItem item in menu.Items) item.Enabled = library.CanWrite;
            button.ContextMenuStrip = menu;
            ChromeTheme.StyleMenu(menu);
            button.Disposed += (_, _) => menu.Dispose();
        }
        ChromeTheme.Apply(button);
        button.Paint += (_, e) =>
        {
            e.Graphics.Clear(button.BackColor);
            ChromeTheme.Glass(e.Graphics, new RectangleF(4, 2, button.Width - 8, button.Height - 10), ChromeTheme.Purple, position == camera);
            var iconSize = Math.Min(32, Math.Max(18, button.Height - 22));
            ChromeTheme.Mark(e.Graphics, new(16, (button.Height - iconSize) / 2 - 3, iconSize, iconSize));
            TextRenderer.DrawText(e.Graphics, name, button.Font, new Rectangle(58, 10, button.Width - 70, button.Height - 24), ChromeTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        cards.Controls.Add(button);
    }
    protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
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
