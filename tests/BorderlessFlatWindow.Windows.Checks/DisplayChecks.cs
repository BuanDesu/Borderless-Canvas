using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Displays;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class DisplayChecks
{
    internal static void Run(Form fixture, Action<string, Action> check)
    {
        var catalog = new DesktopDisplayCatalog();
        var baseline = catalog.ReadDisplays();
        check("Native monitor and work-area geometry matches PMv2 WinForms screens", () =>
        {
            var screens = Screen.AllScreens;
            Require(baseline.Monitors.Count == screens.Length, "Monitor count mismatch.");
            foreach (var monitor in baseline.Monitors)
            {
                var reference = screens.Single(screen => screen.DeviceName == monitor.DeviceName);
                Require(monitor.IsPrimary == reference.Primary && Matches(monitor.Bounds, reference.Bounds)
                    && Matches(monitor.WorkArea, reference.WorkingArea), "Monitor/work-area pixels mismatch.");
            }
        });
        check("Monitor effective DPI matches a real PMv2 fixture on that monitor", () =>
        {
            var name = Screen.FromHandle(fixture.Handle).DeviceName;
            var monitor = baseline.Monitors.Single(monitor => monitor.DeviceName == name);
            Require(monitor.EffectiveDpi is > 0 && monitor.EffectiveDpi == GetDpiForWindow(fixture.Handle), "DPI differs from fixture.");
        });
        check("Unaware callers receive physical monitor pixels and retain their DPI context", () =>
            FromUnaware(() =>
            {
                var sample = catalog.ReadDisplays();
                Require(SameGeometry(sample, baseline), "DPI virtualization changed monitor coordinates or DPI.");
                Require(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -1), "Monitor read leaked DPI context.");
            }));
        check("Unaware callers receive physical window bounds and retain their DPI context", () =>
        {
            var windowCatalog = new DesktopWindowCatalog(0);
            var expected = windowCatalog.Inspect().Entries.Single(entry => entry.Observation.Handle == fixture.Handle).Observation.Bounds;
            FromUnaware(() =>
            {
                var actual = windowCatalog.Inspect().Entries.Single(entry => entry.Observation.Handle == fixture.Handle).Observation.Bounds;
                Require(expected == actual, "Window rect was DPI-virtualized.");
                Require(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -1), "Window read leaked DPI context.");
            });
        });
        check("Repeated DPI probes release all HWNDs and preserve the foreground window", () =>
        {
            var windows = ThreadWindows();
            var foreground = GetForegroundWindow();
            for (var i = 0; i < 8; i++) catalog.ReadDisplays();
            Require(windows.SetEquals(ThreadWindows()), "Hidden DPI probe leaked a window.");
            Require(foreground == GetForegroundWindow(), "DPI probe changed foreground window.");
        });
        check("Display inspection rejects MTA calls before accessing desktop state", () =>
        {
            var rejected = Task.Run(() =>
            {
                try { catalog.ReadDisplays(); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            Require(rejected, "MTA call was accepted.");
        });

        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "native-displays.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            baseline.CapturedAt, baseline.IsSingleScreen100Percent,
            monitors = baseline.Monitors.Select(monitor => new
            { monitor.DeviceName, monitor.IsPrimary, monitor.Bounds, monitor.WorkArea, monitor.EffectiveDpi }),
            negativeCoordinateHardwareTested = baseline.Monitors.Any(monitor => monitor.Bounds.Left < 0 || monitor.Bounds.Top < 0),
            desktopMutationEnabled = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool Matches(Core.Geometry.PixelRect pixel, Rectangle reference) =>
        pixel.Left == reference.Left && pixel.Top == reference.Top && pixel.Right == reference.Right && pixel.Bottom == reference.Bottom;
    private static bool SameGeometry(DisplayLayout a, DisplayLayout b) =>
        a.Monitors.OrderBy(monitor => monitor.DeviceName).SequenceEqual(b.Monitors.OrderBy(monitor => monitor.DeviceName));
    private static void FromUnaware(Action action)
    {
        var previous = SetThreadDpiAwarenessContext(-1);
        if (previous == 0) throw new InvalidOperationException("Cannot enter unaware test context.");
        try { action(); }
        finally { Require(SetThreadDpiAwarenessContext(previous) != 0, "Cannot restore test DPI context."); }
    }

    private static HashSet<nint> ThreadWindows()
    {
        var windows = new HashSet<nint>();
        Require(EnumThreadWindows(GetCurrentThreadId(), (handle, _) => { windows.Add(handle); return true; }, 0), "Cannot enumerate fixture thread HWNDs.");
        return windows;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private delegate bool EnumCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint a, nint b);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumCallback callback, nint parameter);
}
