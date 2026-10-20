using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;

namespace BorderlessFlatWindow.Presentation;

public sealed partial class SpatialOverviewScene
{
    public void SetWorkspaces(IEnumerable<SceneWorkspace> workspaces)
    {
        cards.Children.Clear(); workspaceScreens.Clear(); sliceIds.Clear();
        foreach (var workspace in workspaces)
        {
            var column = new StackPanel { Width = 144, Margin = new(12, 2, 12, 4) };
            var selected = workspace.Position == camera;
            var casing = Glass(13, selected ? Color.FromArgb(220, 218, 236, 255) : Color.FromArgb(200, 240, 222, 253));
            casing.BorderBrush = new SolidColorBrush(selected ? accents[1] : Color.FromArgb(210, 255, 255, 255)); casing.BorderThickness = new(selected ? 2 : 1); casing.Padding = new(9, 8, 9, 10);
            var screen = new Border { Width = 122, Height = 70, CornerRadius = new(5), Background = new SolidColorBrush(Color.FromRgb(40, 38, 65)), ClipToBounds = true };
            var image = new Image { Source = wallpaper, Stretch = Stretch.Uniform }; screen.Child = image; casing.Child = screen;
            var stand = new Border { Width = 58, Height = 5, CornerRadius = new(3), Background = Gradient(Color.FromRgb(229, 214, 255), accents[0]), Margin = new(0, 3, 0, 2) };
            column.Children.Add(casing); column.Children.Add(stand);
            column.Children.Add(new TextBlock { Text = workspace.Name, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, Foreground = summary.Foreground, Margin = new(2, 2, 2, 0), ToolTip = workspace.Name });
            column.MouseRightButtonUp += (_, e) =>
            {
                if (workspace.Id is not { } id) return;
                var p = e.GetPosition(root); WorkspaceMenuRequested?.Invoke(id, new((int)(p.X * scale), (int)(p.Y * scale))); e.Handled = true;
            };
            var card = new SceneCard(() => WorkspaceRequested?.Invoke(workspace.Position)) { Child = column };
            card.MouseLeftButtonDown += (_, e) => { WorkspaceRequested?.Invoke(workspace.Position); e.Handled = true; };
            System.Windows.Automation.AutomationProperties.SetName(card, "工作区 " + workspace.Name);
            workspaceScreens.Add((workspace, screen)); cards.Children.Add(card);
        }
        root.UpdateLayout(); LayoutScene();
    }
    public void SetWallpaper(Bitmap bitmap, RectD? backgroundFraction = null)
    {
        wallpaper = Upload(bitmap);
        foreach (var (_, screen) in workspaceScreens) ((Image)screen.Child).Source = wallpaper;
        // The overview background follows the primary monitor's wallpaper, not the
        // old application rectangles. Workspace screens keep the complete atlas.
        if (backgroundFraction is { } f)
        {
            var left = Math.Clamp((int)Math.Round(f.Position.X * wallpaper.PixelWidth), 0, wallpaper.PixelWidth - 1);
            var top = Math.Clamp((int)Math.Round(f.Position.Y * wallpaper.PixelHeight), 0, wallpaper.PixelHeight - 1);
            var width = Math.Clamp((int)Math.Round(f.Size.Width * wallpaper.PixelWidth), 1, wallpaper.PixelWidth - left);
            var height = Math.Clamp((int)Math.Round(f.Size.Height * wallpaper.PixelHeight), 1, wallpaper.PixelHeight - top);
            var cropped = new System.Windows.Media.Imaging.CroppedBitmap(wallpaper, new(left, top, width, height)); cropped.Freeze(); blurred.Source = cropped;
        }
        else blurred.Source = wallpaper;
    }
    public IReadOnlyList<ScenePreview> PreviewLayout()
    {
        var result = new List<ScenePreview>();
        foreach (var window in model.Windows.OrderBy(w => model.SelectedId == w.Id))
        {
            if (!mapAreas.TryGetValue(window.Id, out var r)) continue;
            var area = Pixels(r);
            var invalid = area is not { } box || box.Left < 0 || box.Top < 0 || box.Right > map.ActualWidth * scale || box.Bottom > map.ActualHeight * scale;
            result.Add(new(window.Id, window.Id, invalid ? null : area, !model.IsMatch(window.Id) ? PreviewState.Unmatched : invalid ? PreviewState.TooSmall : null));
        }
        var scrollOrigin = scroll.TranslatePoint(new Point(), root);
        var clipping = TryPixels(scrollOrigin.X * scale, scrollOrigin.Y * scale,
            (scrollOrigin.X + scroll.ViewportWidth) * scale, (scrollOrigin.Y + scroll.ViewportHeight) * scale);
        if (clipping is not { } clip) return result;
        foreach (var (workspace, screen) in workspaceScreens)
        {
            var p = screen.TranslatePoint(new Point(3, 3), root);
            var candidate = TryPixels(p.X * scale, p.Y * scale,
                (p.X + screen.ActualWidth - 6) * scale, (p.Y + screen.ActualHeight - 6) * scale);
            if (candidate is not { } frame || !clip.Contains(frame)) continue;
            foreach (var window in model.Windows)
            for (var monitor = 0; monitor < monitorOffsets.Length; monitor++)
            {
                var area = monitorOffsets[monitor];
                if (ViewportPreview.Project(window.Bounds, new(workspace.Position, viewport), frame, new(workspace.Position + area.Position, area.Size)) is not { } slice) continue;
                var key = (workspace.Id ?? Guid.Empty, window.Id, monitor);
                if (!sliceIds.TryGetValue(key, out var id)) sliceIds[key] = id = Guid.NewGuid();
                result.Add(new(id, window.Id, slice.Destination, null, slice.SourceFraction, workspace.Id ?? Guid.Empty));
            }
        }
        return result;
    }
    public PixelRect? FocusDestination(Guid id) => mapAreas.TryGetValue(id, out var bounds) ? Pixels(bounds) : null;
    private static PixelRect? Pixels(RectD r) => TryPixels(r.Position.X, r.Position.Y, r.Right, r.Bottom);
    private static PixelRect? TryPixels(double left, double top, double right, double bottom)
    {
        left = Math.Ceiling(left); top = Math.Ceiling(top); right = Math.Floor(right); bottom = Math.Floor(bottom);
        if (!double.IsFinite(left + top + right + bottom) || left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
            || right <= left || bottom <= top || right - left > int.MaxValue || bottom - top > int.MaxValue) return null;
        return new((int)left, (int)top, (int)right, (int)bottom);
    }
}
