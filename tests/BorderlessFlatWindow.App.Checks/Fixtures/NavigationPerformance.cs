using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal static void MeasureNavigationPerformance(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var directory = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Never show the manager, install global hooks, enumerate or manage desktop windows.
        using var form = new MainForm(new PreviewWindowCatalog(empty: true), showWithoutActivation: true,
            journalPath: Path.Combine(directory, "recovery.json"), bookmarksPath: Path.Combine(directory, "bookmarks.json"),
            settingsPath: Path.Combine(directory, "settings.json"));
        _ = form.Handle;
        var results = new List<object>();
        foreach (var count in new[] { 0, 64, 256 })
        {
            var snapshot = new WindowCatalogSnapshot(DateTimeOffset.UtcNow, Enumerable.Range(0, count).Select(index =>
                new InspectedWindow(Guid.NewGuid(), new WindowObservation
                {
                    Handle = (nint)(0x70000000 + index * 16), ProcessId = 0x70000000, ProcessCreationFileTime = 1,
                    Title = "Synthetic " + index, ClassName = "BFW.Performance.Synthetic", IsAlive = true, IsVisible = true,
                    MetadataAvailable = true, IsOnCurrentDesktop = true, IsCloaked = false,
                    Bounds = new(new(index * 20, 20), new(300, 200))
                }, new(WindowExclusionReason.None))).ToArray());
            form.snapshot = snapshot;
            results.Add(form.management.MeasureFrames(snapshot));
            results.Add(Measure("hidden-catalog", count, form.RenderSnapshot));
        }
        results.Add(form.management.MeasureInputIdle(touch: false));
        results.Add(form.management.MeasureInputIdle(touch: true));
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS Synthetic navigation measurements saved (no desktop capture or management)");
    }

    internal static object Measure(string scenario, int windows, Action action)
    {
        const int iterations = 1000;
        for (var i = 0; i < 50; i++) action();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) action();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return new { scenario, windows, iterations, elapsedMs = elapsed,
            uiThreadBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
            processCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds };
    }
}

internal sealed partial class ManagedCanvasControl
{
    internal object MeasureFrames(WindowCatalogSnapshot snapshot)
    {
        automaticAdmission.Checked = false;
        if (session?.IsRunning != true) session!.Start([], DateTimeOffset.UtcNow);
        lastSnapshot = snapshot;
        return MainForm.Measure("unchanged-navigation-frame", snapshot.Entries.Count, NavigationFrame);
    }

    internal object MeasureInputIdle(bool touch)
    {
        lastSnapshot = new(DateTimeOffset.UtcNow, []);
        var platform = new PerformanceInputPlatform();
        globalInput = new(CreateInputWake(), inputOptions, null, platform);
        ConfigurePointerScreens(); RefreshState(); globalInput.SynchronizeRoutingCheck();
        if (touch) { touchNavigation = new(); globalInput.SetTouchNavigation(true); }
        else globalInput.MouseRoutingCheck(0x200, new(200, 200));
        globalInput.SynchronizeRoutingCheck();
        // Optional lookup lets the identical fixture measure the original App
        // sources, which predate this timer. Production never uses reflection.
        var healthTimer = typeof(ManagedCanvasControl).GetField("inputHealthTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(this) as System.Windows.Forms.Timer;
        var ticks = 0; var refreshes = 0; var healthTicks = 0;
        void OnTick(object? sender, EventArgs args) => ticks++;
        void OnRefresh() => refreshes++;
        void OnHealth(object? sender, EventArgs args) => healthTicks++;
        navigationTimer.Tick += OnTick; StatusChanged += OnRefresh;
        if (healthTimer is not null) { healthTimer.Tick += OnHealth; healthTimer.Start(); }
        navigationTimer.Start();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started).TotalSeconds < 3)
        { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(2); }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        navigationTimer.Tick -= OnTick; StatusChanged -= OnRefresh;
        if (healthTimer is not null) { healthTimer.Tick -= OnHealth; healthTimer.Stop(); }
        CloseTouchNavigation(discard: true); navigationTimer.Stop();
        var available = globalInput.State.Available;
        globalInput.Dispose(); globalInput = null;
        if (!available) throw new InvalidOperationException("Idle benchmark input watchdog fired.");
        return new { scenario = touch ? "touch-idle-worker" : "desktop-idle-worker", seconds = elapsed, ticks, healthTicks, refreshes,
            uiThreadBytesPerSecond = bytes / elapsed, processCpuMs = cpuMs };
    }

    private sealed class PerformanceInputPlatform : INavigationInputPlatform
    {
        public long TickCount => Environment.TickCount64;
        public nint Foreground => 0;
        public bool MenuActive => false;
        public bool Down(int key) => false;
        public bool IsDesktop(PointD point) => true;
        public bool GetPointer(out PointD point) { point = new(200, 200); return true; }
        public PixelRect VirtualScreen => new(0, 0, 1920, 1080);
        public PixelRect PrimaryScreen => VirtualScreen;
        public bool ReadClip(out PixelRect rectangle) { rectangle = VirtualScreen; return true; }
        public bool WriteClip(PixelRect? rectangle) => throw new InvalidOperationException("Benchmark must not clip.");
        public bool SetPointer(PointD point) => throw new InvalidOperationException("Benchmark must not move the cursor.");
    }
}
