using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed class SpatialOverviewForm : Form
{
    private readonly SpatialOverviewSession model;
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "搜索窗口标题；保持原有空间位置", Margin = new(12) };
    private readonly Label summary = new() { Dock = DockStyle.Bottom, Height = 96, Padding = new(12) };
    private readonly Button confirm = new() { Text = "定位选中窗口", AutoSize = true };
    private readonly OverviewMap map;
    private readonly bool showWithoutActivation;
    private bool canConfirm = true;
    private string? managementNotice;
    internal CanvasWindow? ConfirmedTarget { get; private set; }

    internal SpatialOverviewForm(IEnumerable<CanvasWindow> windows, Camera2D camera, Rectangle bounds, Guid? selected = null, bool showWithoutActivation = false)
    {
        this.showWithoutActivation = showWithoutActivation;
        model = new(windows, camera, new(1000, 700), selected);
        Text = "BFW 空间总览"; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None;
        Bounds = bounds; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 11); BackColor = Color.FromArgb(20, 25, 35); ForeColor = Color.White;
        map = new(model) { Dock = DockStyle.Fill };
        map.ConfirmRequested += ConfirmSelection;
        var heading = new Label { Dock = DockStyle.Top, Height = 54, Padding = new(16), Text = "空间总览  ·  方向键选择  ·  单击 / Enter 确认  ·  Esc 取消" };
        var searchContainer = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new(16, 8, 16, 8) };
        searchContainer.Controls.Add(search);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new(12, 4, 12, 4) };
        var fit = new Button { Text = "适配全部（F5）", AutoSize = true };
        var cancel = new Button { Text = "取消（Esc）", AutoSize = true };
        confirm.Click += (_, _) => ConfirmSelection(); cancel.Click += (_, _) => CancelOverview();
        fit.Click += (_, _) => { map.Refit(); RefreshSelection(); };
        toolbar.Controls.AddRange([confirm, fit, cancel]);
        search.TextChanged += (_, _) => { model.Search(search.Text); RefreshSelection(); };
        Controls.Add(map); Controls.Add(summary); Controls.Add(toolbar); Controls.Add(searchContainer); Controls.Add(heading);
        RefreshSelection();
    }
    protected override bool ShowWithoutActivation => showWithoutActivation;
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); map.Refit(); if (!showWithoutActivation) search.Focus();
    }
    internal void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message)
    {
        if (IsDisposed || model.Status != OverviewStatus.Open) return;
        var next = windows.ToArray();
        if (!model.Windows.SequenceEqual(next)) model.Synchronize(next);
        canConfirm = running; managementNotice = running ? null : "管理已暂停，暂不可跳转。" + message;
        RefreshSelection();
    }
    private void RefreshSelection()
    {
        var selected = model.Windows.FirstOrDefault(window => window.Id == model.SelectedId);
        confirm.Enabled = canConfirm && selected is not null;
        summary.Text = $"匹配 {model.Matches.Count} / {model.Windows.Count}  ·  已选：{selected?.Title ?? "无匹配窗口"}\n"
            + (managementNotice ?? $"确认前视角保持 ({model.StartCamera.Position.X:0}, {model.StartCamera.Position.Y:0})；Tab 可轮选重叠窗口，Ctrl+F 搜索。");
        map.Invalidate();
    }
    private void ConfirmSelection()
    {
        if (!canConfirm || model.Confirm() is not { } target) return;
        ConfirmedTarget = target; DialogResult = DialogResult.OK; Close();
    }
    internal void CancelOverview()
    { model.Cancel(); DialogResult = DialogResult.Cancel; Close(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (ConfirmedTarget is null) model.Cancel();
        base.OnFormClosing(e);
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
        => HandleCommand(keys) || base.ProcessCmdKey(ref message, keys);
    internal bool HandleCommand(Keys keys)
    {
        switch (keys)
        {
            case Keys.Escape: CancelOverview(); return true;
            case Keys.Enter: ConfirmSelection(); return true;
            case Keys.Left: model.MoveSelection(OverviewDirection.Left); break;
            case Keys.Right: model.MoveSelection(OverviewDirection.Right); break;
            case Keys.Up: model.MoveSelection(OverviewDirection.Up); break;
            case Keys.Down: model.MoveSelection(OverviewDirection.Down); break;
            case Keys.Tab: model.Cycle(1); break;
            case Keys.Shift | Keys.Tab: model.Cycle(-1); break;
            case Keys.Control | Keys.F: search.Focus(); search.SelectAll(); return true;
            case Keys.F5: map.Refit(); break;
            default: return false;
        }
        RefreshSelection(); return true;
    }
    internal void SetSmokeQuery(string text) => search.Text = text;

    private sealed class OverviewMap(SpatialOverviewSession model) : Control
    {
        internal event Action? ConfirmRequested;
        protected override void OnCreateControl() { base.OnCreateControl(); DoubleBuffered = true; ResizeRedraw = true; AccessibleName = "空间地图"; }
        internal void Refit()
        {
            if (Width > 128 && Height > 128) model.Refit(new(Width, Height));
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Refit(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && model.HitTest(new(e.X, e.Y)) is { } id && model.Select(id)) ConfirmRequested?.Invoke();
        }
        private RectangleF? VisibleRect(RectD world)
        {
            var rectangle = model.Projection.ToMap(world);
            var left = Math.Max(0, rectangle.Position.X); var top = Math.Max(0, rectangle.Position.Y);
            var right = Math.Min(Width, rectangle.Right); var bottom = Math.Min(Height, rectangle.Bottom);
            return right <= left || bottom <= top ? null : new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(Color.FromArgb(20, 25, 35)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var window in model.Windows.OrderBy(window => window.Id == model.SelectedId))
            {
                if (VisibleRect(window.Bounds) is not { } rect) continue;
                var matched = model.IsMatch(window.Id); var selected = window.Id == model.SelectedId;
                using var fill = new SolidBrush(matched ? Color.FromArgb(45, 71, 93) : Color.FromArgb(28, 34, 43));
                using var border = new Pen(selected ? Color.Gold : matched ? Color.FromArgb(102, 150, 181) : Color.FromArgb(48, 58, 70), selected ? 3 : 1);
                e.Graphics.FillRectangle(fill, rect); e.Graphics.DrawRectangle(border, rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
                if (rect.Width > 50 && rect.Height > 24)
                    TextRenderer.DrawText(e.Graphics, window.Title + (rect.Height > 75 ? "\n标题视图 · 可定位" : ""), Font, Rectangle.Round(rect),
                        matched ? Color.White : Color.FromArgb(90, 100, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            if (VisibleRect(model.StartCamera.Bounds) is { } view)
            {
                using var pen = new Pen(Color.FromArgb(111, 231, 196), 2) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(pen, view.X, view.Y, view.Width, view.Height);
            }
            var origin = model.Projection.ToMap(PointD.Zero);
            if (origin.X >= 0 && origin.X <= Width && origin.Y >= 0 && origin.Y <= Height)
            {
                using var pen = new Pen(Color.Gold, 2);
                e.Graphics.DrawLine(pen, (float)origin.X - 7, (float)origin.Y, (float)origin.X + 7, (float)origin.Y);
                e.Graphics.DrawLine(pen, (float)origin.X, (float)origin.Y - 7, (float)origin.X, (float)origin.Y + 7);
            }
            TextRenderer.DrawText(e.Graphics, "虚线：当前视口    十字：画布原点", Font, new Point(12, 8), Color.FromArgb(173, 213, 211));
        }
        protected override AccessibleObject CreateAccessibilityInstance() => new MapAccessible(this, model);
        private sealed class MapAccessible(OverviewMap owner, SpatialOverviewSession model) : ControlAccessibleObject(owner)
        {
            private readonly Dictionary<Guid, AccessibleObject> cards = [];
            public override AccessibleRole Role => AccessibleRole.Pane;
            public override int GetChildCount() => model.Windows.Count;
            public override AccessibleObject? GetChild(int index)
            {
                if (index < 0 || index >= model.Windows.Count) return null;
                var id = model.Windows[index].Id;
                if (!cards.TryGetValue(id, out var card)) cards[id] = card = new CardAccessible(owner, model, id, this);
                return card;
            }
        }
        private sealed class CardAccessible(OverviewMap owner, SpatialOverviewSession model, Guid id, AccessibleObject parent) : AccessibleObject
        {
            public override string? Name { get => model.Windows.FirstOrDefault(window => window.Id == id)?.Title ?? "窗口已关闭"; set { } }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override AccessibleObject Parent => parent;
            public override string DefaultAction => "定位窗口";
            public override AccessibleStates State => model.IsMatch(id) ? AccessibleStates.Focusable | AccessibleStates.Selectable
                | (id == model.SelectedId ? AccessibleStates.Selected : AccessibleStates.None) : AccessibleStates.Unavailable;
            public override Rectangle Bounds => model.Windows.FirstOrDefault(window => window.Id == id) is { } window && owner.VisibleRect(window.Bounds) is { } rect
                ? owner.RectangleToScreen(Rectangle.Round(rect)) : Rectangle.Empty;
            public override void DoDefaultAction() { if (model.Select(id)) owner.ConfirmRequested?.Invoke(); }
        }
    }
}
