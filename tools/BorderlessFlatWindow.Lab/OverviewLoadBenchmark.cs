using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

using System.Drawing;
using System.Windows.Forms;

namespace BorderlessFlatWindow.App;

/// <summary>Opt-in native thumbnail/scene load measurement; never captures the desktop or injects input.</summary>
internal static class OverviewLoadBenchmark
{
    internal static int Run(string output)
    {
        if (File.Exists(output)) throw new IOException("Choose a new report path; existing measurements are preserved.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var process = Process.GetCurrentProcess();
        var foreground = WindowEventListener.ForegroundWindow;
        var warming = Stopwatch.StartNew(); SpatialOverviewScene.WarmUp(); var warmup = warming.Elapsed.TotalMilliseconds;
        var results = new List<object>();
        var sequence = 0;
        Exception? failure = null;
        using var coordinator = new FixtureForm { Opacity = 0, ClientSize = new(1, 1), Location = new(-12000, -12000) };
        coordinator.Shown += async (_, _) =>
        {
            try
            {
                foreach (var count in new[] { 1, 10, 30, 60 })
                foreach (var parkedFraction in new[] { 0d, .5 })
                {
                    var sources = new List<SourceForm>();
                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var source = new SourceForm(i)
                            {
                                Location = i < count * parkedFraction ? new(-12000 - i * 4, -12000) : new(40 + i % 6 * 28, 60 + i / 6 * 20)
                            };
                            sources.Add(source); source.Show(); source.Update();
                        }
                        // Dispatch creation/paint messages outside the measured open path.
                        await Task.Delay(80);
                        foreach (var enabled in new[] { false, true })
                        for (var sample = 0; sample < 3; sample++)
                            results.Add(await Measure(sources, enabled, (int)Math.Ceiling(count * parkedFraction), sample, sequence++, process));
                    }
                    finally { foreach (var source in sources) source.Dispose(); }
                }
            }
            catch (Exception error) { failure = error; }
            finally { coordinator.Close(); }
        };
        System.Windows.Forms.Application.Run(coordinator);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        // No activation is requested. Record interference rather than changing the user's foreground back.
        var report = new
        {
            SchemaVersion = 1, Scope = "Own native sources and production retained scene; no desktop input/capture",
            DesktopCaptured = false, InputInjected = false, WarmupMs = warmup,
            ForegroundUnchanged = foreground == WindowEventListener.ForegroundWindow,
            OsBuild = Environment.OSVersion.Version.Build, Tier = CompositionSurface.RenderingTier, Results = results
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Overview benchmark: {results.Count} samples, own sources only; report: {output}");
        return 0;
    }

