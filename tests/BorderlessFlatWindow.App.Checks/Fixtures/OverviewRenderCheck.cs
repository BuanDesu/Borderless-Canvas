using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

/// <summary>Opt-in interactive compositor check. Synthetic pixels only; never captures the desktop.</summary>
internal static class OverviewRenderCheck
{
    internal static int Run(string output)
    {
        var results = new List<object>();
        foreach (var size in new[] { new Size(1920, 1080), new Size(3440, 1440) })
        {
            using var background = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using var target = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using var buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(background)) g.Clear(Color.FromArgb(231, 236, 249));
            using (var g = Graphics.FromImage(target))
            using (var fill = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.FromArgb(190, 211, 255), Color.FromArgb(255, 216, 237), 35))
                g.FillRectangle(fill, new Rectangle(Point.Empty, size));
            var cpu = new List<double>();
            using (var g = Graphics.FromImage(buffer))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = .5f });
                for (var i = 0; i < 32; i++)
                {
                    var watch = Stopwatch.StartNew();
                    // Previous production transition: full image + full alpha image every frame.
                    g.DrawImage(background, new Rectangle(Point.Empty, size));
                    g.DrawImage(target, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
                    g.Flush(FlushIntention.Sync);
                    if (i > 3) cpu.Add(watch.Elapsed.TotalMilliseconds);
                }
            }
            var gaps = new List<double>(); var work = new List<double>();
            var upload = Stopwatch.StartNew();
            using var surface = new CompositionSurface(background, target) { Dock = DockStyle.Fill };
            var uploadMs = upload.Elapsed.TotalMilliseconds;
            using var form = new CheckWindow { ClientSize = size, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = Point.Empty, Text = "BFW synthetic composition check" };
            form.Controls.Add(surface);
            _ = surface.Handle;
            using var sprite = new Bitmap(640, 480);
            using (var g = Graphics.FromImage(sprite)) g.Clear(Color.MediumPurple);
            surface.SetMovingImage(sprite, new(80, 80, 640, 480), new(120, 100, 240, 180));
            var clock = new Stopwatch(); double previous = 0; var frames = 0; var timedOut = false;
            var phase = 0;
            using var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
            watchdog.Tick += (_, _) => { timedOut = true; form.Close(); };
            surface.Frame += progress =>
            {
                var started = Stopwatch.GetTimestamp(); var elapsed = clock.Elapsed.TotalMilliseconds;
                if (frames++ > 0) gaps.Add(elapsed - previous); previous = elapsed;
                work.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (phase == 0 && progress > .45) { phase = 1; surface.Retarget(false, 700); }
                else if (phase == 1 && progress < .2) { phase = 2; surface.Retarget(true, 700); }
                else if (phase == 2 && progress >= 1) { surface.Stop(); form.BeginInvoke(form.Close); }
            };
            form.Shown += (_, _) => { clock.Start(); watchdog.Start(); surface.Start(700); };
            System.Windows.Forms.Application.Run(form);
            surface.Stop(); var before = frames;
            for (var i = 0; i < 3; i++) System.Windows.Forms.Application.DoEvents();
            if (timedOut || frames < 3 || frames != before || phase != 2) throw new InvalidOperationException("Composition reversal, clock or teardown failed.");
            double P95(List<double> values) { values.Sort(); return values[(int)Math.Ceiling(values.Count * .95) - 1]; }
            results.Add(new { size.Width, size.Height, Tier = CompositionSurface.RenderingTier, Frames = frames,
                PreviousCpuBlendP95Ms = P95(cpu), UploadMs = uploadMs, CompositionGapP95Ms = P95(gaps), CompositionGapMaxMs = gaps.Max(),
                EmptyCallbackP95Ms = P95(work), Reversals = phase, CompositorSprite = surface.HasMovingImage, DesktopCaptured = false });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private sealed class CheckWindow : Form { protected override bool ShowWithoutActivation => true; }
}
