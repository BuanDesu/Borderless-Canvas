using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Keys = System.Windows.Forms.Keys;

namespace BorderlessFlatWindow.Presentation;

public sealed record SceneWorkspace(Guid? Id, string Name, PointD Position, int? Members = null);
public sealed record ScenePreview(Guid Id, Guid Window, PixelRect? Bounds, PreviewState? Fallback = null, RectD? Fraction = null, Guid? Workspace = null);

/// <summary>One retained WPF visual tree. No WinForms child layout or whole-page screenshots.</summary>
public sealed partial class SpatialOverviewView : Grid, IDisposable
{
    private readonly SpatialOverviewSession model;
    private readonly PointD camera;
    private readonly SizeD viewport;
    private readonly RectD[] monitorOffsets;
    private readonly Grid root = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Image sharp = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image blurred = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0 };
    private readonly Grid content = new() { Opacity = 0 };
    private readonly Canvas map = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBox search = new() { BorderThickness = new(0), Background = Brushes.Transparent, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, Padding = new(8, 6, 8, 6) };
    private readonly TextBlock searchHint = new() { Text = "搜索窗口", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(115, 107, 133)), VerticalAlignment = VerticalAlignment.Center, Margin = new(10, 0, 0, 0), IsHitTestVisible = false };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.FromRgb(47, 40, 65)) };
    private readonly TextBlock notice = new() { Visibility = Visibility.Collapsed, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(75, 60, 92)), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<Guid, Border> mapCards = new();
    private readonly Dictionary<Guid, RectD> mapAreas = new();
    private readonly Dictionary<(Guid Workspace, Guid Window, int Monitor), Guid> sliceIds = new();
    private readonly List<(SceneWorkspace Workspace, Border Screen)> workspaceScreens = new();
    private readonly Color[] accents;
    private readonly bool synthetic;
    private readonly RowDefinition footerRow = new();
    private BitmapSource? wallpaper;
    private bool layingOut, released, showViewports, updatingSearch;
    private double scale = 1;
    private int pixelWidth = 1, pixelHeight = 1;
    private double configuredScale = 1;
    public event Action? GeometryChanged;
    public event Action? ConfirmRequested;
    public event Action<PointD>? WorkspaceRequested;
    public event Action<Guid>? WorkspaceOpenRequested;
    public event Action<Guid, System.Drawing.Point>? WorkspaceMenuRequested;
    public event Action? SaveRequested;
    public event Action? ManageRequested;
    public event Action? OptionsRequested;
    public event Action? CancelRequested;
    public event Func<Keys, bool>? Command;
    // Read the animated dependency property: retargets and native opacity must
    // follow WPF's evaluated frame, not a separate wall clock racing ahead of it.
    public double Position => content.Opacity;
    public bool HasFocusImage => focusImage?.Visibility == Visibility.Visible;
    public bool IsAnimating => frameHandler is not null;

    public SpatialOverviewView(SpatialOverviewSession model, PointD camera, SizeD viewport, RectD[] monitorOffsets, System.Drawing.Color[] colors, bool synthetic = false)
    {
        this.model = model; this.camera = camera; this.viewport = viewport; this.monitorOffsets = monitorOffsets;
        this.synthetic = synthetic;
        accents = colors.Select(c => Color.FromRgb(c.R, c.G, c.B)).ToArray();
        Focusable = true;
        root.Children.Add(sharp); root.Children.Add(blurred);
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); content.RowDefinitions.Add(footerRow);
        content.Children.Add(map);
        var footer = BuildFooter(); Grid.SetRow(footer, 1); content.Children.Add(footer); root.Children.Add(content);
        Children.Add(root);
        root.PreviewKeyDown += SceneKeyDown;
        root.SizeChanged += (_, _) => LayoutScene(); map.SizeChanged += (_, _) => LayoutScene();
        scroll.ScrollChanged += (_, _) => { LayoutScene(); GeometryChanged?.Invoke(); };
        search.TextChanged += (_, _) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; if (!updatingSearch) { model.Search(search.Text); RefreshWindows(); } };
        AutomationProperties.SetName(search, "搜索窗口"); AutomationProperties.SetName(map, "空间地图");
    }
    private Border BuildFooter()
    {
        var shell = Glass(18, Color.FromArgb(192, 247, 248, 255)); shell.Margin = new(12, 0, 12, 8); shell.Padding = new(16, 8, 16, 8);
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(42) }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new Grid(); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = new(290) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = "工作区", FontSize = 15, Foreground = summary.Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) });
        left.Children.Add(ActionButton("+", "保存窗口布局", () => SaveRequested?.Invoke()));
        left.Children.Add(ActionButton("⋯", "管理工作区", () => ManageRequested?.Invoke())); toolbar.Children.Add(left);
        Grid.SetColumn(summary, 1); summary.Margin = new(16, 0, 16, 0); toolbar.Children.Add(summary);
        var searchGlass = Glass(12, Color.FromArgb(185, 255, 255, 255)); searchGlass.Margin = new(0, 2, 6, 2);
        var searchLayers = new Grid(); searchLayers.Children.Add(search); searchLayers.Children.Add(searchHint); searchGlass.Child = searchLayers; Grid.SetColumn(searchGlass, 2); toolbar.Children.Add(searchGlass);
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(ActionButton("⋯", "总览选项", () => OptionsRequested?.Invoke())); right.Children.Add(ActionButton("×", "关闭总览", () => CancelRequested?.Invoke())); Grid.SetColumn(right, 3); toolbar.Children.Add(right);
        toolbar.SizeChanged += (_, _) => { var compact = toolbar.ActualWidth < 720; summary.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; toolbar.ColumnDefinitions[2].Width = new(compact ? Math.Max(130, toolbar.ActualWidth - 260) : 290); };
        layout.Children.Add(toolbar); scroll.Content = cards; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        Grid.SetRow(notice, 2); layout.Children.Add(notice); shell.Child = layout; return shell;
    }
    private Button ActionButton(string glyph, string name, Action action)
    {
        var button = new Button { Content = glyph, Width = 40, Height = 36, Margin = new(2), FontSize = 22, Foreground = summary.Foreground, Background = Gradient(Color.FromArgb(240, 255, 255, 255), Color.FromArgb(150, 221, 213, 252)), BorderBrush = Brushes.White, BorderThickness = new(1), Padding = new(4) };
        var frame = new FrameworkElementFactory(typeof(Border)); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(11)); frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        AutomationProperties.SetName(button, name); button.ToolTip = name; button.Click += (_, _) => action(); return button;
    }
    private static LinearGradientBrush Gradient(Color a, Color b) { var brush = new LinearGradientBrush(a, b, 65); brush.Freeze(); return brush; }
    private static Border Glass(double radius, Color color) => new() { CornerRadius = new(radius), Background = Gradient(Color.FromArgb(Math.Min((byte)245, color.A), 255, 255, 255), color), BorderBrush = new SolidColorBrush(Color.FromArgb(165, 255, 255, 255)), BorderThickness = new(1) };
    public void SetViewport(int width, int height, double pixelsPerDip)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        pixelWidth = width; pixelHeight = height; configuredScale = pixelsPerDip; EnsureLayout();
    }
    private double CurrentScale => PresentationSource.FromVisual(root) is null ? configuredScale : VisualTreeHelper.GetDpi(root).DpiScaleX;
    private void EnsureLayout()
    {
        scale = CurrentScale;
        var size = new System.Windows.Size(pixelWidth / scale, pixelHeight / scale);
        Measure(size); Arrange(new(new Point(), size)); UpdateLayout(); LayoutScene();
    }
    private void LayoutScene()
    {
        if (layingOut || released || root.ActualWidth < 1 || root.ActualHeight < 1) return;
        layingOut = true;
        try
        {
            scale = CurrentScale; footerRow.Height = new(Math.Clamp(root.ActualHeight / 5, 150, 230));
            if (map.ActualWidth > 128 && map.ActualHeight > 128)
            { model.Refit(new(map.ActualWidth * scale, map.ActualHeight * scale)); DrawMap(); }
            foreach (var (_, screen) in workspaceScreens)
            {
                var height = Math.Clamp(scroll.ViewportHeight - 54, 20, 120);
                screen.Height = height;
                if (screen.Parent is Border casing && casing.Parent is StackPanel column)
                { column.Width = (height + 50) * 1.16; screen.Width = Math.Max(30, column.Width - 22); }
            }
        }
        finally { layingOut = false; }
        GeometryChanged?.Invoke();
    }
    public void RefreshWindows()
    {
        DrawMap(); summary.Text = model.Matches.Count == 0 ? "没有匹配的窗口" : model.Windows.FirstOrDefault(w => w.Id == model.SelectedId)?.Title ?? "";
        GeometryChanged?.Invoke();
    }
    private void DrawMap()
    {
        if (map.ActualWidth < 1) return;
        var ids = model.Windows.Select(w => w.Id).ToHashSet();
        foreach (var id in mapCards.Keys.Where(id => !ids.Contains(id)).ToArray()) { map.Children.Remove(mapCards[id]); mapCards.Remove(id); }
        mapAreas.Clear();
        foreach (var window in model.Windows)
        {
            if (!mapCards.TryGetValue(window.Id, out var card))
            {
                var style = Glass(12, Color.FromArgb(205, 240, 232, 255));
                card = new SceneCard(() => { if (model.Select(window.Id)) ConfirmRequested?.Invoke(); }) { CornerRadius = style.CornerRadius, Background = style.Background, BorderBrush = style.BorderBrush, BorderThickness = style.BorderThickness };
                card.Child = new TextBlock { Foreground = summary.Foreground, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, Margin = new(6, 4, 6, 0), VerticalAlignment = VerticalAlignment.Top };
                card.MouseLeftButtonDown += (_, e) => { if (model.Select(window.Id)) { ConfirmRequested?.Invoke(); e.Handled = true; } };
                mapCards.Add(window.Id, card); map.Children.Add(card);
            }
            var r = model.Projection.ToMap(window.Bounds); var selected = model.SelectedId == window.Id;
            card.Width = Math.Max(1, r.Size.Width / scale); card.Height = Math.Max(1, r.Size.Height / scale); Canvas.SetLeft(card, r.Position.X / scale); Canvas.SetTop(card, r.Position.Y / scale);
            card.Opacity = model.IsMatch(window.Id) ? 1 : .3; card.BorderBrush = new SolidColorBrush(selected ? accents[2] : Color.FromArgb(185, 255, 255, 255)); card.BorderThickness = new(selected ? 2 : 1);
            ((TextBlock)card.Child).Text = window.Title; card.ToolTip = window.Title; AutomationProperties.SetName(card, window.Title); Panel.SetZIndex(card, selected ? 2 : 1);
            var header = r.Size.Width / scale > 40 && r.Size.Height / scale > 50 ? 26 * scale : 0;
            var body = new RectD(new(r.Position.X + 5 * scale, r.Position.Y + header), new(Math.Max(.001, r.Size.Width - 10 * scale), Math.Max(.001, r.Size.Height - header - 5 * scale)));
            mapAreas[window.Id] = body;
        }
        DrawViewports();
    }
    private readonly List<System.Windows.Shapes.Rectangle> viewportLines = new();
    public void ShowViewports(bool value) { showViewports = value; DrawViewports(); }
    private void DrawViewports()
    {
        foreach (var line in viewportLines) map.Children.Remove(line); viewportLines.Clear();
        if (!showViewports) return;
        foreach (var area in monitorOffsets)
        {
            var r = model.Projection.ToMap(new RectD(camera + area.Position, area.Size));
            var line = new System.Windows.Shapes.Rectangle { Width = r.Size.Width / scale, Height = r.Size.Height / scale, Stroke = new SolidColorBrush(Color.FromArgb(70, 50, 40, 80)), StrokeDashArray = new() { 4, 4 }, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetLeft(line, r.Position.X / scale); Canvas.SetTop(line, r.Position.Y / scale); map.Children.Add(line); viewportLines.Add(line);
        }
    }
    public void SetStatus(string? text) { notice.Text = text ?? ""; notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible; }
    public void FocusSearch(bool selectAll = false) { search.Focus(); if (selectAll) search.SelectAll(); }
    public void AppendSearch(string text) { search.AppendText(text); search.CaretIndex = search.Text.Length; }
    public void SetQuery(string text) { updatingSearch = true; try { search.Text = text; model.Search(text); } finally { updatingSearch = false; } RefreshWindows(); }
    private void SceneKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var keys = (Keys)KeyInterop.VirtualKeyFromKey(key);
        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) keys |= Keys.Control;
        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) keys |= Keys.Shift;
        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) keys |= Keys.Alt;
        if (Command?.Invoke(keys) == true) e.Handled = true;
    }
    public byte[] RenderSyntheticPng()
    {
        if (!synthetic) throw new InvalidOperationException("Only explicitly synthetic scenes can be exported.");
        EnsureLayout(); var image = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new System.IO.MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static bool warmed;
    public static void WarmUp()
    {
        if (warmed) return; warmed = true;
        var camera = new Camera2D(PointD.Zero, new(800, 600));
        var model = new SpatialOverviewSession([], camera, camera.ViewportSize);
        using var view = new SpatialOverviewView(model, camera.Position, camera.ViewportSize, [camera.Bounds],
            [System.Drawing.Color.MediumPurple, System.Drawing.Color.DeepSkyBlue, System.Drawing.Color.HotPink, System.Drawing.Color.Turquoise], synthetic: true);
        view.SetWorkspaces([new(null, "", PointD.Zero)]); view.SetViewport(800, 600, 1);
    }
    public void Dispose()
    {
        if (!released)
        {
            StopMotion(); released = true; root.PreviewKeyDown -= SceneKeyDown;
            sharp.Source = blurred.Source = null; blurred.Effect = null;
            if (focusImage is not null) focusImage.Source = null;
            foreach (var (_, screen) in workspaceScreens) ((Image)screen.Child).Source = null;
            Children.Clear(); root.Children.Clear(); map.Children.Clear(); cards.Children.Clear(); mapCards.Clear(); workspaceScreens.Clear();
            wallpaper = null; Frame = null;
            GeometryChanged = null; ConfirmRequested = null; WorkspaceRequested = null; WorkspaceOpenRequested = null; WorkspaceMenuRequested = null;
            SaveRequested = null; ManageRequested = null; OptionsRequested = null; CancelRequested = null; Command = null;
        }
    }
}
