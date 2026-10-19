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
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "搜索窗口标题；保持原有空间位置", Margin = new(12) };
    private readonly Label summary = new() { Dock = DockStyle.Bottom, Height = 34, Padding = new(12, 4, 12, 0) };
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

    internal SpatialOverviewForm(IEnumerable<CanvasWindow> windows, Camera2D camera, Rectangle bounds, Guid? selected = null, bool showWithoutActivation = false,
        BookmarkLibrary? bookmarks = null, IEnumerable<RectD>? viewports = null)
    {
        this.showWithoutActivation = showWithoutActivation;
        backdropBounds = bounds;
        model = new(windows, camera, new(1000, 700), selected);
        Text = "BFW 空间总览"; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None;
        Bounds = bounds; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 11); BackColor = Color.FromArgb(20, 25, 35); ForeColor = Color.White;
        map = new(model, viewports?.ToArray() ?? [camera.Bounds]) { Dock = DockStyle.Fill };
        map.GeometryChanged += RefreshPreviews;
        map.ConfirmRequested += ConfirmSelection;
        var heading = new Label { Dock = DockStyle.Top, Height = 48, Padding = new(16, 12, 16, 0), Text = "SPATIAL OVERVIEW // 空间总览    ·    方向键 / Tab 选择    ·    Enter 定位    ·    Esc 取消" };
        var searchContainer = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new(16, 8, 16, 8) };
        searchContainer.Controls.Add(search);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new(12, 4, 12, 4) };
        var fit = new Button { Text = "适配 / 重试（F5）", AutoSize = true };
        var cancel = new Button { Text = "取消（Esc）", AutoSize = true };
        confirm.Click += (_, _) => ConfirmSelection(); cancel.Click += (_, _) => CancelOverview();
        fit.Click += (_, _) => { previews?.RetryFailed(); map.Refit(); RefreshSelection(); };
        showPreviews.CheckedChanged += (_, _) => RefreshSelection();
        toolbar.Controls.AddRange([confirm, fit, cancel, showPreviews]);
        search.TextChanged += (_, _) => { model.Search(search.Text); RefreshSelection(); };
        workspaces = new(bookmarks, camera.Position) { Dock = DockStyle.Bottom, Height = Math.Max(150, bounds.Height / 5) };
        workspaces.JumpRequested += position => { if (!canConfirm) return; ConfirmedCameraPosition = position; DialogResult = DialogResult.OK; Close(); };
        var upper = new Panel { Dock = DockStyle.Fill };
        upper.Controls.Add(map); upper.Controls.Add(summary); upper.Controls.Add(toolbar); upper.Controls.Add(searchContainer); upper.Controls.Add(heading);
        Controls.Add(upper); Controls.Add(workspaces);
        Resize += (_, _) => workspaces.Height = Math.Max(150, ClientSize.Height / 5);
        ChromeTheme.Apply(this);
        RefreshSelection();
    }
    protected override bool ShowWithoutActivation => showWithoutActivation;
    internal void CaptureBackdrop()
    {
        if (showWithoutActivation) return;
        backdrop = FrostedBackdrop.Capture(backdropBounds); map.Backdrop = backdrop;
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
        if (!model.Windows.SequenceEqual(next)) model.Synchronize(next);
        canConfirm = running; managementNotice = running ? null : "管理已暂停，暂不可跳转。" + message;
        RefreshSelection();
    }
    private void RefreshSelection()
    {
        var selected = model.Windows.FirstOrDefault(window => window.Id == model.SelectedId);
        confirm.Enabled = canConfirm && selected is not null;
        summary.Text = managementNotice ?? $"匹配 {model.Matches.Count} / {model.Windows.Count}  ·  已选：{selected?.Title ?? "无匹配窗口"}  ·  视角 ({model.StartCamera.Position.X:0}, {model.StartCamera.Position.Y:0})";
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
    { if (disposing) { closing = true; ReleasePreviews(); map.Backdrop = null; backdrop?.Dispose(); backdrop = null; } base.Dispose(disposing); }
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

    private sealed class OverviewMap(SpatialOverviewSession model, RectD[] viewports) : Control
    {
        internal Bitmap? Backdrop;
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
            ChromeTheme.Grid(e.Graphics, ClientRectangle);
            if (Backdrop is not null)
            {
                var location = TopLevelControl?.PointToClient(PointToScreen(Point.Empty)) ?? Point.Empty;
                e.Graphics.DrawImage(Backdrop, ClientRectangle, new Rectangle(location, ClientSize), GraphicsUnit.Pixel);
                using var grid = new Pen(Color.FromArgb(22, ChromeTheme.Cyan));
                for (var x = 0; x < Width; x += 40) e.Graphics.DrawLine(grid, x, 0, x, Height);
                for (var y = 0; y < Height; y += 40) e.Graphics.DrawLine(grid, 0, y, Width, y);
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var window in model.Windows.OrderBy(window => window.Id == model.SelectedId))
            {
                if (VisibleRect(window.Bounds) is not { } rect) continue;
                var matched = model.IsMatch(window.Id); var selected = window.Id == model.SelectedId;
                using var fill = new LinearGradientBrush(rect, matched ? Color.FromArgb(210, 67, 67, 92) : Color.FromArgb(210, 28, 25, 42), Color.FromArgb(225, 23, 22, 40), 100);
                using var border = new Pen(selected ? ChromeTheme.Lime : matched ? ChromeTheme.Cyan : Color.FromArgb(70, 67, 90), selected ? 3 : 1);
                e.Graphics.FillRectangle(fill, rect); e.Graphics.DrawRectangle(border, rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
                if (rect.Width > 50 && rect.Height > 24)
                {
                    var title = Rectangle.Round(rect); title.Height = Math.Min(title.Height, 30);
                    TextRenderer.DrawText(e.Graphics, window.Title, Font, title,
                        matched ? Color.White : Color.FromArgb(90, 100, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    if (rect.Height > 75)
                    {
                        var footer = Rectangle.Round(rect); footer.Y = footer.Bottom - 26; footer.Height = 26;
                        TextRenderer.DrawText(e.Graphics, PreviewLabel(window.Id), Font, footer,
                            matched ? Color.LightGray : Color.FromArgb(90, 100, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                }
            }
            foreach (var viewport in viewports)
            if (VisibleRect(viewport) is { } view)
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
