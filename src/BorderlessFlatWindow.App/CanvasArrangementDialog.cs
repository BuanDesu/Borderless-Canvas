using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

/// <summary>Geometry-only preview. No desktop capture, movement or recovery ownership.</summary>
internal sealed class CanvasArrangementDialog : Form
{
    private readonly IReadOnlyList<CanvasWindow> source;
    private readonly RectD region;
    private readonly CheckedListBox selection = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly ChromeButton apply = new() { Text = "确认整理", DialogResult = DialogResult.OK, Size = new(124, 42) };
    private readonly WrapLabel summary = new() { Dock = DockStyle.Bottom, Height = 58, Padding = new(0, 8, 0, 4) };
    private readonly CheckBox before = new() { Text = "显示整理前", Dock = DockStyle.Top, Height = 34 };
    private readonly ArrangementPreview preview;
    private readonly bool showWithoutActivation;
    private IReadOnlyList<CanvasWindow> targets = [];
    protected override bool ShowWithoutActivation => showWithoutActivation;
    internal Guid[] SelectedIds => selection.CheckedItems.Cast<Choice>().Select(c => c.Window.Id).ToArray();
    internal bool CanApply => apply.Enabled;
    internal DialogResult ShowFor(Control anchor)
    {
        var host = anchor.FindForm(); TopMost = host?.TopMost == true;
        return ShowDialog(host ?? (IWin32Window)anchor);
    }
    private sealed record Choice(CanvasWindow Window) { public override string ToString() => Window.Title; }

    internal CanvasArrangementDialog(IReadOnlyList<CanvasWindow> windows, RectD region, IReadOnlyList<RectD> visibleRegions, bool showWithoutActivation = false)
    {
        source = windows.ToArray(); this.region = region; this.showWithoutActivation = showWithoutActivation;
        Text = "平铺窗口"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false; MinimumSize = new(760, 460); ClientSize = new(880, 550);
        var content = new Panel { Dock = DockStyle.Fill, Padding = new(18, 10, 18, 0) };
        var heading = new WrapLabel { Text = "勾选要整理的窗口，确认后从当前主屏视角按行排布。\n保持原生尺寸，空间不足时向画布下方延伸。", Dock = DockStyle.Top, Height = 64, Padding = new(0, 4, 0, 8) };
        var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new(0, 0, 14, 0) };
        left.Controls.Add(selection);
        left.Controls.Add(new Label { Text = "要整理的窗口", Dock = DockStyle.Top, Height = 32, Padding = new(0, 4, 0, 0) });
        var right = new Panel { Dock = DockStyle.Fill };
        preview = new(source, region) { Dock = DockStyle.Fill };
        right.Controls.Add(preview); right.Controls.Add(before); right.Controls.Add(summary);
        content.Controls.Add(right); content.Controls.Add(left); content.Controls.Add(heading);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.RightToLeft, Padding = new(12) };
        var cancel = new ChromeButton { Text = "取消", DialogResult = DialogResult.Cancel, Size = new(104, 42) };
        actions.Controls.AddRange([apply, cancel]); Controls.Add(content); Controls.Add(actions);
        AcceptButton = apply; CancelButton = cancel;
        foreach (var window in source.OrderBy(w => w.Bounds.Position.Y).ThenBy(w => w.Bounds.Position.X))
            selection.Items.Add(new Choice(window), visibleRegions.Any(r => r.Contains(window.Bounds.Center)));
        selection.ItemCheck += (_, _) => { if (IsHandleCreated && !IsDisposed && !Disposing) BeginInvoke(RefreshPreview); };
        before.CheckedChanged += (_, _) => RefreshPreview();
        ChromeTheme.Apply(this); ChromeTheme.Accent(apply, ChromeTheme.Purple); RefreshPreview();
    }
    private void RefreshPreview()
    {
        if (IsDisposed || Disposing) return;
        var ids = SelectedIds;
        var valid = RegionTiling.TryPlan(source, ids, region, out targets);
        var changed = valid ? targets.Count(w => source.Single(old => old.Id == w.Id).Bounds != w.Bounds) : 0;
        apply.Enabled = valid && changed > 0;
        var outside = targets.Count(w => !NearbyWindowPlacement.FitsInside(w.Bounds, region));
        summary.Text = ids.Length == 0 ? "勾选窗口以查看布局。默认勾选中心位于当前各屏视区的窗口。"
            : !valid ? "无法为当前选择生成布局，请减少选择或调整视角。"
            : $"已选 {ids.Length} · 调整 {changed} · 淡色为未选窗口"
                + (outside > 0 ? $"\n{outside} 个位置延伸到主屏视区外，可从空间总览定位。" : "\n未选窗口保持原位；取消不会移动窗口。");
        preview.SetLayout(before.Checked || !valid ? source : source.Where(w => !ids.Contains(w.Id)).Concat(targets).ToArray(), ids);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK) { RefreshPreview(); if (!apply.Enabled) e.Cancel = true; }
        base.OnFormClosing(e);
    }

    private sealed class ArrangementPreview(IReadOnlyList<CanvasWindow> initial, RectD region) : Control
    {
        private IReadOnlyList<CanvasWindow> windows = initial;
        private HashSet<Guid> chosen = [];
        internal void SetLayout(IReadOnlyList<CanvasWindow> next, IEnumerable<Guid> ids) { windows = next; chosen = ids.ToHashSet(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 80 || Height < 80) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var projection = OverviewProjection.Fit(windows.Where(w => chosen.Contains(w.Id)).Select(w => w.Bounds).Append(region), new(Width, Height), padding: 22);
            ChromeTheme.Grid(e.Graphics, ClientRectangle);
            foreach (var window in windows.OrderBy(w => chosen.Contains(w.Id)))
            {
                var projected = projection.ToMap(window.Bounds);
                var rect = new RectangleF((float)projected.Position.X, (float)projected.Position.Y, (float)projected.Size.Width, (float)projected.Size.Height);
                if (!float.IsFinite(rect.X) || !float.IsFinite(rect.Y) || !float.IsFinite(rect.Width) || !float.IsFinite(rect.Height)
                    || rect.Width < 1 || rect.Height < 1 || !rect.IntersectsWith(ClientRectangle)) continue;
                var selected = chosen.Contains(window.Id);
                ChromeTheme.Glass(e.Graphics, rect, selected ? ChromeTheme.Purple : Color.FromArgb(75, ChromeTheme.Muted), selected);
                if (rect.Width > 36 && rect.Height > 18) TextRenderer.DrawText(e.Graphics, window.Title, Font, Rectangle.Round(rect),
                    selected ? ChromeTheme.Text : ChromeTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            var viewport = projection.ToMap(region); using var pen = new Pen(ChromeTheme.Muted) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, (float)viewport.Position.X, (float)viewport.Position.Y, (float)viewport.Size.Width, (float)viewport.Size.Height);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DoubleBuffered = true; ResizeRedraw = true; }
    }
}
