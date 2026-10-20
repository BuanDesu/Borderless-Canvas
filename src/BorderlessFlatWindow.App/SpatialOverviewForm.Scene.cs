using System.Diagnostics;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    private SpatialOverviewScene? scene;
    private bool sceneReady;
    private BookmarkLibrary? sceneBookmarks;
    private void ConfigureScene(BookmarkLibrary? bookmarks, Camera2D camera, RectD[] viewports)
    {
        sceneBookmarks = bookmarks;
        scene = new(model, camera.Position, camera.ViewportSize, viewports.Select(r => new RectD(r.Position - camera.Position, r.Size)).ToArray(), ChromeTheme.Candy)
        { Dock = DockStyle.Fill };
        scene.GeometryChanged += RefreshPreviews;
        scene.ConfirmRequested += ConfirmSelection;
        scene.CancelRequested += CancelOverview;
        scene.Command += HandleCommand;
        scene.WorkspaceRequested += position => { if (!canConfirm) return; ConfirmedCameraPosition = position; DialogResult = DialogResult.OK; Close(); };
        scene.SaveRequested += () => { if (bookmarks?.CanWrite != true) return; var name = AskSceneName("保存工作区", "工作区"); if (name is not null) { bookmarks.Add(name, camera.Position); RefreshSceneWorkspaces(); } };
        scene.ManageRequested += ManageSceneWorkspaces;
        scene.OptionsRequested += () => options.Show(this, PointToClient(Cursor.Position));
        scene.WorkspaceMenuRequested += SceneWorkspaceMenu;
        scene.Frame += AdvanceSceneMotion;
        Controls.Add(scene); scene.BringToFront(); RefreshSceneWorkspaces();
    }
    private void RefreshSceneWorkspaces()
    {
        scene?.SetWorkspaces(new[] { new SceneWorkspace(null, "原点", PointD.Zero) }.Concat(sceneBookmarks?.Collection.Items.Select(b => new SceneWorkspace(b.Id, b.Name, b.CameraPosition)) ?? []));
        if (sceneBookmarks is { CanWrite: false }) scene?.SetStatus(sceneBookmarks.Message);
    }
    private void ManageSceneWorkspaces()
    {
        if (sceneBookmarks is null) return;
        using var dialog = new Form { Text = "工作区管理", ClientSize = new(820, 580), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font, TopMost = TopMost };
        var editor = new BookmarkControl(sceneBookmarks, () => model.StartCamera.Position, () => false, _ => "关闭编辑器后选择总览中的工作区。", () => { }) { Dock = DockStyle.Fill };
        dialog.Controls.Add(editor); ChromeTheme.Apply(dialog); dialog.ShowDialog(this); RefreshSceneWorkspaces();
    }
    private void SceneWorkspaceMenu(Guid id, Point position)
    {
        var entry = sceneBookmarks?.Collection.Items.FirstOrDefault(b => b.Id == id); if (entry is null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("重命名", null, (_, _) => { var name = AskSceneName("重命名工作区", entry.Name); if (name is not null) { sceneBookmarks!.Rename(id, name); RefreshSceneWorkspaces(); } });
        menu.Items.Add("删除工作区", null, (_, _) => { sceneBookmarks!.Remove(id); RefreshSceneWorkspaces(); });
        foreach (ToolStripItem item in menu.Items) item.Enabled = sceneBookmarks!.CanWrite;
        ChromeTheme.StyleMenu(menu);
        menu.Closed += (_, _) =>
        {
            if (IsDisposed || Disposing) { menu.Dispose(); return; }
            try { BeginInvoke(menu.Dispose); } catch (InvalidOperationException) { menu.Dispose(); }
        };
        menu.Show(this, position);
    }
    private string? AskSceneName(string title, string current)
    {
        using var dialog = new Form { Text = title, ClientSize = new(400, 136), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font, TopMost = TopMost };
        var field = new TextBox { Text = current, Location = new(16, 16), Width = 368, MaxLength = 80 };
        var save = new ChromeButton { Text = "保存", DialogResult = DialogResult.OK, Location = new(280, 72), Size = new(104, 40) };
        var cancel = new ChromeButton { Text = "取消", DialogResult = DialogResult.Cancel, Location = new(168, 72), Size = new(104, 40) };
        dialog.Controls.AddRange([field, save, cancel]); dialog.AcceptButton = save; dialog.CancelButton = cancel; ChromeTheme.Apply(dialog);
        dialog.Shown += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
    }
    private void CaptureSceneBackdrop()
    {
        var started = Stopwatch.GetTimestamp();
        using var raw = FrostedBackdrop.CaptureRaw(backdropBounds);
        if (raw is null) return;
        var source = model.SelectedId is { } id ? previewSources.GetValueOrDefault(id) : null;
        if (animateEntrance && source is { FullyOnscreen: true, ScreenBounds: { } bounds })
        {
            var rect = new PixelRect((int)bounds.Position.X, (int)bounds.Position.Y, (int)bounds.Right, (int)bounds.Bottom);
            if (new PixelRect(backdropBounds.Left, backdropBounds.Top, backdropBounds.Right, backdropBounds.Bottom).Contains(rect))
            {
                focusCropStatus = (int)DesktopCropEligibility.Evaluate(source.Identity, rect, rect);
                if (focusCropStatus == 0)
                {
                    try
                    {
                        focusFrame = raw.Clone(new Rectangle(rect.Left - backdropBounds.Left, rect.Top - backdropBounds.Top, rect.Width, rect.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        if (!DesktopCropEligibility.IsUnobscured(source.Identity, rect)) { focusFrame.Dispose(); focusFrame = null; }
                    }
                    catch (Exception error) when (error is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
                    { focusFrame?.Dispose(); focusFrame = null; }
                }
            }
        }
        scene!.SetBackdrop(raw);
        captureMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    private void PrepareSceneEntrance()
    {
        var started = Stopwatch.GetTimestamp();
        scene!.EnsureLayout(); scene.RefreshWindows(); sceneReady = true; RefreshPreviews();
        var source = model.SelectedId is { } id ? previewSources.GetValueOrDefault(id) : null;
        if (focusFrame is not null && source?.ScreenBounds is { } from && displayCards.FirstOrDefault(c => c.Id == source.Id)?.ContentBounds is { } to)
        {
            entranceSource = source;
            scene.SetFocusImage(focusFrame, new(from.Position - new PointD(Left, Top), from.Size), to); compositorFocus = true;
        }
        focusFrame?.Dispose(); focusFrame = null;
        var animate = animateEntrance && CompositionSurface.RenderingTier > 0 && SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled != 0;
        entering = animate; entranceProgress = animate ? 0 : 1; scene.Seek(entranceProgress);
        if (!animate) scene.ShowFocusImage(false);
        animationPreviewCount = displayCards.Length; AnimateScenePreviews();
        prepareMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!showWithoutActivation) _ = LoadSceneWallpaperAsync();
    }
    private async Task LoadSceneWallpaperAsync()
    {
        using var wallpaper = await WallpaperPaletteReader.ReadPreviewAsync();
        if (wallpaper is not null && !IsDisposed && !Disposing)
        {
            var desktop = Screen.AllScreens.Select(s => s.WorkingArea).Aggregate(Rectangle.Union);
            var fraction = new RectD(new((Left - desktop.Left) / (double)desktop.Width, (Top - desktop.Top) / (double)desktop.Height), new(Width / (double)desktop.Width, Height / (double)desktop.Height));
            scene?.SetWallpaper(wallpaper, fraction);
        }
    }
    private bool BeginSceneExit()
    {
        if (exiting) return true;
        // Selection has committed. Return control immediately; never leave a confirmed
        // session swallowing Tab while it plays a return-to-the-old-window animation.
        if (ConfirmedTarget is not null || ConfirmedCameraPosition is not null || !Visible || !animateEntrance
            || CompositionSurface.RenderingTier == 0 || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled == 0) return false;
        var returning = compositorFocus && canConfirm && entranceSource is { } source && previewSources.ContainsKey(source.Id);
        if (!entering && returning) returning = RefreshReturnImage();
        scene!.ShowFocusImage(returning); entering = false; exiting = true; scene.Retarget(false, 160); return true;
    }
    private void AnimateScenePreviews()
    {
        if (previews is null) return;
        var opacity = (byte)Math.Clamp((int)Math.Round(entranceProgress * 255), 0, 255);
        var focus = scene!.HasFocusImage && entranceSource is { } source ? source.Id : (Guid?)null;
        previews.Animate(displayCards.Where(c => c.Id != focus), opacity);
        if (focus is { } id && displayCards.FirstOrDefault(c => c.Id == id) is { } card) previews.Animate([card], 0);
    }
    private void AdvanceSceneMotion(double progress)
    {
        if (closing || IsDisposed) { scene?.StopMotion(); return; }
        var started = Stopwatch.GetTimestamp(); entranceProgress = progress;
        if (exiting)
        {
            AnimateScenePreviews();
            if (progress <= 0) { scene?.StopMotion(); closing = true; Close(); }
            return;
        }
        if (motionTimes.Count == 0) firstCallbackTimestamp = Stopwatch.GetTimestamp();
        motionTimes.Add(Stopwatch.GetElapsedTime(entranceStarted).TotalMilliseconds);
        if (progress >= 1) { CompleteEntrance(); return; }
        AnimateScenePreviews(); motionSamples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
