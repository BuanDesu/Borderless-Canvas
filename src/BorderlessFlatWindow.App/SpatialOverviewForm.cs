using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.App;

internal sealed class SpatialOverviewForm : Form
{
    internal sealed record PreviewSource(Guid Id, NativeWindowIdentity Identity, bool FullyOnscreen);
    private readonly SpatialOverviewSession model;
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索窗口", AccessibleName = "搜索窗口" };
    private readonly Label summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, Padding = new(6, 0, 12, 0) };
    private readonly Button confirm = new() { Text = "定位选中窗口", AutoSize = true };
    private readonly OverviewMap map;
    private readonly bool showWithoutActivation;
    private bool canConfirm = true;
    private string? managementNotice;
    private readonly CheckBox showPreviews = new() { Text = "窗口预览（可能停帧）", AutoSize = true, Checked = true };
    private IReadOnlyDictionary<Guid, PreviewSource> previewSources = new Dictionary<Guid, PreviewSource>();
    private OverviewPreviewSession? previews;
    private Control? previewHost;
    private nint previewDestination;
    private bool closing, refreshingPreviews;
    internal CanvasWindow? ConfirmedTarget { get; private set; }
    internal PointD? ConfirmedCameraPosition { get; private set; }
    private readonly WorkspaceStrip workspaces;
    private Bitmap? backdrop;
    private readonly Rectangle backdropBounds;
    private readonly ContextMenuStrip options = new();
    private readonly ToolTip hints = new();

    internal SpatialOverviewForm(IEnumerable<CanvasWindow> windows, Camera2D camera, Rectangle bounds, Guid? selected = null, bool showWithoutActivation = false,
        BookmarkLibrary? bookmarks = null, IEnumerable<RectD>? viewports = null)
    {
        this.showWithoutActivation = showWithoutActivation;
        backdropBounds = bounds;
        model = new(windows, camera, new(1000, 700), selected);
        Text = "BFW 空间总览"; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None;
        Bounds = bounds; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 11); BackColor = Color.FromArgb(20, 25, 35); ForeColor = Color.White;
        map = new(model, viewports?.ToArray() ?? [camera.Bounds]) { Dock = DockStyle.Fill, PaintBackdrop = PaintScene };
        map.GeometryChanged += RefreshPreviews;
        map.ConfirmRequested += ConfirmSelection;
        var searchContainer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        searchContainer.RowStyles.Add(new(SizeType.Percent, 100));
        searchContainer.ColumnStyles.Add(new(SizeType.Percent, 100)); searchContainer.ColumnStyles.Add(new(SizeType.Absolute, Math.Clamp(bounds.Width / 3, 240, 420)));
        searchContainer.ColumnStyles.Add(new(SizeType.Absolute, 48)); searchContainer.ColumnStyles.Add(new(SizeType.Absolute, 48));
        var searchSurface = new GlassSearchSurface { Dock = DockStyle.Fill, Padding = new(14, 10, 14, 6) }; searchSurface.Controls.Add(search);
        var more = new Button { Text = "•••", AccessibleName = "总览选项", Dock = DockStyle.Fill };
        var cancel = new Button { Text = "×", AccessibleName = "关闭总览", Dock = DockStyle.Fill };
        confirm.Click += (_, _) => ConfirmSelection(); cancel.Click += (_, _) => CancelOverview();
        options.Items.Add("适配全部    F5", null, (_, _) => { previews?.RetryFailed(); map.Refit(); RefreshSelection(); });
        var previewsToggle = new ToolStripMenuItem("窗口预览") { Checked = true, CheckOnClick = true };
        previewsToggle.CheckedChanged += (_, _) => showPreviews.Checked = previewsToggle.Checked;
        options.Items.Add(previewsToggle);
        var viewportToggle = new ToolStripMenuItem("显示当前视野") { CheckOnClick = true };
        viewportToggle.CheckedChanged += (_, _) => { map.ShowViewport = viewportToggle.Checked; map.Invalidate(); };
        options.Items.Add(viewportToggle);
        options.Items.Add("操作提示", null, (_, _) => MessageBox.Show(TopLevelControl as Form, "方向键 / Tab 选择窗口，Enter 定位，Esc 返回。\n输入标题筛选，下方卡片切换工作区。", "空间总览"));
        more.Click += (_, _) => options.Show(more, new Point(0, more.Height));
        ChromeTheme.StyleMenu(options);
        showPreviews.CheckedChanged += (_, _) => RefreshSelection();
        searchContainer.Controls.Add(summary, 0, 0); searchContainer.Controls.Add(searchSurface, 1, 0);
        searchContainer.Controls.Add(more, 2, 0); searchContainer.Controls.Add(cancel, 3, 0);
        hints.SetToolTip(cancel, "关闭 · Esc"); hints.SetToolTip(more, "适配、预览与操作提示");
        search.TextChanged += (_, _) => { model.Search(search.Text); RefreshSelection(); };
        workspaces = new(bookmarks, camera.Position, () => model.Windows, camera.ViewportSize, searchContainer) { Dock = DockStyle.Bottom, Height = Math.Clamp(bounds.Height / 5, 210, 320) };
        workspaces.JumpRequested += position => { if (!canConfirm) return; ConfirmedCameraPosition = position; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(map); Controls.Add(workspaces);
        Resize += (_, _) =>
        {
            workspaces.Height = Math.Clamp(ClientSize.Height / 5, 210, 320);
            searchContainer.ColumnStyles[1].Width = Math.Clamp(ClientSize.Width / 3, 210, 420);
        };
        ChromeTheme.Apply(this);
        MakeTransparent(workspaces);
        search.BorderStyle = BorderStyle.None;
        RefreshSelection();
    }
    protected override bool ShowWithoutActivation => showWithoutActivation;
    private static void MakeTransparent(Control root)
    {
        if (root is Panel or UserControl or Button or Label) root.BackColor = Color.Transparent;
        foreach (Control child in root.Controls) MakeTransparent(child);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => PaintScene(e.Graphics, this);
    private void PaintScene(Graphics graphics, Control target)
    {
        var location = ReferenceEquals(target, this) ? Point.Empty : PointToClient(target.PointToScreen(Point.Empty));
        var saved = graphics.Save(); graphics.TranslateTransform(-location.X, -location.Y);
        ChromeTheme.Grid(graphics, ClientRectangle);
        if (backdrop is not null) graphics.DrawImage(backdrop, ClientRectangle);
        graphics.Restore(saved);
    }
    internal void CaptureBackdrop()
    {
        if (showWithoutActivation) return;
        backdrop?.Dispose(); backdrop = FrostedBackdrop.Capture(backdropBounds); Invalidate(true);
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); map.Refit(); RefreshPreviews(); if (!showWithoutActivation) search.Focus();
    }
    internal void SetPreviewSources(IEnumerable<PreviewSource> sources)
    { previewSources = sources.ToDictionary(source => source.Id); RefreshPreviews(); }
    internal void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message)
    {
        if (IsDisposed || model.Status != OverviewStatus.Open) return;
        var next = windows.ToArray();
        if (!model.Windows.SequenceEqual(next)) { model.Synchronize(next); workspaces.Invalidate(true); }
        canConfirm = running; managementNotice = running ? null : "管理已暂停，暂不可跳转。" + message;
        RefreshSelection();
    }
    private void RefreshSelection()
    {
        var selected = model.Windows.FirstOrDefault(window => window.Id == model.SelectedId);
        confirm.Enabled = canConfirm && selected is not null;
        summary.Text = managementNotice ?? (model.Matches.Count == 0 ? "没有匹配的窗口" : $"{selected?.Title ?? ""}  ·  Enter 打开");
        map.Invalidate();
        RefreshPreviews();
    }
    private void RefreshPreviews()
    {
        if (closing || refreshingPreviews || IsDisposed || !Visible || !IsHandleCreated || !map.IsHandleCreated) return;
        refreshingPreviews = true;
        try { RefreshPreviewsCore(); }
        finally { refreshingPreviews = false; }
    }
    private void RefreshPreviewsCore()
    {
        var host = TopLevelControl;
        if (host is null || !host.IsHandleCreated) return;
        if (!ReferenceEquals(previewHost, host) || previewDestination != host.Handle)
        {
            ReleasePreviews(); previewHost = host; previewDestination = host.Handle;
            host.HandleDestroyed += PreviewHostDestroyed;
            previews = new(new DwmOverviewPreviewPort(previewDestination));
        }
        var current = previews!;
        current.Synchronize(map.PreviewCards(previewSources, host), model.SelectedId, showPreviews.Checked && canConfirm);
        if (closing || !ReferenceEquals(previews, current)) return;
        map.PreviewStates = current.States;
        map.Invalidate();
    }
    private void PreviewHostDestroyed(object? sender, EventArgs e) => ReleasePreviews();
    private void ReleasePreviews()
    {
        previews?.Dispose(); previews = null;
        if (previewHost is not null) previewHost.HandleDestroyed -= PreviewHostDestroyed;
        previewHost = null; previewDestination = 0;
        map.PreviewStates = new Dictionary<Guid, PreviewState>();
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
        closing = true; ReleasePreviews();
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { closing = true; ReleasePreviews(); options.Dispose(); hints.Dispose(); confirm.Dispose(); showPreviews.Dispose(); backdrop?.Dispose(); backdrop = null; } base.Dispose(disposing); }
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
            case Keys.F5: previews?.RetryFailed(); map.Refit(); break;
            default: return false;
        }
        RefreshSelection(); return true;
    }
    internal void SetSmokeQuery(string text) => search.Text = text;

    private sealed class GlassSearchSurface : Panel
    {
        internal GlassSearchSurface() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); ChromeTheme.Glass(e.Graphics, new RectangleF(2, 2, Width - 4, Height - 6), ChromeTheme.Purple); }
    }

    private sealed class OverviewMap(SpatialOverviewSession model, RectD[] viewports) : Control
    {
        internal Action<Graphics, Control>? PaintBackdrop;
        internal bool ShowViewport;
        internal event Action? ConfirmRequested;
        internal event Action? GeometryChanged;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IReadOnlyDictionary<Guid, PreviewState> PreviewStates { get; set; } = new Dictionary<Guid, PreviewState>();
        protected override void OnCreateControl() { base.OnCreateControl(); DoubleBuffered = true; ResizeRedraw = true; AccessibleName = "空间地图"; }
        internal void Refit()
        {
            if (Width > 128 && Height > 128) model.Refit(new(Width, Height));
            Invalidate();
            GeometryChanged?.Invoke();
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
        internal IEnumerable<PreviewCard> PreviewCards(IReadOnlyDictionary<Guid, PreviewSource> sources, Control host)
        {
            var rectangles = model.Windows.ToDictionary(window => window.Id, window => model.Projection.ToMap(window.Bounds));
            var origin = model.Projection.ToMap(PointD.Zero);
            foreach (var window in model.Windows)
            {
                var rect = rectangles[window.Id];
                var matched = model.IsMatch(window.Id);
                sources.TryGetValue(window.Id, out var source);
                PreviewState? fallback = !matched ? PreviewState.Unmatched : source is null ? PreviewState.Unavailable
                    : !source.FullyOnscreen ? PreviewState.Offscreen : null;
                PixelRect? content = null;
                if (rect.Position.X < 0 || rect.Position.Y < 0 || rect.Right > Width || rect.Bottom > Height
                    || rect.Size.Width < 110 || rect.Size.Height < 110) fallback ??= PreviewState.TooSmall;
                else
                {
                    var body = Rectangle.FromLTRB((int)Math.Ceiling(rect.Position.X) + 4, (int)Math.Ceiling(rect.Position.Y) + 32,
                        (int)Math.Floor(rect.Right) - 4, (int)Math.Floor(rect.Bottom) - 28);
                    if (rectangles.Any(other => other.Key != window.Id && other.Value.Position.X < body.Right && other.Value.Right > body.Left
                        && other.Value.Position.Y < body.Bottom && other.Value.Bottom > body.Top)
                        || (origin.X + 8 > body.Left && origin.X - 8 < body.Right && origin.Y + 8 > body.Top && origin.Y - 8 < body.Bottom))
                        fallback ??= PreviewState.Overlap;
                    var point = host.PointToClient(PointToScreen(body.Location));
                    content = new(point.X, point.Y, point.X + body.Width, point.Y + body.Height);
                }
                yield return new(window.Id, source?.Identity, content, fallback);
            }
        }
        private string PreviewLabel(Guid id) => PreviewStates.GetValueOrDefault(id, PreviewState.Unavailable) switch
        {
            PreviewState.Preview => "应用画面 · 可能停帧",
            PreviewState.Disabled => "预览关闭 · 标题视图",
            PreviewState.Offscreen => "离屏 / 屏幕边缘 · 标题视图",
            PreviewState.Unmatched => "不匹配 · 标题视图",
            PreviewState.Overlap => "重叠区域 · 标题视图",
            PreviewState.TooSmall => "卡片过小 · 标题视图",
            PreviewState.Capacity => "预览数量限制 · 标题视图",
            _ => "预览不可用 · 标题视图"
        };
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (PaintBackdrop is not null) PaintBackdrop(e.Graphics, this); else ChromeTheme.Grid(e.Graphics, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (ShowViewport) foreach (var viewport in viewports)
                if (VisibleRect(viewport) is { } view)
                {
                    using var outline = new Pen(Color.FromArgb(45, ChromeTheme.Text), 1) { DashStyle = DashStyle.Dash };
                    e.Graphics.DrawRectangle(outline, view.X, view.Y, view.Width, view.Height);
                }
            foreach (var window in model.Windows.OrderBy(window => window.Id == model.SelectedId))
            {
                if (VisibleRect(window.Bounds) is not { } rect || rect.Width < 2 || rect.Height < 2) continue;
                var matched = model.IsMatch(window.Id); var selected = window.Id == model.SelectedId;
                var color = ChromeTheme.Candy[(window.Id.GetHashCode() & int.MaxValue) % ChromeTheme.Candy.Length];
                ChromeTheme.Glass(e.Graphics, rect, matched ? color : ChromeTheme.Muted, selected);
                if (rect.Width < 50 || rect.Height < 24) continue;
                var title = Rectangle.Round(rect); title.Height = Math.Min(title.Height, 30); title.Inflate(-6, 0);
                TextRenderer.DrawText(e.Graphics, window.Title, Font, title, matched ? ChromeTheme.Text : ChromeTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (PreviewStates.GetValueOrDefault(window.Id) != PreviewState.Preview && rect.Width > 80 && rect.Height > 100)
                {
                    var center = new Rectangle((int)(rect.X + rect.Width / 2 - 18), (int)(rect.Y + rect.Height / 2 - 12), 36, 36);
                    using var tile = ChromeTheme.Round(center, 10); using var brush = new SolidBrush(Color.FromArgb(40, color));
                    e.Graphics.FillPath(brush, tile);
                    TextRenderer.DrawText(e.Graphics, window.Title[..1], Font, center, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
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
