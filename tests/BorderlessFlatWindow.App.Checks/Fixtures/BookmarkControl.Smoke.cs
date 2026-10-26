using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class BookmarkControl
{
    internal void SetSmokeQuery(string query) => search.Text = query;
    internal void RefreshSmoke() => RefreshList(null);
    internal void CheckSmokeCommands()
    {
        search.Clear(); name.Text = "组件测试标签"; add.PerformClick();
        var entry = library.Collection.Items.Single(item => item.Name == "组件测试标签");
        name.Text = "组件重命名"; rename.PerformClick();
        if (library.Collection.Items.Single(item => item.Id == entry.Id).Name != "组件重命名") throw new InvalidOperationException("Label rename command failed.");
        remove.PerformClick();
        if (library.Collection.Items.Any(item => item.Id == entry.Id)) throw new InvalidOperationException("Label deletion command failed.");
        var first = library.Collection.Items.FirstOrDefault();
        if (first is not null)
        {
            name.Text = "先前输入"; search.Text = first.Name;
            if (name.Text != first.Name) throw new InvalidOperationException("Label query selection left a stale name field.");
        }
    }}
