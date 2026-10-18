using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var smoke = args.Contains("--smoke-check", StringComparer.Ordinal);
        var journalIndex = Array.IndexOf(args, "--journal");
        if (journalIndex >= 0 && journalIndex + 1 >= args.Length) return 1;
        var journalPath = journalIndex < 0 ? null : args[journalIndex + 1];
        using var form = new MainForm(new DesktopWindowCatalog(), showWithoutActivation: smoke, journalPath: journalPath);
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
            using (var overview = new SpatialOverviewForm([
                new(Guid.NewGuid(), "编辑器", new(new(-900, 0), new(640, 480))),
                new(Guid.NewGuid(), "浏览器", new(new(0, 0), new(800, 600))),
                new(Guid.NewGuid(), "资料", new(new(0, -700), new(640, 480))),
                new(Guid.NewGuid(), "聊天", new(new(1000, 100), new(480, 700)))],
                new(new(-200, 100), new(1920, 1080)), new(0, 0, 1200, 800), showWithoutActivation: true) { Opacity = 0 })
            {
                overview.Show();
                System.Windows.Forms.Application.DoEvents();
                using var overviewBitmap = new Bitmap(overview.Width, overview.Height);
                overview.DrawToBitmap(overviewBitmap, new Rectangle(Point.Empty, overview.Size));
                overviewBitmap.Save(Path.Combine(output, "overview-smoke.png"));
                overview.SetSmokeQuery("资料");
                System.Windows.Forms.Application.DoEvents();
                overview.DrawToBitmap(overviewBitmap, new Rectangle(Point.Empty, overview.Size));
                overviewBitmap.Save(Path.Combine(output, "overview-search-smoke.png"));
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
                using var hostBitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(hostBitmap, new Rectangle(Point.Empty, form.Size));
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
