using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Runtime.InteropServices;
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
        OverviewBackdropChecks.Run();
        DesktopReadbackChecks.Run(output);
        var warming = Stopwatch.StartNew(); SpatialOverviewScene.WarmUp(); var warmupMs = warming.Elapsed.TotalMilliseconds;
        CheckHiddenPreparation(output);
        CheckVisualClock();
        CheckFormLifecycle();
        CheckCancelledPresentation(output);
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
        var firstShowChanged = false; var firstCloseChanged = false; var secondShowChanged = false;
        using (var overview = new SpatialOverviewForm(windows, camera, new(0, 0, 1200, 800), windows[0].Id, showWithoutActivation: true, animate: false, sceneFixture: true))
        {
            overview.Show(); System.Windows.Forms.Application.DoEvents();
            firstShowChanged = foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
            overview.UpdateWindows([windows[0] with { Title = "Alpha renamed" }, windows[1]], true, "");
            overview.SetSmokeQuery("Beta"); overview.HandleCommand(Keys.Enter);
            if (!overview.IsDisposed || overview.ConfirmedTarget?.Id != windows[1].Id) throw new InvalidOperationException("Production scene form failed search/selection/close.");
            firstCloseChanged = foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        }
        using (var overview = new SpatialOverviewForm(windows, camera, new(0, 0, 1200, 800), windows[1].Id, showWithoutActivation: true, animate: false, sceneFixture: true))
        {
            overview.Show(); System.Windows.Forms.Application.DoEvents();
            secondShowChanged = foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
            overview.HandleCommand(Keys.Tab);
            if (!overview.IsDisposed || overview.ConfirmedTarget is not null) throw new InvalidOperationException("Reopened scene could not cancel with Tab.");
        }
        if (BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException($"Scene lifecycle foreground changed: firstShow={firstShowChanged}, firstClose={firstCloseChanged}, secondShow={secondShowChanged}, initialMissing={foreground == 0}.");
    }
    private static void CheckVisualClock()
    {
        var camera = new Camera2D(PointD.Zero, new(800, 600));
        var model = new SpatialOverviewSession([], camera, camera.ViewportSize);
        using var form = new CheckForm { ClientSize = new(800, 600), ShowInTaskbar = false, Text = "BFW visual clock fixture" };
        using var scene = new SpatialOverviewScene(model, camera.Position, camera.ViewportSize, [camera.Bounds], [Color.MediumPurple, Color.DeepSkyBlue, Color.HotPink, Color.Turquoise], synthetic: true) { Dock = DockStyle.Fill };
        form.Controls.Add(scene); scene.EnsureLayout(); scene.Seek(0);
        var checkedClock = false; var timedOut = false; string? failure = null;
        using var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
        watchdog.Tick += (_, _) => { timedOut = true; form.Close(); };
        scene.Frame += position =>
        {
            if (!checkedClock && position is > .15 and < .8)
            {
                checkedClock = true;
                var displayed = scene.Position;
                // Deliberately stall only this synthetic UI thread. No new WPF timing
                // tick can run here: a wall clock must not invent a newer visual value.
                Thread.Sleep(80);
                if (Math.Abs(scene.Position - displayed) > 1e-8) failure = "Logical motion advanced without a WPF timing tick.";
                scene.Retarget(false, 160);
                if (Math.Abs(scene.Position - displayed) > 1e-8) failure ??= "Reversal jumped away from the current visual value.";
            }
            else if (checkedClock && position <= 0) { scene.StopMotion(); form.BeginInvoke(form.Close); }
        };
        form.Shown += (_, _) => { watchdog.Start(); scene.Retarget(true, 1200); };
        System.Windows.Forms.Application.Run(form);
        if (timedOut || !checkedClock || failure is not null) throw new InvalidOperationException(failure ?? "Visual clock fixture did not finish.");
        Console.WriteLine("PASS WPF visual clock remains stable during an 80ms fixture stall and reversal");
    }
    private static void CheckCancelledPresentation(string output)
    {
        var foreground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        var camera = new Camera2D(PointD.Zero, new(800, 600));
        var state = Path.Combine(Path.GetDirectoryName(output)!, "cancelled-fixture"); Directory.CreateDirectory(state);
        using var host = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true,
            journalPath: Path.Combine(state, "recovery.json"), bookmarksPath: Path.Combine(state, "bookmarks.json"));
        foreach (var toggles in new[] { 1, 3 })
        {
            using var overview = new SpatialOverviewForm([], camera, new(0, 0, 800, 600), showWithoutActivation: true, animate: false, sceneFixture: true);
            var shown = 0; var completed = 0;
            overview.Shown += (_, _) => shown++;
            for (var i = 0; i < toggles; i++) overview.ToggleOverview();
            host.PresentOverview(overview, () => { completed++; overview.Dispose(); });
            System.Windows.Forms.Application.DoEvents();
            if (shown != 0 || completed != 1 || host.Visible || !overview.IsDisposed)
                throw new InvalidOperationException("A cancelled overview was shown or its completion was lost.");
        }
        if (foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow)
            throw new InvalidOperationException("Cancelled presentation changed foreground.");
        Console.WriteLine("PASS Cancelled preparation never shows an overview and completes exactly once");
    }
    private sealed class CheckForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000; return parameters; }
        }
    }
    private static void CheckHiddenPreparation(string output)
    {
        var foreground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        using var process = Process.GetCurrentProcess();
        using var source = new CheckForm { Text = "BFW hidden preparation source", ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.Manual, Bounds = new(80, 80, 400, 280) };
        source.Show(); source.Update(); System.Windows.Forms.Application.DoEvents();
        var sourceChangedForeground = foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        var camera = new Camera2D(PointD.Zero, new(1920, 1080));
        var id = Guid.NewGuid();
        CanvasWindow[] windows = [new(id, "Fixture", new(new(80, 80), new(400, 280)))];
        var identity = new BorderlessFlatWindow.Application.Discovery.NativeWindowIdentity(source.Handle, (uint)Environment.ProcessId, process.StartTime.ToFileTimeUtc());
        var results = new List<object>();
        // The actual interactive style must remain invisible during preparation,
        // without relying on the nonactivating style used for visible test hosts.
        foreach (var size in new[] { new Size(1920, 1080), new Size(3440, 1440), new Size(960, 720) })
        {
            using var production = new SpatialOverviewForm(windows, camera, new(Point.Empty, size));
            var constructorChangedForeground = foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
            production.PrepareHiddenHost();
            if (production.Visible || production.LivePreviewCount != 0 || foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow)
                throw new InvalidOperationException($"Interactive hidden preparation: width={size.Width}, visible={production.Visible}, previews={production.LivePreviewCount}, constructorChanged={constructorChangedForeground}, sourceChanged={sourceChangedForeground}, ownOverview={BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow == production.Handle}, ownSource={BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow == source.Handle}.");
            if ((GetWindowLongPtr(production.Handle, -20).ToInt64() & 8) == 0)
                throw new InvalidOperationException("Prepared native overview lost its topmost style.");
        }
        foreach (var size in new[] { new Size(1920, 1080), new Size(3440, 1440), new Size(960, 720) })
        foreach (var prepared in new[] { false, true })
        for (var sample = 0; sample < 3; sample++)
        {
            using var overview = new SpatialOverviewForm(windows, camera, new(Point.Empty, size), showWithoutActivation: true, animate: false, sceneFixture: true);
            overview.SetPreviewSources([new(id, identity, false)]);
            var shown = 0; overview.Shown += (_, _) => shown++;
            var preparation = prepared ? overview.PrepareHiddenHost() : 0;
            if (overview.Visible || shown != 0 || overview.LivePreviewCount != 0 || foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow)
                throw new InvalidOperationException($"Hidden preparation guard: prepared={prepared}, sample={sample}, visible={overview.Visible}, shown={shown}, previews={overview.LivePreviewCount}, foregroundChanged={foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow}, sourceChangedForeground={sourceChangedForeground}, initialForegroundMissing={foreground == 0}.");
            var started = Stopwatch.GetTimestamp(); overview.Show();
            var showMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            System.Windows.Forms.Application.DoEvents();
            var ownHandle = overview.Handle;
            var shownForeground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
            if (shownForeground != foreground) throw new InvalidOperationException($"Show changed foreground: prepared={prepared}, sample={sample}, ownOverview={shownForeground == ownHandle}, ownSource={shownForeground == source.Handle}.");
            var previews = overview.LivePreviewCount;
            if (shown != 1 || previews < 1) throw new InvalidOperationException("Prepared production host did not show with native content.");
            overview.Close();
            var closedForeground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
            if (closedForeground != foreground) throw new InvalidOperationException($"Close changed foreground: prepared={prepared}, sample={sample}, ownSource={closedForeground == source.Handle}, foregroundMissing={closedForeground == 0}.");
            if (!overview.IsDisposed || overview.LivePreviewCount != 0) throw new InvalidOperationException("Prepared host retained native resources after close.");
            results.Add(new { size.Width, size.Height, Prepared = prepared, Sample = sample, PreparationMs = preparation, ShowMs = showMs, NativeCount = previews });
        }
        if (foreground != BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow) throw new InvalidOperationException("Preparation fixture changed foreground.");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "host-preparation.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS Hidden production hosts stay invisible, acquire previews only on show, and release them on close");
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
}
