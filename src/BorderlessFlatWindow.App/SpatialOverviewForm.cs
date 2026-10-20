using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm : Form
{
    internal sealed record PreviewSource(Guid Id, NativeWindowIdentity Identity, bool FullyOnscreen, RectD? ScreenBounds = null);
    private readonly SpatialOverviewSession model;
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索窗口", AccessibleName = "搜索窗口" };
    private readonly Label summary = new InlineSelectionLabel { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, Padding = new(6, 0, 12, 0) };
    private readonly ChromeButton confirm = new() { Text = "定位选中窗口", AutoSize = true };
    private readonly OverviewMap? legacyMap;
    private OverviewMap map => legacyMap ?? throw new InvalidOperationException("Legacy map is synthetic-only.");
    private readonly bool showWithoutActivation;
    private bool canConfirm = true;
    private string? managementNotice;
    private readonly CheckBox showPreviews = new() { Text = "窗口预览（可能停帧）", AutoSize = true, Checked = true };
    private IReadOnlyDictionary<Guid, PreviewSource> previewSources = new Dictionary<Guid, PreviewSource>();
    private OverviewPreviewSession? previews;
    private PreviewCard[] displayCards = [];
    private Control? previewHost;
    private nint previewDestination;
    private bool closing, refreshingPreviews;
    internal bool IsTransitioning => entering || exiting;
    internal CanvasWindow? ConfirmedTarget { get; private set; }
    internal PointD? ConfirmedCameraPosition { get; private set; }
    private readonly WorkspaceStrip? legacyWorkspaces;
    private WorkspaceStrip workspaces => legacyWorkspaces ?? throw new InvalidOperationException("Legacy workspace strip is synthetic-only.");
    private Bitmap? backdrop;
    private Bitmap? focusFrame;
    private readonly Rectangle backdropBounds;
    private readonly ContextMenuStrip options = new();
    private readonly ToolTip hints = new();

    internal SpatialOverviewForm(IEnumerable<CanvasWindow> windows, Camera2D camera, Rectangle bounds, Guid? selected = null, bool showWithoutActivation = false,
        BookmarkLibrary? bookmarks = null, IEnumerable<RectD>? viewports = null, bool animate = true, bool sceneFixture = false)
    {
        this.showWithoutActivation = showWithoutActivation;
        animateEntrance = animate && !showWithoutActivation;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        backdropBounds = bounds;
        model = new(windows, camera, new(1000, 700), selected);
        Text = "BFW 空间总览"; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None;
        Bounds = bounds; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
        Font = new Font("Segoe UI", 12); BackColor = Color.FromArgb(20, 25, 35); ForeColor = Color.White;
        var monitorViewports = viewports?.ToArray() ?? [camera.Bounds];
        ConfigureOptions();
        if (!showWithoutActivation || sceneFixture)
        {
            ChromeTheme.Apply(this); ConfigureScene(bookmarks, camera, monitorViewports); RefreshSelection(); return;
        }
        legacyMap = new(model, monitorViewports) { Dock = DockStyle.Fill, PaintBackdrop = PaintScene };
        map.GeometryChanged += RefreshPreviews;
        map.ConfirmRequested += ConfirmSelection;
        var searchContainer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        searchContainer.RowStyles.Add(new(SizeType.Percent, 100));
        searchContainer.ColumnStyles.Add(new(SizeType.Percent, 100)); searchContainer.ColumnStyles.Add(new(SizeType.Absolute, Math.Clamp(bounds.Width / 3, 240, 420)));
        searchContainer.ColumnStyles.Add(new(SizeType.Absolute, 48)); searchContainer.ColumnStyles.Add(new(SizeType.Absolute, 48));
        var searchSurface = new GlassSearchSurface { Dock = DockStyle.Fill, Padding = new(14, 10, 14, 6) }; searchSurface.Controls.Add(search);
        var more = new ChromeButton { Text = "⋯", AccessibleName = "总览选项", Dock = DockStyle.Fill };
        var cancel = new ChromeButton { Text = "×", AccessibleName = "关闭总览", Dock = DockStyle.Fill };
        confirm.Click += (_, _) => ConfirmSelection(); cancel.Click += (_, _) => CancelOverview();
        more.Click += (_, _) => options.Show(more, new Point(0, more.Height));
        searchContainer.Controls.Add(summary, 0, 0); searchContainer.Controls.Add(searchSurface, 1, 0);
        searchContainer.Controls.Add(more, 2, 0); searchContainer.Controls.Add(cancel, 3, 0);
        hints.SetToolTip(cancel, "关闭 · Tab / Esc"); hints.SetToolTip(more, "适配、预览与操作提示");
        search.TextChanged += (_, _) => { model.Search(search.Text); RefreshSelection(); };
        legacyWorkspaces = new(bookmarks, camera.Position, () => model.Windows, camera.ViewportSize, searchContainer, monitorViewports) { Dock = DockStyle.Bottom, Height = Math.Clamp(bounds.Height / 5, (int)(210 * DeviceDpi / 96f), (int)(320 * DeviceDpi / 96f)) };
        workspaces.JumpRequested += position => { if (!canConfirm) return; ConfirmedCameraPosition = position; DialogResult = DialogResult.OK; Close(); };
        workspaces.GeometryChanged += () => { sceneDirty = true; RefreshPreviews(); };
        workspaces.AppearanceChanged += () => sceneDirty = true;
        Controls.Add(map); Controls.Add(workspaces);
        void FitOverviewToolbar()
        {
            workspaces.Height = Math.Clamp(ClientSize.Height / 5, (int)(210 * DeviceDpi / 96f), (int)(320 * DeviceDpi / 96f));
            var compact = ClientSize.Width < 800 * DeviceDpi / 96f;
            summary.Visible = !compact;
            searchContainer.ColumnStyles[0].SizeType = compact ? SizeType.Absolute : SizeType.Percent;
            searchContainer.ColumnStyles[0].Width = compact ? 0 : 100;
            searchContainer.ColumnStyles[1].SizeType = compact ? SizeType.Percent : SizeType.Absolute;
            searchContainer.ColumnStyles[1].Width = compact ? 100 : Math.Clamp(ClientSize.Width / 3, 210, 420);
            searchContainer.ColumnStyles[2].Width = searchContainer.ColumnStyles[3].Width = Math.Max(48 * DeviceDpi / 96f, Font.Height + 18);
        }
        Resize += (_, _) => FitOverviewToolbar(); FitOverviewToolbar();
        ChromeTheme.Apply(this);
        MakeTransparent(workspaces);
        search.BorderStyle = BorderStyle.None;
        RefreshSelection();
    }
    private void ConfigureOptions()
    {
        options.Items.Add("适配全部    F5", null, (_, _) => { previews?.RetryFailed(); if (scene is not null) scene.EnsureLayout(); else map.Refit(); RefreshSelection(); });
        var previewsToggle = new ToolStripMenuItem("窗口预览") { Checked = true, CheckOnClick = true };
        previewsToggle.CheckedChanged += (_, _) => showPreviews.Checked = previewsToggle.Checked; options.Items.Add(previewsToggle);
        var viewportToggle = new ToolStripMenuItem("显示当前视野") { CheckOnClick = true };
        viewportToggle.CheckedChanged += (_, _) => { if (scene is not null) scene.ShowViewports(viewportToggle.Checked); else { map.ShowViewport = viewportToggle.Checked; map.Invalidate(); } }; options.Items.Add(viewportToggle);
        options.Items.Add("操作提示", null, (_, _) => MessageBox.Show(this, "Tab / Shift+Tab 返回，方向键 / Ctrl+Tab 选择窗口，Enter 定位。\n输入标题筛选，下方卡片切换工作区。", "空间总览"));
        ChromeTheme.StyleMenu(options); showPreviews.CheckedChanged += (_, _) => RefreshSelection();
    }
    protected override bool ShowWithoutActivation => showWithoutActivation;
    private static void MakeTransparent(Control root)
    {
        if (root is Panel or UserControl or Button or Label) root.BackColor = Color.Transparent;
        foreach (Control child in root.Controls) MakeTransparent(child);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => PaintScene(e.Graphics, this);
    internal void PaintScene(Graphics graphics, Control target)
    {
        if (scene is not null) { graphics.Clear(ChromeTheme.Ink); return; }
        var location = ReferenceEquals(target, this) ? Point.Empty : PointToClient(target.PointToScreen(Point.Empty));
        var saved = graphics.Save(); graphics.TranslateTransform(-location.X, -location.Y);
        if (backdrop is null && entranceBackground is null) ChromeTheme.Grid(graphics, ClientRectangle);
        if (!showWithoutActivation || !entering)
        {
            graphics.Restore(saved);
            if (backdrop is not null)
                graphics.DrawImage(backdrop, target.ClientRectangle, new Rectangle(location, target.ClientSize), GraphicsUnit.Pixel);
            return;
        }
        if (entering && entranceBackground is not null)
        {
            graphics.DrawImage(entranceBackground, ClientRectangle);
            if (entranceScene is not null) DrawAlpha(graphics, entranceScene, ClientRectangle, (float)OverviewMotion.Ease(entranceProgress));
            else if (backdrop is not null) DrawAlpha(graphics, backdrop, ClientRectangle, (float)OverviewMotion.Ease(entranceProgress));
        }
        else if (entering && entranceScene is not null) graphics.DrawImage(entranceScene, ClientRectangle);
        else if (backdrop is not null) graphics.DrawImageUnscaled(backdrop, 0, 0);
        graphics.Restore(saved);
    }
    internal void CaptureBackdrop()
    {
        if (showWithoutActivation) return;
        if (scene is not null) { CaptureSceneBackdrop(); return; }
        var captureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        backdrop?.Dispose(); entranceBackground?.Dispose(); focusFrame?.Dispose(); focusFrame = null;
        var focus = model.SelectedId is { } id ? previewSources.GetValueOrDefault(id) : null;
        PixelRect? frameBounds = focus?.ScreenBounds is { } r && r.Size.IsPositive && focus.FullyOnscreen
            ? new((int)r.Position.X, (int)r.Position.Y, (int)r.Right, (int)r.Bottom) : null;
        var candidate = frameBounds is { } f && f.Left >= backdropBounds.Left && f.Top >= backdropBounds.Top && f.Right <= backdropBounds.Right && f.Bottom <= backdropBounds.Bottom;
        focusCropStatus = candidate ? (int)DesktopCropEligibility.Evaluate(focus!.Identity, frameBounds!.Value, frameBounds.Value) : -1;
        var cropEligible = focusCropStatus == (int)DesktopCropStatus.Ready;
        entranceBackground = FrostedBackdrop.CaptureRaw(backdropBounds);
        if (cropEligible && entranceBackground is not null && DesktopCropEligibility.IsUnobscured(focus!.Identity, frameBounds!.Value))
        {
            var crop = frameBounds.Value;
            try { focusFrame = entranceBackground.Clone(new Rectangle(crop.Left - backdropBounds.Left, crop.Top - backdropBounds.Top, crop.Width, crop.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb); }
            catch (Exception error) when (error is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { }
        }
        backdrop = entranceBackground is null ? null : FrostedBackdrop.BlurredCopy(entranceBackground);
        captureMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds;
        Invalidate(true);
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        overviewShown = true;
        if (scene is not null)
        {
            if (!desiredOpen) { closing = true; Close(); return; }
            if (entering) { entranceStarted = System.Diagnostics.Stopwatch.GetTimestamp(); scene.Retarget(true, 220); }
            else if (!showWithoutActivation) scene.FocusSearch();
            return;
        }
        if (entering)
        {
            entranceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (desiredOpen) compositionSurface?.Start(220);
            else { reversibleClose = true; Close(); }
        }
        else { entranceBackground?.Dispose(); entranceBackground = null; RefreshPreviews(); }
        if (!showWithoutActivation) search.Focus();
    }
    internal void SetPreviewSources(IEnumerable<PreviewSource> sources)
    {
        var next = sources.ToDictionary(source => source.Id);
        if (next.Count == previewSources.Count && next.All(pair => previewSources.TryGetValue(pair.Key, out var old) && old == pair.Value)) return;
        previewSources = next; RefreshPreviews();
    }
    internal void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message)
    {
        if (IsDisposed || model.Status != OverviewStatus.Open) return;
        var next = windows.ToArray(); var changed = canConfirm != running || managementNotice != (running ? null : "管理已暂停，暂不可跳转。" + message);
        if (!model.Windows.SequenceEqual(next))
        {
            model.Synchronize(next); legacyWorkspaces?.Invalidate(true);
            changed = true;
            if (entering && entranceSource is { } source && !next.Any(w => w.Id == source.Id)) CompleteEntrance();
        }
        canConfirm = running; managementNotice = running ? null : "管理已暂停，暂不可跳转。" + message;
        if (changed) RefreshSelection();
    }
    private void RefreshSelection()
    {
        sceneDirty = true;
        if (entranceSource is { } source && !model.Windows.Any(window => window.Id == source.Id)) compositionSurface?.ShowMovingImage(false);
        if (scene is not null)
        {
            if (entranceSource is { } old && !model.Windows.Any(window => window.Id == old.Id)) scene.ShowFocusImage(false);
            scene.RefreshWindows(); scene.SetStatus(managementNotice); RefreshPreviews(); return;
        }
        var selected = model.Windows.FirstOrDefault(window => window.Id == model.SelectedId);
        confirm.Enabled = canConfirm && selected is not null;
        summary.Text = managementNotice ?? (model.Matches.Count == 0 ? "没有匹配的窗口" : $"{selected?.Title ?? ""}  ·  Enter 打开");
        map.Invalidate();
        RefreshPreviews();
    }
    private void RefreshPreviews()
    {
        if (closing || refreshingPreviews || IsDisposed || !IsHandleCreated || (scene is null ? !map.IsHandleCreated : !sceneReady || !scene.IsHandleCreated)) return;
        if (IsTransitioning && animationCards is not null) return;
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
        var layoutStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var cards = scene is null ? map.PreviewCards(previewSources, host).Concat(workspaces.PreviewCards(previewSources, host)).ToArray()
            : scene.PreviewLayout().Select(card => new PreviewCard(card.Id, previewSources.GetValueOrDefault(card.Window)?.Identity, card.Bounds, card.Fallback, card.Fraction)).ToArray();
        previewLayoutMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(layoutStarted).TotalMilliseconds;
        var syncStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        current.Synchronize(cards, null, showPreviews.Checked && canConfirm, entering ? (byte)0 : (byte)255);
        previewSyncMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(syncStarted).TotalMilliseconds; previewSyncCalls++;
        if (closing || !ReferenceEquals(previews, current)) return;
        if (scene is null && (map.PreviewStates.Count != current.States.Count || current.States.Any(pair => map.PreviewStates.GetValueOrDefault(pair.Key) != pair.Value)))
        { map.PreviewStates = current.States.ToDictionary(); map.Invalidate(); }
        displayCards = cards.Select(card => card.SourceFraction is null && card.ContentBounds is { } bounds && current.SourceSize(card.Id) is { } size
            ? card with { ContentBounds = OverviewPreviewSession.Fit(size, bounds) } : card).ToArray();
        if (entering) { animationCards = displayCards; AnimatePreviews(); }
    }
    private void PreviewHostDestroyed(object? sender, EventArgs e) => ReleasePreviews();
    private void ReleasePreviews()
    {
        previews?.Dispose(); previews = null;
        if (previewHost is not null) previewHost.HandleDestroyed -= PreviewHostDestroyed;
        previewHost = null; previewDestination = 0;
        if (legacyMap is not null) legacyMap.PreviewStates = new Dictionary<Guid, PreviewState>();
    }
    private void ConfirmSelection()
    {
        if (!canConfirm || model.Confirm() is not { } target) return;
        ConfirmedTarget = target; DialogResult = DialogResult.OK; Close();
    }
    internal void CancelOverview()
    { desiredOpen = false; reversibleClose = false; if (overviewShown || showWithoutActivation) Close(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closing && !showWithoutActivation && animateEntrance && e.CloseReason == CloseReason.UserClosing && BeginExit()) { e.Cancel = true; return; }
        if (ConfirmedTarget is null) { model.Cancel(); DialogResult = DialogResult.Cancel; }
        closing = true; compositionSurface?.Stop(); ReleasePreviews();
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { closing = true; focusFrame?.Dispose(); focusFrame = null; compositionSurface?.Dispose(); compositionSurface = null; entranceBackground?.Dispose(); entranceBackground = null; entranceScene?.Dispose(); entranceScene = null; ReleasePreviews(); scene?.Dispose(); legacyMap?.Dispose(); legacyWorkspaces?.Dispose(); options.Dispose(); hints.Dispose(); confirm.Dispose(); showPreviews.Dispose(); backdrop?.Dispose(); backdrop = null; } base.Dispose(disposing); }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
        => !OwnedForms.Any(form => form.Visible && form.Modal) && HandleCommand(keys) || base.ProcessCmdKey(ref message, keys);
    internal bool HandleCommand(Keys keys)
    {
        if (keys is Keys.Tab or (Keys.Shift | Keys.Tab)) { ToggleOverview(); return true; }
        if (exiting) return true;
        if (entering && keys is not (Keys.Escape or Keys.Tab or (Keys.Shift | Keys.Tab))) CompleteEntrance();
        switch (keys)
        {
            case Keys.Escape:
                CancelOverview(); return true;
            case Keys.Enter: ConfirmSelection(); return true;
            case Keys.Left: model.MoveSelection(OverviewDirection.Left); break;
            case Keys.Right: model.MoveSelection(OverviewDirection.Right); break;
            case Keys.Up: model.MoveSelection(OverviewDirection.Up); break;
            case Keys.Down: model.MoveSelection(OverviewDirection.Down); break;
            case Keys.Control | Keys.Tab: model.Cycle(1); break;
            case Keys.Control | Keys.Shift | Keys.Tab: model.Cycle(-1); break;
            case Keys.Control | Keys.F: if (scene is not null) scene.FocusSearch(true); else { search.Focus(); search.SelectAll(); } return true;
            case Keys.F5: previews?.RetryFailed(); if (scene is not null) scene.EnsureLayout(); else map.Refit(); break;
            default: return false;
        }
        RefreshSelection(); return true;
    }
    internal void SetSmokeQuery(string text) { if (scene is not null) scene.SetQuery(text); else search.Text = text; }

    private sealed class InlineSelectionLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = Rectangle.FromLTRB(Padding.Left, Padding.Top, Width - Padding.Right, Height - Padding.Bottom);
            TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class GlassSearchSurface : Panel
    {
        internal GlassSearchSurface() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); ChromeTheme.Glass(e.Graphics, new RectangleF(2, 2, Width - 4, Height - 6), ChromeTheme.Purple); }
    }

    private sealed class OverviewMap(SpatialOverviewSession model, RectD[] viewports) : Control
    {
        private readonly ToolTip titles = new();
        private Guid? hover;
        internal void PauseTitles(bool pause) => titles.Active = !pause;
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
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var id = model.HitTest(new(e.X, e.Y));
            if (id == hover) return;
            hover = id; titles.SetToolTip(this, model.Windows.FirstOrDefault(window => window.Id == id)?.Title ?? "");
        }
        protected override void Dispose(bool disposing) { if (disposing) titles.Dispose(); base.Dispose(disposing); }
        private int HeaderHeight(double width, double height) => width >= Font.Height + 16 && height >= Font.Height + 32 ? Font.Height + 8 : 0;
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
            foreach (var window in model.Windows.OrderBy(window => window.Id == model.SelectedId))
            {
                var rect = rectangles[window.Id];
                var matched = model.IsMatch(window.Id);
                sources.TryGetValue(window.Id, out var source);
                PreviewState? fallback = !matched ? PreviewState.Unmatched : source is null ? PreviewState.Unavailable : null;
                PixelRect? content = null;
                if (rect.Position.X < 0 || rect.Position.Y < 0 || rect.Right > Width || rect.Bottom > Height
                    || rect.Size.Width < 3 || rect.Size.Height < 3) fallback ??= PreviewState.TooSmall;
                else
                {
                    var inset = Math.Min(4, rect.Size.Width / 8); var title = HeaderHeight(rect.Size.Width, rect.Size.Height);
                    var body = Rectangle.FromLTRB((int)Math.Ceiling(rect.Position.X + inset), (int)Math.Ceiling(rect.Position.Y) + title,
                        (int)Math.Floor(rect.Right - inset), (int)Math.Floor(rect.Bottom) - 3);
                    var point = host.PointToClient(PointToScreen(body.Location));
                    if (body.Width > 0 && body.Height > 0) content = new(point.X, point.Y, point.X + body.Width, point.Y + body.Height);
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
                var header = HeaderHeight(rect.Width, rect.Height);
                if (header == 0) continue;
                var title = Rectangle.Round(rect); title.Height = header; title.Inflate(-6, 0);
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
