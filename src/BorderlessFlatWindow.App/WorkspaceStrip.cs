using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Core.Canvas;
using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Application.Overview;

namespace BorderlessFlatWindow.App;

internal sealed class WorkspaceStrip : UserControl
{
    private readonly BookmarkLibrary? library;
    private readonly PointD camera;
    private readonly Func<IReadOnlyList<CanvasWindow>> windows;
    private readonly SizeD viewport;
    private readonly RectD[] monitorOffsets;
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new(12, 0, 12, 0) };
    private readonly Label message = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new(24, 2, 0, 0), Visible = false };
    private readonly ToolTip hints = new();
    private readonly TableLayoutPanel header;
    private bool sizing;
    private readonly Dictionary<Button, PointD> positions = [];
    private readonly Dictionary<(Button Card, Guid Window, int Monitor), Guid> previewIds = [];
    private Bitmap? wallpaper;
    internal event Action? GeometryChanged;
    internal event Action? AppearanceChanged;
    internal async Task LoadWallpaperAsync()
    {
        var next = await WallpaperPaletteReader.ReadPreviewAsync();
        if (IsDisposed || Disposing) { next?.Dispose(); return; }
        wallpaper?.Dispose(); wallpaper = next; cards.Invalidate(true); AppearanceChanged?.Invoke();
    }
    internal event Action<PointD>? JumpRequested;
    internal WorkspaceStrip(BookmarkLibrary? library, PointD camera, Func<IReadOnlyList<CanvasWindow>> windows, SizeD viewport, Control tools, RectD[]? viewports = null)
    {
        this.library = library; this.camera = camera; this.windows = windows; this.viewport = viewport;
        monitorOffsets = viewports?.Select(r => new RectD(r.Position - camera, r.Size)).ToArray() ?? [new(PointD.Zero, viewport)];
        DoubleBuffered = true; ResizeRedraw = true;
        cards.SizeChanged += (_, _) =>
        { SizeCards(); };
        cards.Layout += (_, _) => SizeCards();
        cards.Scroll += (_, _) => GeometryChanged?.Invoke();
        header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new(24, 0, 24, 0), ColumnCount = 4, RowCount = 1 };
        header.RowStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 90)); header.ColumnStyles.Add(new(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new(SizeType.Absolute, 48)); header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = "工作区", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var add = new ChromeButton { Text = "+", AccessibleName = "保存当前视角", Size = new(44, 44), Enabled = library?.CanWrite == true };
        add.Click += (_, _) =>
        {
            var name = AskName("保存工作区", "工作区");
            if (name is null) return;
            library!.Add(name, camera); Render(true);
        };
        var manage = new ChromeButton { Text = "⋯", AccessibleName = "管理工作区", Size = new(44, 44), Enabled = library is not null };
        manage.Click += (_, _) =>
        {
            using var dialog = new Form
            {
                Text = "工作区管理",
                ClientSize = new(820, 580),
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                Font = Font
            };
            var editor = new BookmarkControl(library!, () => camera, () => false, _ => "请关闭编辑器后选择总览中的工作区卡片。", () => { }) { Dock = DockStyle.Fill };
            dialog.Controls.Add(editor); ChromeTheme.Apply(dialog);
            dialog.TopMost = (TopLevelControl as Form)?.TopMost == true;
            dialog.ShowDialog(TopLevelControl as Form); Render(true);
        };
        hints.SetToolTip(add, "将当前视角保存为工作区"); hints.SetToolTip(manage, "搜索、编辑与存储");
        header.Controls.Add(add, 1, 0); header.Controls.Add(manage, 2, 0); header.Controls.Add(tools, 3, 0);
        tools.Dock = DockStyle.Fill;
        Controls.Add(cards); Controls.Add(message); Controls.Add(header); Padding = new(12, 12, 12, 10);
        Render(); ChromeTheme.Apply(this);
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitHeader(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitHeader(); }
    private void FitHeader()
    {
        if (header is null) return;
        var s = DeviceDpi / 96f;
        header.Height = Math.Max((int)(52 * s), Font.Height + (int)(20 * s));
        header.ColumnStyles[0].Width = TextRenderer.MeasureText("工作区", Font).Width + (int)(18 * s);
        header.ColumnStyles[1].Width = header.ColumnStyles[2].Width = Math.Max(48 * s, Font.Height + 18 * s);
        foreach (var button in header.Controls.OfType<Button>()) button.Size = new((int)header.ColumnStyles[1].Width - 6, header.Height - 6);
        SizeCards();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        ChromeTheme.Glass(e.Graphics, new RectangleF(18, 5, Width - 36, Height - 18), ChromeTheme.Purple);
    }
    private void SizeCards()
    {
        if (sizing || cards is null) return; sizing = true;
        try
        {
            var desired = cards.Height - cards.Padding.Vertical - 12;
            var needsScroll = cards.Controls.Cast<Control>().Sum(card => (int)(desired * 1.16) + card.Margin.Horizontal) + 32 > cards.Width;
            var height = Math.Clamp(desired - (needsScroll ? SystemInformation.HorizontalScrollBarHeight + 8 : 8), 60, (int)(228 * DeviceDpi / 96f));
            foreach (Control card in cards.Controls) { var size = new Size((int)(height * 1.16), height); if (card.Size != size) card.Size = size; }
            var total = cards.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
            var left = Math.Max(16, (cards.ClientSize.Width - total) / 2);
            if (cards.Padding.Left != left) cards.Padding = new(left, 4, 16, 8);
        }
        finally { sizing = false; }
        GeometryChanged?.Invoke();
    }
    private void Render(bool showStorageMessage = false)
    {
        foreach (Control child in cards.Controls.Cast<Control>().ToArray()) child.Dispose();
        cards.Controls.Clear();
        positions.Clear(); previewIds.Clear();
        if (library is not null) foreach (var bookmark in library.Collection.Items)
            AddCard(bookmark.Name, bookmark.CameraPosition, bookmark.Id);
        message.Text = library is not null && (showStorageMessage || !library.CanWrite) ? library.Message : "工作区记录视角；窗口始终位于同一画布。右击卡片可重命名或删除。";
        message.Visible = library is { CanWrite: false };
        SizeCards();
    }
    private void AddCard(string name, PointD position, Guid? id)
    {
        var button = new ChromeButton { Text = name, Size = new(164, 142), Margin = new(12, 3, 12, 6), AccessibleName = "工作区 " + name };
        positions.Add(button, position);
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
        button.BackColor = Color.Transparent;
        button.Artwork = graphics =>
        {
            DrawMonitor(graphics, button, name, position, position == camera);
        };
        cards.Controls.Add(button);
    }
    private void DrawMonitor(Graphics g, Button button, string name, PointD position, bool selected)
    {
        if (TopLevelControl is SpatialOverviewForm overview)
        {
            overview.PaintScene(g, button);
            var offset = button.PointToClient(PointToScreen(Point.Empty));
            var saved = g.Save(); g.TranslateTransform(offset.X, offset.Y);
            ChromeTheme.Glass(g, new RectangleF(18, 5, Width - 36, Height - 18), ChromeTheme.Purple); g.Restore(saved);
        }
        else g.Clear(ChromeTheme.Ink);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var captionHeight = TextRenderer.MeasureText(g, name, button.Font).Height + 8;
        var body = new RectangleF(7, 3, button.Width - 14, Math.Max(20, button.Height - captionHeight - 22));
        var accent = selected ? ChromeTheme.Pink : ChromeTheme.Cyan;
        using var baseShape = ChromeTheme.Round(new(body.X + body.Width * .22f, body.Bottom + 4, body.Width * .56f, 11), 6);
        using var pedestal = new LinearGradientBrush(new RectangleF(body.X, body.Y, body.Width, body.Height + 18), Color.White, ChromeTheme.Purple, 90);
        g.FillRectangle(pedestal, body.X + body.Width * .42f, body.Bottom - 2, body.Width * .16f, 12); g.FillPath(pedestal, baseShape);
        ChromeTheme.Glass(g, body, accent, selected);
        using var casing = ChromeTheme.Round(body, 18); using var shell = new LinearGradientBrush(body, Color.FromArgb(255, 230, 252), ChromeTheme.Mix(Color.White, accent, .6f), 40); g.FillPath(shell, casing);
        using var edge = new Pen(selected ? ChromeTheme.Pink : Color.White, selected ? 2.5f : 1.5f); g.DrawPath(edge, casing);
        var screen = new RectangleF(body.X + 10, body.Y + 9, body.Width - 20, body.Height - 25);
        if (screen.Width > 8 && screen.Height > 8)
        {
            using var bezel = ChromeTheme.Round(screen, 10); using var dark = new SolidBrush(ChromeTheme.Mix(ChromeTheme.Purple, Color.Black, .65f)); g.FillPath(dark, bezel);
            var clip = g.Save(); g.SetClip(bezel);
            using var screenFill = new LinearGradientBrush(screen, ChromeTheme.Mix(ChromeTheme.Cyan, Color.Black, .55f), ChromeTheme.Mix(ChromeTheme.Purple, Color.Black, .4f), 35); g.FillPath(screenFill, bezel);
            var scale = Math.Min((screen.Width - 8) / viewport.Width, (screen.Height - 8) / viewport.Height);
            var ox = screen.X + (screen.Width - viewport.Width * scale) / 2;
            var oy = screen.Y + (screen.Height - viewport.Height * scale) / 2;
            var viewportClip = new RectangleF((float)ox, (float)oy, (float)(viewport.Width * scale), (float)(viewport.Height * scale));
            g.SetClip(viewportClip, CombineMode.Intersect);
            if (wallpaper is not null) g.DrawImage(wallpaper, viewportClip);
            // Window content is composited by DWM at its true viewport position, with source cropping.
            g.Restore(clip);
            using var reflection = new LinearGradientBrush(screen, Color.FromArgb(75, Color.White), Color.Transparent, 75); g.FillPath(reflection, bezel);
        }
        using var light = new SolidBrush(selected ? ChromeTheme.Pink : ChromeTheme.Lime); g.FillEllipse(light, body.Right - 17, body.Bottom - 11, 5, 5);
        TextRenderer.DrawText(g, name, button.Font, new Rectangle(4, button.Height - captionHeight, button.Width - 8, captionHeight), ChromeTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (button.Focused) { using var focus = ChromeTheme.Round(body, 18); using var pen = new Pen(ChromeTheme.Purple, 2); g.DrawPath(pen, focus); }
    }
    internal IEnumerable<PreviewCard> PreviewCards(IReadOnlyDictionary<Guid, OverviewPreviewSource> sources, Control host)
    {
        foreach (var (button, position) in positions)
        {
            if (!button.IsHandleCreated) continue;
            var caption = TextRenderer.MeasureText(button.Text, button.Font).Height + 8;
            var bodyHeight = Math.Max(20, button.Height - caption - 22);
            var screen = new Rectangle(21, 16, button.Width - 42, (int)bodyHeight - 33);
            if (screen.Width <= 0 || screen.Height <= 0) continue;
            var point = cards.PointToClient(button.PointToScreen(screen.Location));
            if (!cards.ClientRectangle.Contains(new Rectangle(point, screen.Size))) continue;
            point = host.PointToClient(button.PointToScreen(screen.Location));
            var frame = new PixelRect(point.X, point.Y, point.X + screen.Width, point.Y + screen.Height);
            foreach (var window in windows())
            {
                if (!sources.TryGetValue(window.Id, out var source)) continue;
                for (var monitor = 0; monitor < monitorOffsets.Length; monitor++)
                {
                    var area = monitorOffsets[monitor];
                    if (ViewportPreview.Project(window.Bounds, new(position, viewport), frame, new(position + area.Position, area.Size)) is not { } slice) continue;
                    var key = (button, window.Id, monitor);
                    if (!previewIds.TryGetValue(key, out var id)) previewIds[key] = id = Guid.NewGuid();
                    yield return new(id, source.Identity, slice.Destination, null, slice.SourceFraction);
                }
            }
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { hints.Dispose(); wallpaper?.Dispose(); wallpaper = null; } base.Dispose(disposing); }
    private string? AskName(string title, string current)
    {
        using var dialog = new Form
        {
            Text = title,
            ClientSize = new(400, 136),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Font = Font
        };
        var field = new TextBox { Text = current, Location = new(16, 16), Width = 368, MaxLength = 80 };
        var save = new ChromeButton { Text = "保存", DialogResult = DialogResult.OK, Location = new(280, 72), Size = new(104, 40) };
        var cancel = new ChromeButton { Text = "取消", DialogResult = DialogResult.Cancel, Location = new(168, 72), Size = new(104, 40) };
        dialog.Controls.AddRange([field, save, cancel]); dialog.AcceptButton = save; dialog.CancelButton = cancel; ChromeTheme.Apply(dialog);
        dialog.TopMost = (TopLevelControl as Form)?.TopMost == true;
        dialog.Shown += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog(TopLevelControl as Form) == DialogResult.OK && !string.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
    }
}
