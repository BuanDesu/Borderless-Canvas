using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

/// <summary>Production visual-tree checks using synthetic pixels and windows only.</summary>
internal static class OverviewSceneCheck
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var warming = Stopwatch.StartNew(); SpatialOverviewScene.WarmUp(); var warmupMs = warming.Elapsed.TotalMilliseconds;
        CheckFormLifecycle();
        var privacyCamera = new Camera2D(PointD.Zero, new(800, 600));
        var privacyModel = new SpatialOverviewSession([], privacyCamera, new(800, 600));
        using (var production = new SpatialOverviewScene(privacyModel, privacyCamera.Position, privacyCamera.ViewportSize, [new(PointD.Zero, privacyCamera.ViewportSize)], [Color.MediumPurple, Color.DeepSkyBlue, Color.HotPink, Color.Turquoise]))
        {
            var rejected = false;
            try { _ = production.RenderSyntheticPng(); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Production scene allowed pixel export.");
        }
        var results = new List<object>();
        foreach (var size in new[] { new Size(1920, 1080), new Size(3440, 1440), new Size(960, 720) })
        {
            var clock = Stopwatch.StartNew();
            CanvasWindow[] windows = [new(Guid.NewGuid(), "编辑器", new(new(-600, 60), new(640, 480))), new(Guid.NewGuid(), "浏览器", new(new(120, 0), new(800, 600))), new(Guid.NewGuid(), "资料", new(new(1300, -700), new(540, 600)))];
            var camera = new Camera2D(new(-100, 0), new(1920, 1080));
            var model = new SpatialOverviewSession(windows, camera, new(1000, 700), windows[0].Id);
            using var form = new CheckForm { ClientSize = size, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = Point.Empty, Text = "BFW synthetic scene check" };
            using var scene = new SpatialOverviewScene(model, camera.Position, camera.ViewportSize, [new(PointD.Zero, camera.ViewportSize)], [Color.MediumPurple, Color.DeepSkyBlue, Color.HotPink, Color.Turquoise], synthetic: true) { Dock = DockStyle.Fill };
            // Exercise the production subscription while scroll viewports are still zero-sized.
            scene.GeometryChanged += () => _ = scene.PreviewLayout();
            form.Controls.Add(scene); _ = scene.Handle;
            var createdMs = clock.Elapsed.TotalMilliseconds;
            using var background = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(background))
            using (var fill = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.FromArgb(185, 202, 235), Color.FromArgb(239, 215, 242), 35)) g.FillRectangle(fill, new Rectangle(Point.Empty, size));
            scene.SetBackdrop(background); scene.SetWallpaper(background);
            var empty = Guid.NewGuid(); var partial = Guid.NewGuid();
            scene.SetWorkspaces([new(null, "原点", PointD.Zero), new(partial, "边缘裁剪", new(300, 0)), new(empty, "空视窗", new(1e6, 1e6))]);
            scene.EnsureLayout(); scene.RefreshWindows(); scene.Seek(1);
            var layout = scene.PreviewLayout();
            if (layout.Count(p => p.Workspace is null) != windows.Length || layout.Any(p => p.Workspace == empty)) throw new InvalidOperationException("Scene map or empty workspace projection failed.");
            if (!layout.Any(p => p.Workspace == partial && p.Fraction is { } f && f.Size.Width < 1)) throw new InvalidOperationException("Scene edge crop was lost.");
            var original = windows.Select(w => model.Projection.ToMap(w.Bounds)).ToArray();
            scene.SetQuery("没有匹配");
            if (scene.PreviewLayout().Count(p => p.Workspace is null && p.Fallback == PreviewState.Unmatched) != windows.Length) throw new InvalidOperationException("Scene search lost title fallbacks.");
            scene.SetQuery("");
            if (!original.SequenceEqual(windows.Select(w => model.Projection.ToMap(w.Bounds)))) throw new InvalidOperationException("Search rearranged spatial positions.");
            var readyMs = clock.Elapsed.TotalMilliseconds;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output)!, $"scene-{size.Width}x{size.Height}.png"), scene.RenderSyntheticPng());
            using var sprite = new Bitmap(640, 480);
            using (var g = Graphics.FromImage(sprite)) g.Clear(Color.MediumPurple);
            scene.SetFocusImage(sprite, new(new(80, 80), new(640, 480)), scene.FocusDestination(windows[0].Id)!.Value); scene.Seek(0);
            var frames = 0; var phase = 0; var timeout = false; var gaps = new List<double>(); var previous = 0d;
            using var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
            watchdog.Tick += (_, _) => { timeout = true; form.Close(); };
            scene.Frame += position =>
            {
                var now = clock.Elapsed.TotalMilliseconds; if (frames++ > 0) gaps.Add(now - previous); previous = now;
                if (phase == 0 && position > .4) { phase = 1; scene.Retarget(false, 160); }
                else if (phase == 1 && position < .2) { phase = 2; scene.Retarget(true, 220); }
                else if (phase == 2 && position >= 1) { scene.StopMotion(); form.BeginInvoke(form.Close); }
            };
            form.Shown += (_, _) => { watchdog.Start(); scene.Retarget(true, 220); };
            System.Windows.Forms.Application.Run(form);
            var completed = frames; scene.StopMotion();
            for (var i = 0; i < 3; i++) System.Windows.Forms.Application.DoEvents();
            if (timeout || phase != 2 || frames < 3 || frames != completed || scene.IsAnimating) throw new InvalidOperationException("Scene rapid reversal or teardown failed.");
            gaps.Sort(); results.Add(new { size.Width, size.Height, Dpi = scene.DeviceDpi, Tier = CompositionSurface.RenderingTier, WarmupMs = warmupMs, CreatedMs = createdMs, ReadyMs = readyMs, Frames = frames, Reversals = phase, GapP95Ms = gaps[(int)Math.Ceiling(gaps.Count * .95) - 1], GapMaxMs = gaps.Max(), DesktopCaptured = false });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    private static void CheckFormLifecycle()
    {
        var foreground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        var camera = new Camera2D(PointD.Zero, new(1920, 1080));
        CanvasWindow[] windows = [new(Guid.NewGuid(), "Alpha", new(PointD.Zero, new(640, 480))), new(Guid.NewGuid(), "Beta", new(new(900, 40), new(640, 480)))];
        using (var overview = new SpatialOverviewForm(windows, camera, new(0, 0, 1200, 800), windows[0].Id, showWithoutActivation: true, animate: false, sceneFixture: true))
        {
            overview.Show(); System.Windows.Forms.Application.DoEvents();
            overview.UpdateWindows([windows[0] with { Title = "Alpha renamed" }, windows[1]], true, "");
            overview.SetSmokeQuery("Beta"); overview.HandleCommand(Keys.Enter);
            if (!overview.IsDisposed || overview.ConfirmedTarget?.Id != windows[1].Id) throw new InvalidOperationException("Production scene form failed search/selection/close.");
        }
        using (var overview = new SpatialOverviewForm(windows, camera, new(0, 0, 1200, 800), windows[1].Id, showWithoutActivation: true, animate: false, sceneFixture: true))
        {
            overview.Show(); System.Windows.Forms.Application.DoEvents(); overview.HandleCommand(Keys.Tab);
            if (!overview.IsDisposed || overview.ConfirmedTarget is not null) throw new InvalidOperationException("Reopened scene could not cancel with Tab.");
        }
        if (BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Scene lifecycle fixture changed desktop foreground.");
    }
    private sealed class CheckForm : Form { protected override bool ShowWithoutActivation => true; }
}
