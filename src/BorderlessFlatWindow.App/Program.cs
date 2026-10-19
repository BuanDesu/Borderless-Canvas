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
            var inspection = new DesktopWindowCatalog().Inspect();
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
