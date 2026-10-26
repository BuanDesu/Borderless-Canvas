using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class BookmarkControl : UserControl
{
    private readonly BookmarkLibrary library;
    private readonly Func<PointD> camera;
    private readonly Func<bool> canJump;
    private readonly Func<Guid, string> jump;
    private readonly Action endNavigation;
    private readonly TextBox name = new() { Width = 250, MaxLength = BookmarkCollection.MaxNameLength, PlaceholderText = "标签名称（允许重名）", AccessibleName = "标签名称" };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索标签名称；不移动视角", AccessibleName = "搜索位置标签" };
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, AccessibleName = "位置标签列表" };
    private readonly Label position = new() { Dock = DockStyle.Top, Height = 32, Padding = new(12, 2, 12, 2) };
    private readonly TextBox status = new() { Dock = DockStyle.Bottom, Height = 112, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "标签状态" };
    private readonly ChromeButton add = new() { Text = "保存当前位置", AutoSize = true };
    private readonly ChromeButton rename = new() { Text = "重命名", AutoSize = true };
    private readonly ChromeButton remove = new() { Text = "删除标签", AutoSize = true };
    private readonly ChromeButton go = new() { Text = "跳转", AutoSize = true };
    private readonly ChromeButton reset = new() { Text = "备份原文件并新建", AutoSize = true };
    private Guid? SelectedId => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as Guid? : null;
    private bool refreshing;

    internal BookmarkControl(BookmarkLibrary library, Func<PointD> camera, Func<bool> canJump, Func<Guid, string> jump, Action endNavigation)
    {
        this.library = library; this.camera = camera; this.canJump = canJump; this.jump = jump; this.endNavigation = endNavigation;
        var heading = new Label { Dock = DockStyle.Top, Height = 84, Padding = new(12), Text = "位置标签保存共享画布上的相机位置；不会隐藏窗口。\n重名标签使用坐标和 ID 区分，删除不会移动窗口。" };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new(10), AutoScroll = true };
        actions.Controls.AddRange([name, add, rename, remove, go]);
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new(12, 8, 12, 8) }; searchPanel.Controls.Add(search);
        var files = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new(10, 4, 10, 4), AutoScroll = true };
        var reload = new ChromeButton { Text = "重新读取标签", AutoSize = true }; files.Controls.AddRange([reload, reset]);
        list.Columns.Add("名称", 260); list.Columns.Add("相机坐标 (X, Y)", 280); list.Columns.Add("稳定 ID", 120);
        search.TextChanged += (_, _) => RefreshList(SelectedId);
        name.Enter += (_, _) => endNavigation(); search.Enter += (_, _) => endNavigation();
        list.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            var selected = library.Collection.Items.FirstOrDefault(entry => entry.Id == SelectedId);
            if (selected is not null) name.Text = selected.Name;
            RefreshNavigationState();
        };
        list.MouseDoubleClick += (_, e) => { if (list.GetItemAt(e.X, e.Y) is not null) Go(); };
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Go(); e.Handled = true; e.SuppressKeyPress = true; } };
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Go(); e.Handled = true; e.SuppressKeyPress = true; } };
        add.Click += (_, _) =>
        {
            endNavigation(); var added = library.Add(name.Text, camera());
            if (added is not null) { search.Clear(); RefreshList(added.Id); } else RefreshMessage();
        };
        rename.Click += (_, _) =>
        {
            endNavigation(); var id = SelectedId;
            if (id is not null) library.Rename(id.Value, name.Text);
            RefreshList(id);
        };
        remove.Click += (_, _) =>
        {
            endNavigation(); var id = SelectedId;
            if (id is not null) library.Remove(id.Value);
            RefreshList(id);
        };
        go.Click += (_, _) => Go();
        reload.Click += (_, _) => { endNavigation(); library.Reload(); RefreshList(SelectedId); };
        reset.Click += (_, _) => { endNavigation(); library.BackupAndReset(); RefreshList(null); };
        Controls.Add(list); Controls.Add(searchPanel); Controls.Add(position); Controls.Add(actions); Controls.Add(heading); Controls.Add(status); Controls.Add(files);
        RefreshList(null);
    }
    private void Go()
    {
        if (!canJump() || SelectedId is not { } id) return;
        endNavigation(); status.Text = jump(id); RefreshNavigationState();
    }
    private void RefreshList(Guid? selected)
    {
        refreshing = true; list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var entry in library.Collection.Search(search.Text))
            {
                var item = new ListViewItem([entry.Name, $"({entry.CameraPosition.X:G12}, {entry.CameraPosition.Y:G12})", entry.Id.ToString("N")[..8]]) { Tag = entry.Id };
                list.Items.Add(item); if (entry.Id == selected) item.Selected = true;
            }
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[0].Selected = true;
        }
        finally { list.EndUpdate(); refreshing = false; }
        var current = library.Collection.Items.FirstOrDefault(entry => entry.Id == SelectedId);
        if (current is not null) name.Text = current.Name;
        RefreshMessage();
    }
    private void RefreshMessage() { status.Text = library.Message; RefreshNavigationState(); }
    internal void RefreshNavigationState()
    {
        var point = camera(); position.Text = $"当前位置 ({point.X:G12}, {point.Y:G12})  ·  匹配 {list.Items.Count} / {library.Collection.Items.Count}";
        add.Enabled = library.CanWrite; rename.Enabled = remove.Enabled = library.CanWrite && SelectedId is not null;
        go.Enabled = canJump() && SelectedId is not null; reset.Visible = library.CanBackupAndReset;
    }

}
