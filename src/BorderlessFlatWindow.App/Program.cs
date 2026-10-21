using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var pixelFont = new PixelFont();
        OverviewRuntimeOptions.NativeRequested = args.Contains("--native-overview", StringComparer.Ordinal);
        OverviewRuntimeOptions.ForceSolid = args.Contains("--native-overview-solid", StringComparer.Ordinal);
        var nativeCheck = Array.IndexOf(args, "--native-overview-check");
        if (nativeCheck >= 0) return nativeCheck + 1 < args.Length ? NativeOverviewCheck.Run(Path.GetFullPath(args[nativeCheck + 1])) : 1;
        var liveLab = Array.IndexOf(args, "--live-overview-lab");
        if (liveLab >= 0) return liveLab + 1 < args.Length ? LiveOverviewLab.Run(Path.GetFullPath(args[liveLab + 1]),
            args.Contains("--lab-interactive", StringComparer.Ordinal), args.Contains("--lab-solid", StringComparer.Ordinal)) : 1;
        FrostedBackdrop.DirectReadback = args.Contains("--direct-readback", StringComparer.Ordinal);
        var overviewBenchmark = Array.IndexOf(args, "--overview-benchmark");
        if (overviewBenchmark >= 0) return overviewBenchmark + 1 < args.Length ? OverviewLoadBenchmark.Run(Path.GetFullPath(args[overviewBenchmark + 1])) : 1;
        var sceneCheck = Array.IndexOf(args, "--scene-check");
        if (sceneCheck >= 0) return sceneCheck + 1 < args.Length ? OverviewSceneCheck.Run(Path.GetFullPath(args[sceneCheck + 1])) : 1;
        var motionCheck = Array.IndexOf(args, "--motion-check");
        if (motionCheck >= 0) return motionCheck + 1 < args.Length ? OverviewRenderCheck.Run(Path.GetFullPath(args[motionCheck + 1])) : 1;
        var metricsIndex = Array.IndexOf(args, "--motion-metrics");
        if (metricsIndex >= 0)
        {
            if (metricsIndex + 1 >= args.Length) return 1;
            SpatialOverviewForm.MotionMetricsPath = Path.GetFullPath(args[metricsIndex + 1]);
        }
        var smoke = args.Contains("--smoke-check", StringComparer.Ordinal);
        var journalIndex = Array.IndexOf(args, "--journal");
        if (journalIndex >= 0 && journalIndex + 1 >= args.Length) return 1;
        var journalPath = journalIndex < 0 ? null : args[journalIndex + 1];
        var bookmarkIndex = Array.IndexOf(args, "--bookmarks");
        if (bookmarkIndex >= 0 && bookmarkIndex + 1 >= args.Length) return 1;
        var bookmarkPath = bookmarkIndex < 0 ? null : args[bookmarkIndex + 1];
        if (smoke)
        {
            PixelFont.VerifyGlyphs();
            var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ui-smoke", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture);
            bookmarkPath = Path.Combine(fixture, "bookmarks.json");
            journalPath = Path.Combine(fixture, "recovery.json");
        }
        using var form = new MainForm(smoke ? new PreviewWindowCatalog() : new DesktopWindowCatalog(), showWithoutActivation: smoke, journalPath: journalPath, bookmarksPath: bookmarkPath,
            reviewPanel: args.Contains("--review-panel", StringComparer.Ordinal));
        if (smoke)
        {
            // Realize child HWNDs without making an interactive window visible.
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            Directory.CreateDirectory(output);
            bitmap.Save(Path.Combine(output, "shell-smoke.png"));
            form.PrepareSampleSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "sample-smoke.png"));
            form.PrepareCatalogSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "catalog-smoke.png"));
            form.PrepareDisplaySmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "display-smoke.png"));
            form.PrepareLiveSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "live-map-smoke.png"));
            form.PrepareRecoverySmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "recovery-smoke.png"));
            form.CheckRecoveryErrorSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "recovery-error-smoke.png"));
            form.PrepareBookmarkSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "bookmarks-smoke.png"));
            form.PrepareBookmarkSearchSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "bookmarks-search-smoke.png"));
            form.CheckBookmarkCommandsSmoke();
            File.WriteAllText(bookmarkPath!, "{intentionally-malformed-smoke-fixture");
            form.ReloadBookmarksSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "bookmarks-readonly-smoke.png"));
            form.ResetBookmarksSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "bookmarks-reset-smoke.png"));
            form.PrepareRuntimeChromeSmoke();
            System.Windows.Forms.Application.DoEvents();
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "runtime-shell-smoke.png"));
            var regularSize = form.ClientSize;
            form.ClientSize = new(800, 560);
            System.Windows.Forms.Application.DoEvents();
            using (var compact = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(compact, new Rectangle(Point.Empty, form.Size)); compact.Save(Path.Combine(output, "runtime-compact-smoke.png")); }
            form.ClientSize = regularSize;
            form.CheckTraySmoke();
            form.PreparePopupSmoke();
            foreach (var page in new (string Name, int Index, int Sub)[] { ("console-home", 0, 0), ("console-windows", 1, 0), ("console-displays", 1, 1), ("console-map", 1, 2), ("console-recovery", 2, 0) })
            {
                form.PrepareRuntimePageSmoke(page.Index, page.Sub); System.Windows.Forms.Application.DoEvents();
                using var portrait = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(portrait, new Rectangle(Point.Empty, form.Size)); portrait.Save(Path.Combine(output, page.Name + "-smoke.png"));
            }
            form.PrepareRuntimePageSmoke(0);
            foreach (var warm in new[] { true, false })
            {
                ChromeTheme.SetPalette(BorderlessFlatWindow.Core.Appearance.CandyPalette.FromSamples(warm
                    ? [new(230, 95, 30), new(210, 80, 20), new(230, 160, 60)] : [new(20, 90, 220), new(35, 110, 210), new(75, 40, 190)]));
                ChromeTheme.Apply(form); System.Windows.Forms.Application.DoEvents();
                using var colored = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(colored, new Rectangle(Point.Empty, form.Size)); colored.Save(Path.Combine(output, warm ? "console-warm-smoke.png" : "console-cool-smoke.png"));
            }
            ChromeTheme.SetPalette(BorderlessFlatWindow.Core.Appearance.CandyPalette.Default); ChromeTheme.Apply(form);
            using (var hud = new NavigationHud { Opacity = 0 })
            {
                hud.Present("Ctrl+Alt+Space", new(-12000, 8500));
                System.Windows.Forms.Application.DoEvents();
                using var hudBitmap = new Bitmap(hud.Width, hud.Height);
                hud.DrawToBitmap(hudBitmap, new Rectangle(Point.Empty, hud.Size));
                hudBitmap.Save(Path.Combine(output, "navigation-hud-smoke.png"));
                hud.Hide();
            }
            using (var settings = new NavigationSettingsDialog(new(wheelDistance: 240), showWithoutActivation: true) { Opacity = 0 })
            {
                settings.Show();
                System.Windows.Forms.Application.DoEvents();
                using var settingsBitmap = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(settingsBitmap, new Rectangle(Point.Empty, settings.Size));
                settingsBitmap.Save(Path.Combine(output, "navigation-settings-smoke.png"));
                settings.Hide();
            }
            using var previewLabels = new BorderlessFlatWindow.Application.Bookmarks.BookmarkLibrary(
                () => new BorderlessFlatWindow.Windows.Bookmarks.JsonBookmarkStore(Path.Combine(Path.GetDirectoryName(bookmarkPath!)!, "visual-bookmarks.json")));
            previewLabels.Reload(); previewLabels.Add("工作", new(0, 0)); previewLabels.Add("资料", new(1200, -800)); previewLabels.Add("灵感", new(-1600, 400));
            using (var overview = new SpatialOverviewForm([
                new(Guid.NewGuid(), "编辑器", new(new(-900, 0), new(640, 480))),
                new(Guid.NewGuid(), "浏览器", new(new(0, 0), new(800, 600))),
                new(Guid.NewGuid(), "资料", new(new(0, -700), new(640, 480))),
                new(Guid.NewGuid(), "聊天", new(new(1000, 100), new(480, 700)))],
                new(new(-200, 100), new(1920, 1080)), new(0, 0, 1200, 800), showWithoutActivation: true, bookmarks: previewLabels)
            { Opacity = 0 })
            {
                overview.Show();
                System.Windows.Forms.Application.DoEvents();
                using var overviewBitmap = new Bitmap(overview.Width, overview.Height);
                overview.DrawToBitmap(overviewBitmap, new Rectangle(Point.Empty, overview.Size));
                overviewBitmap.Save(Path.Combine(output, "overview-smoke.png"));
                overview.SetSmokeEntrance(.45); System.Windows.Forms.Application.DoEvents();
                overview.DrawToBitmap(overviewBitmap, new Rectangle(Point.Empty, overview.Size)); overviewBitmap.Save(Path.Combine(output, "overview-transition-smoke.png"));
                overview.EndSmokeEntrance();
                overview.SetSmokeQuery("资料");
                System.Windows.Forms.Application.DoEvents();
                overview.DrawToBitmap(overviewBitmap, new Rectangle(Point.Empty, overview.Size));
                overviewBitmap.Save(Path.Combine(output, "overview-search-smoke.png"));
                overview.SetSmokeQuery(""); overview.Size = new(800, 560);
                System.Windows.Forms.Application.DoEvents();
                using (var compact = new Bitmap(overview.Width, overview.Height))
                { overview.DrawToBitmap(compact, new Rectangle(Point.Empty, overview.Size)); compact.Save(Path.Combine(output, "overview-compact-smoke.png")); }
                overview.Font = PixelFont.Create(18); System.Windows.Forms.Application.DoEvents();
                using (var largeText = new Bitmap(overview.Width, overview.Height))
                { overview.DrawToBitmap(largeText, new Rectangle(Point.Empty, overview.Size)); largeText.Save(Path.Combine(output, "overview-large-text-smoke.png")); }
                overview.Hide();
            }
            var inspection = new DesktopWindowCatalog().Inspect();
            var hostBounds = form.Bounds; var hostBorder = form.FormBorderStyle; var hostTitle = form.Text; var hostTopMost = form.TopMost;
            var hostReturned = false;
            using (var embedded = new SpatialOverviewForm([
                new(Guid.NewGuid(), "总览承载检查", new(new(-600, 100), new(640, 480)))],
                new(new(240, 0), new(1920, 1080)), new(0, 0, 1200, 800), showWithoutActivation: true))
            {
                form.PresentOverview(embedded, () => hostReturned = true);
                System.Windows.Forms.Application.DoEvents();
                if (!embedded.TopLevel || embedded.Parent is not null) throw new InvalidOperationException("Overview is not an independent top-level surface.");
                using var hostBitmap = new Bitmap(embedded.Width, embedded.Height);
                embedded.DrawToBitmap(hostBitmap, new Rectangle(Point.Empty, embedded.Size));
                hostBitmap.Save(Path.Combine(output, "overview-host-smoke.png"));
                embedded.CancelOverview();
                System.Windows.Forms.Application.DoEvents();
            }
            if (!hostReturned || form.Bounds != hostBounds || form.FormBorderStyle != hostBorder || form.Text != hostTitle || form.TopMost != hostTopMost)
                throw new InvalidOperationException("Overview did not restore its host window.");
            var tracking = form.GetTrackingDiagnostics();
            File.WriteAllText(Path.Combine(output, "shell-smoke.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    os = Environment.OSVersion.VersionString,
                    candidateCount = inspection.AdmittedWindows.Count,
                    inspectedCount = inspection.Entries.Count,
                    liveMapWindowCount = tracking.WindowCount,
                    winEventWatching = tracking.Watching,
                    reasons = inspection.Entries.GroupBy(entry => entry.Admission.Reason.ToString())
                        .ToDictionary(group => group.Key, group => group.Count()),
                    sampleWindows = 4,
                    desktopMutationEnabled = false
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            form.Close();
            return 0;
        }
        System.Windows.Forms.Application.Run(form);
        return 0;
    }
}