    private static async Task<object> Measure(IReadOnlyList<SourceForm> sources, bool enabled, int parked, int sample, int sequence, Process process)
    {
        var start = Stopwatch.GetTimestamp();
        process.Refresh(); var cpuBefore = process.TotalProcessorTime; var privateBefore = process.PrivateMemorySize64; var handlesBefore = process.HandleCount;
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var columns = (int)Math.Ceiling(Math.Sqrt(sources.Count * 1.5));
        var windows = sources.Select((source, i) => new CanvasWindow(Guid.NewGuid(), "Fixture " + (i + 1), new(new(i % columns * 440, i / columns * 300), new(400, 260)))).ToArray();
        var camera = new Camera2D(PointD.Zero, new(1920, 1080));
        var model = new SpatialOverviewSession(windows, camera, new(1000, 700), windows[0].Id);
        var modelMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bounds = Screen.PrimaryScreen!.WorkingArea;
        var size = new Size(Math.Min(1920, bounds.Width), Math.Min(1080, bounds.Height));
        using var host = new FixtureForm { ClientSize = size, Location = bounds.Location, Text = "BFW overview load fixture" };
        using var scene = new SpatialOverviewScene(model, camera.Position, camera.ViewportSize, [camera.Bounds], ChromeTheme.Candy, synthetic: true) { Dock = DockStyle.Fill };
        host.Controls.Add(scene); _ = host.Handle; _ = scene.Handle;
        var constructMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds - modelMs;
        using var background = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(background)) graphics.Clear(Color.FromArgb(213, 220, 245));
        var uploading = Stopwatch.GetTimestamp(); scene.SetBackdrop(background); scene.SetWallpaper(background);
        var uploadMs = Stopwatch.GetElapsedTime(uploading).TotalMilliseconds;
        scene.SetWorkspaces([new(null, "原点", PointD.Zero), new(Guid.NewGuid(), "切片", new(640, 320)), new(Guid.NewGuid(), "空视窗", new(1e6, 1e6))]);
        var identities = windows.Select((window, i) => (window.Id, Identity: new NativeWindowIdentity(sources[i].Handle, (uint)Environment.ProcessId, process.StartTime.ToFileTimeUtc()))).ToDictionary(p => p.Id, p => p.Identity);
        var port = new MeasuredPort(new DwmOverviewPreviewPort(host.Handle));
        using var previews = new OverviewPreviewSession(port);
        PreviewCard[] cards = [];
        var layoutMs = 0d; var syncMs = 0d; var syncCalls = 0; var refreshing = false; var ready = false;
        void Synchronize()
        {
            if (!ready || refreshing || host.IsDisposed) return;
            refreshing = true;
            try
            {
                var begun = Stopwatch.GetTimestamp();
                var next = scene.PreviewLayout().Select(c => new PreviewCard(c.Id, identities[c.Window], c.Bounds, c.Fallback, c.Fraction)).ToArray();
                layoutMs += Stopwatch.GetElapsedTime(begun).TotalMilliseconds; begun = Stopwatch.GetTimestamp();
                previews.Synchronize(next, null, enabled, 0);
                syncMs += Stopwatch.GetElapsedTime(begun).TotalMilliseconds; syncCalls++;
                cards = next.Select(c => c.SourceFraction is null && c.ContentBounds is { } area && previews.SourceSize(c.Id) is { } sourceSize
                    ? c with { ContentBounds = OverviewPreviewSession.Fit(sourceSize, area) } : c).ToArray();
            }
            finally { refreshing = false; }
        }
        scene.GeometryChanged += Synchronize;
        var firstMs = 0d; var frames = 0; var previous = 0d; var gaps = new List<double>(); var callbackMs = new List<double>(); var timeout = false;
        var nativeCount = 0; var missing = 0;
        using var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
        watchdog.Tick += (_, _) => { timeout = true; host.Close(); };
        scene.Frame += position =>
        {
            var begun = Stopwatch.GetTimestamp(); var now = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (frames++ == 0) firstMs = now; else gaps.Add(now - previous);
            previous = now; previews.Animate(cards, (byte)Math.Clamp((int)Math.Round(position * 255), 0, 255));
            callbackMs.Add(Stopwatch.GetElapsedTime(begun).TotalMilliseconds);
            if (position >= 1)
            {
                nativeCount = previews.ResourceCount;
                missing = previews.States.Values.Count(s => s == PreviewState.Unavailable);
                scene.StopMotion(); host.BeginInvoke(host.Close);
            }
        };
        var shownMs = 0d;
        host.Shown += (_, _) =>
        {
            scene.EnsureLayout(); scene.RefreshWindows(); ready = true; Synchronize(); scene.Seek(0);
            shownMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds; watchdog.Start(); scene.Retarget(true, 220);
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.FormClosed += (_, _) => closed.TrySetResult();
        host.Show(); await closed.Task;
        var closing = Stopwatch.GetTimestamp(); previews.Dispose(); scene.StopMotion();
        var releaseMs = Stopwatch.GetElapsedTime(closing).TotalMilliseconds;
        if (timeout || frames < 2 || previews.ResourceCount != 0 || port.Active != 0 || enabled && (nativeCount < sources.Count || missing != 0)
            || !enabled && nativeCount != 0) throw new InvalidOperationException($"Overview load fixture failed: timeout={timeout}, frames={frames}, native={nativeCount}, missing={missing}, active={port.Active}.");
        scene.Dispose(); background.Dispose();
        process.Refresh(); gaps.Sort(); callbackMs.Sort();
        return new
        {
            Sequence = sequence, Sample = sample, WindowCount = sources.Count, ParkedCount = parked, PreviewsEnabled = enabled,
            size.Width, size.Height, Dpi = scene.DeviceDpi,
            ModelMs = modelMs, ConstructMs = constructMs, UploadMs = uploadMs, LayoutMs = layoutMs,
            SyncMs = syncMs, SyncCalls = syncCalls, ShownMs = shownMs, FirstCallbackMs = firstMs,
            Frames = frames, GapP95Ms = Percentile(gaps, .95), GapMaxMs = gaps.LastOrDefault(),
            CallbackP95Ms = Percentile(callbackMs, .95), CallbackMaxMs = callbackMs.LastOrDefault(),
            NativeCount = nativeCount, port.Peak, port.Created, port.Released, port.Updates, port.CreateMs, port.UpdateMs,
            ReleaseMs = releaseMs, CpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            AllocatedBytes = GC.GetTotalAllocatedBytes() - allocatedBefore,
            PrivateBytesBefore = privateBefore, PrivateBytesAfter = process.PrivateMemorySize64,
            HandlesBefore = handlesBefore, HandlesAfter = process.HandleCount
        };
    }
    private static double Percentile(List<double> values, double fraction) => values.Count == 0 ? 0 : values[Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * fraction) - 1)];
    private class FixtureForm : Form
    {
        internal FixtureForm() { ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.FixedToolWindow; AutoScaleMode = AutoScaleMode.None; }
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class SourceForm : FixtureForm
    {
        internal SourceForm(int index) { Text = "BFW load source " + (index + 1); ClientSize = new(400, 260); BackColor = Color.FromArgb(160 + index % 80, 160 + index * 7 % 80, 160 + index * 11 % 80); }
    }
    private sealed class MeasuredPort(IOverviewPreviewPort inner) : IOverviewPreviewPort
    {
        internal int Active, Peak, Created, Released, Updates;
        internal double CreateMs, UpdateMs;
        public IDisposable? BeginBatch() => inner.BeginBatch();
        public IOverviewPreview? Create(NativeWindowIdentity identity)
        {
            var start = Stopwatch.GetTimestamp(); var result = inner.Create(identity); CreateMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (result is null) return null;
            Active++; Created++; Peak = Math.Max(Peak, Active); return new MeasuredPreview(result, this);
        }
        private sealed class MeasuredPreview(IOverviewPreview inner, MeasuredPort owner) : IOverviewPreview
        {
            private bool disposed;
            public bool IsCurrent => inner.IsCurrent;
            public SizeD? SourceSize => inner.SourceSize;
            public bool Update(PixelRect destination) => Update(destination, null, 255);
            public bool Update(PixelRect destination, RectD? sourceFraction, byte opacity)
            {
                var start = Stopwatch.GetTimestamp(); var result = inner.Update(destination, sourceFraction, opacity);
                owner.UpdateMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds; owner.Updates++; return result;
            }
            public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); owner.Active--; owner.Released++; }
        }
    }
}
