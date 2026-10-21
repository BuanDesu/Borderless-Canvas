using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;
using Microsoft.Win32;

namespace BorderlessFlatWindow.App;

/// <summary>Opt-in compositor diagnosis. A tiny sample is read only when an owned opaque pattern and owned target form the complete visible stack.</summary>
internal static class NativeBackdropCheck
{
    private sealed record Mode(string Name, bool Activate, bool NonClient = false, bool Alpha = false, bool Solid = false, bool Production = false,
        bool Blocked = false, bool Moved = false);
    private sealed record Measurement(bool Valid, bool Active, bool Foreground, bool NonClient, double? Response, double? Ratio);
    private sealed record Request(int FrameHResult, int SetHResult, int GetHResult, int ReportedType)
    {
        internal bool MaterialAccepted => FrameHResult >= 0 && SetHResult >= 0 && GetHResult >= 0 && ReportedType == 3;
    }
    private readonly record struct Mean(double R, double G, double B)
    {
        internal double Difference(Mean other) => (Math.Abs(R - other.R) + Math.Abs(G - other.G) + Math.Abs(B - other.B)) / 3;
    }
    private sealed record Sample(Mean All, double StripeContrast);
    internal static int Run(string output)
    {
        if (File.Exists(output)) throw new IOException("Use a new backdrop report path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var work = Screen.PrimaryScreen!.WorkingArea;
        var width = Math.Min(960, work.Width - 80); var height = Math.Min(760, work.Height - 80);
        if (width < 450 || height < 450) throw new InvalidOperationException("Backdrop fixture needs a 450px work region.");
        var backgroundBounds = new PixelRect(work.Left + 40, work.Top + 40, work.Left + 40 + width, work.Top + 40 + height);
        var bounds = new PixelRect(backgroundBounds.Left + 24, backgroundBounds.Top + 24, backgroundBounds.Right - 24, backgroundBounds.Bottom - 24);
        var region = new PixelRect(bounds.Left + 64, bounds.Top + 64, bounds.Left + 256, bounds.Top + 96);
        var modes = new[] { new Mode("OpaqueControl", false, Solid: true), new Mode("BlockedControl", false, Solid: true, Blocked: true),
            new Mode("MovedControl", false, Solid: true, Moved: true), new Mode("LegacyInactive", false),
            new Mode("LegacyActive", true), new Mode("LegacyNonClient", true, NonClient: true),
            new Mode("LegacyAlpha", true, Alpha: true), new Mode("LegacyNonClientAlpha", true, NonClient: true, Alpha: true),
            new Mode("ProductionActive", true, Production: true), new Mode("ProductionInactive", false, Production: true) };
        using var process = Process.GetCurrentProcess();
        using var background = new PatternWindow { Bounds = new(backgroundBounds.Left, backgroundBounds.Top, width, height),
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
        using var context = new ApplicationContext();
        using var driver = new System.Windows.Forms.Timer { Interval = 40 };
        using var watchdog = new System.Windows.Forms.Timer { Interval = 30000 };
        SpatialOverviewWindow? surface = null; SpatialOverviewView? view = null;
        PatternWindow? blocker = null;
        var results = new List<object>(); var samples = new List<Sample?>(); var statuses = new List<string>();
        var measurements = new Dictionary<string, Measurement>();
        var index = -1; var state = 0; var started = 0L; var cancelled = false; Exception? failure = null;
        var readbacks = 0; var completingCase = false; var stopping = false; var sampling = false; var effectVerified = false;
        Request? backdrop = null; int? ncSet = null, alphaSet = null;
        var gateAccepted = false;
        var nonClientRead = -1; var nonClientEnabled = -1;
        var highContrast = SystemInformation.HighContrast;
        bool? transparency = null;
        try { if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", null) is int setting) transparency = setting != 0; }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        var powerKnown = GetSystemPowerStatus(out var power);
        var environment = new { HighContrast = highContrast, TransparencyEnabled = transparency, BatterySaver = powerKnown ? (bool?)(power.SystemStatusFlag == 1) : null,
            RemoteSession = SystemInformation.TerminalServerSession, NativeSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
            AlphaSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100), Tier = CompositionSurface.RenderingTier };
        void Stop(Exception? error = null)
        { if (stopping) return; stopping = true; failure ??= error; driver.Stop(); watchdog.Stop(); blocker?.Dispose(); blocker = null; surface?.Dispose(); surface = null; context.ExitThread(); }
        void Next()
        {
            completingCase = true; blocker?.Dispose(); blocker = null; surface?.Dispose(); surface = null; view = null; completingCase = false;
            if (++index >= modes.Length) { Stop(); return; }
            samples.Clear(); statuses.Clear(); state = readbacks = 0; ncSet = alphaSet = null; backdrop = null; gateAccepted = false;
            background.Pattern = 0; background.Refresh();
            var camera = new Camera2D(PointD.Zero, bounds.Size);
            view = new(new SpatialOverviewSession([], camera, bounds.Size), camera.Position, camera.ViewportSize, [camera.Bounds], ChromeTheme.Candy);
            var mode = modes[index];
            surface = new(view, bounds, (handle, allowNative) =>
            {
                if (mode.Solid || !allowNative) return false;
                if (mode.Production)
                {
                    var result = NativeOverviewBackdrop.TryApply(handle);
                    backdrop = new(result.FrameHResult, result.SetHResult, result.GetHResult, result.ReportedType);
                    ncSet = result.NonClientSetHResult; gateAccepted = result.Accepted; return gateAccepted;
                }
                if (mode.NonClient) { var enabled = 2; ncSet = DwmSetWindowAttribute(handle, 2, in enabled, sizeof(int)); }
                if (mode.Alpha)
                {
                    var enabled = 1;
                    alphaSet = environment.AlphaSupported ? DwmSetWindowAttribute(handle, 39, in enabled, sizeof(int)) : unchecked((int)0x80004001);
                }
                // Preserve the previous implementation as an explicit negative control.
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                var frame = DwmExtendFrameIntoClientArea(handle, in margins);
                var type = 3; var set = DwmSetWindowAttribute(handle, 38, in type, sizeof(int));
                var get = DwmGetWindowAttribute(handle, 38, out var actual, sizeof(int));
                backdrop = new(frame, set, get, actual); gateAccepted = backdrop.MaterialAccepted; return gateAccepted;
            }, false);
            view.CancelRequested += () => { cancelled = true; Stop(); };
            view.Command += keys => { if (keys != Keys.Escape) return false; cancelled = true; Stop(); return true; };
            surface.Closed += (_, _) => { if (!completingCase && !stopping) { cancelled = true; Stop(); } };
            view.SetStatus("原生背景验证 · Esc 取消"); view.Seek(1); surface.Show(mode.Activate);
            if (mode.Blocked)
            {
                blocker = new PatternWindow { Bounds = new(region.Left, region.Top, region.Width, region.Height), FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
                blocker.Show(); blocker.Refresh();
            }
            if (mode.Moved && !SetWindowPos(surface.Handle, 0, bounds.Left + 4, bounds.Top, 0, 0, 0x215))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            started = Stopwatch.GetTimestamp();
        }
        driver.Tick += (_, _) =>
        {
            if (sampling || stopping) return;
            sampling = true;
            try
            {
                if (surface is null) { Next(); return; }
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 400) return;
                _ = DwmFlush();
                var snapshot = CaptureOwned(surface.Handle, background.Handle, bounds, backgroundBounds, region, out var status, out var read);
                if (stopping) return;
                if (read) readbacks++;
                samples.Add(snapshot); statuses.Add(status);
                if (++state < 3) { background.Pattern = state; background.Refresh(); started = Stopwatch.GetTimestamp(); return; }
                var local = surface; var mode = modes[index];
                nonClientRead = DwmGetWindowAttribute(local.Handle, 1, out nonClientEnabled, sizeof(int));
                var valid = samples.All(sample => sample is not null);
                double? response = valid ? samples[0]!.All.Difference(samples[1]!.All) : null;
                double? contrast = valid ? samples[2]!.StripeContrast : null;
                double? ratio = response is >= 3 ? contrast / response : null;
                measurements.Add(mode.Name, new(valid, local.Active, WindowEventListener.ForegroundWindow == local.Handle,
                    nonClientRead >= 0 && nonClientEnabled != 0, response, ratio));
                results.Add(new { Mode = mode.Name, Environment = environment, Backdrop = backdrop, GateAccepted = gateAccepted,
                    NonClientSetHResult = ncSet, AlphaSetHResult = alphaSet,
                    NonClientReadHResult = nonClientRead, NonClientEnabled = nonClientRead >= 0 ? (bool?)(nonClientEnabled != 0) : null,
                    WpfActive = local.Active, ForegroundMatches = WindowEventListener.ForegroundWindow == local.Handle,
                    ReadStatuses = statuses.ToArray(), ReadbackCalls = readbacks, SamplesValidated = valid, SourceResponse = response, FineStripeContrast = contrast,
                    SpatialTransferRatio = ratio, RespondsToOwnedBackground = response is >= 3, SpatialAttenuation = ratio is < .5,
                    NegativeControlStable = mode.Solid && response is <= 1, SavedPixels = false });
                if ((mode.Blocked || mode.Moved) && (readbacks != 0 || samples.Any(sample => sample is not null)))
                    throw new InvalidOperationException("An overlapped or moved fixture was read despite its ownership guard.");
                if (mode.Name == "OpaqueControl" && valid && response is > 1)
                    throw new InvalidOperationException("Opaque negative control changed with its underlay.");
                Next();
            }
            catch (Exception error) { Stop(error); }
            finally { sampling = false; }
        };
        watchdog.Tick += (_, _) => Stop(new TimeoutException("Backdrop diagnosis exceeded its bound."));
        try
        {
            background.Show(); background.Refresh(); driver.Start(); watchdog.Start(); System.Windows.Forms.Application.Run(context);
            if (failure is not null) throw new InvalidOperationException("Backdrop diagnosis failed.", failure);
            if (!cancelled && results.Count != modes.Length) throw new InvalidOperationException("Backdrop diagnosis was incomplete.");
            if (!cancelled && measurements["LegacyNonClient"] is { Valid: true, Active: true, Foreground: true, Response: >= 3, Ratio: < .5 }
                && measurements["ProductionActive"] is { Valid: true, Active: true, Foreground: true } actual
                && (actual.Response is not >= 3 || actual.Ratio is not < .5 || !actual.NonClient))
                throw new InvalidOperationException("The production backdrop lost the response verified by the positive control.");
            effectVerified = !cancelled && measurements["ProductionActive"] is { Valid: true, Active: true, Foreground: true, NonClient: true, Response: >= 3, Ratio: < .5 };
        }
        finally
        {
            stopping = true; driver.Stop(); watchdog.Stop(); blocker?.Dispose(); surface?.Dispose(); view?.Dispose();
            File.WriteAllText(output, JsonSerializer.Serialize(new { Cancelled = cancelled, ProductionEffectVerified = effectVerified, Cases = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(effectVerified ? "PASS Production backdrop responds to the owned underlay and attenuates fine stripes; guarded controls perform no readback."
            : "INCONCLUSIVE Native material response was not verified; inspect capture guards and environment before drawing a visual conclusion.");
        return cancelled ? 2 : 0;
    }
    private static Sample? CaptureOwned(nint target, nint background, PixelRect targetBounds, PixelRect backgroundBounds, PixelRect region, out string status, out bool read)
    {
        read = false;
        if (!OwnedStack(target, background, targetBounds, backgroundBounds, region, out status)) return null;
        read = true;
        using var pixels = DesktopReadback.Capture(new(region.Left, region.Top, region.Width, region.Height));
        if (pixels is null) { status = "ReadbackFailed"; return null; }
        if (!OwnedStack(target, background, targetBounds, backgroundBounds, region, out status)) return null;
        var sum = new double[3]; var first = new double[3]; var second = new double[3]; var countA = 0; var countB = 0;
        for (var y = 0; y < pixels.Height; y++)
        for (var x = 0; x < pixels.Width; x++)
        {
            var color = pixels.GetPixel(x, y); var values = new[] { color.R, color.G, color.B };
            var a = ((region.Left - backgroundBounds.Left + x) / 16 & 1) == 0;
            for (var channel = 0; channel < 3; channel++) { sum[channel] += values[channel]; (a ? first : second)[channel] += values[channel]; }
            if (a) countA++; else countB++;
        }
        var count = pixels.Width * pixels.Height;
        var mean = new Mean(sum[0] / count, sum[1] / count, sum[2] / count);
        var contrast = new Mean(first[0] / countA, first[1] / countA, first[2] / countA).Difference(new(second[0] / countB, second[1] / countB, second[2] / countB));
        return new(mean, contrast);
    }
    private static bool OwnedStack(nint target, nint background, PixelRect targetBounds, PixelRect backgroundBounds, PixelRect region, out string status)
    {
        static bool Matches(nint handle, PixelRect expected)
        {
            GetWindowThreadProcessId(handle, out var process);
            return handle != 0 && process == Environment.ProcessId && GetAncestor(handle, 2) == handle && IsWindowVisible(handle) && !IsIconic(handle)
                && (GetWindowLong(handle, -20) & 0x80000) == 0 && GetWindowRect(handle, out var box)
                && box.Left == expected.Left && box.Top == expected.Top && box.Right == expected.Right && box.Bottom == expected.Bottom;
        }
        if (!backgroundBounds.Contains(targetBounds) || !targetBounds.Contains(region) || !Matches(target, targetBounds) || !Matches(background, backgroundBounds))
        { status = "OwnedGeometryChanged"; return false; }
        var targetSeen = false; var backgroundSeen = false; var blocked = false; var unknown = false; var count = 0;
        var enumerated = EnumWindows((handle, _) =>
        {
            if (++count > 8192) { unknown = true; return false; }
            if (handle == target) { targetSeen = true; return true; }
            if (handle == background) { backgroundSeen = true; return false; }
            if (!IsWindowVisible(handle) || IsIconic(handle)) return true;
            if (!GetWindowRect(handle, out var box)) { unknown = true; return false; }
            if (box.Right <= region.Left || box.Left >= region.Right || box.Bottom <= region.Top || box.Top >= region.Bottom) return true;
            if (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) < 0) { unknown = true; return false; }
            if (cloaked != 0) return true;
            blocked = true; return false;
        }, 0);
        _ = enumerated;
        status = blocked ? "OtherOverlap" : unknown || !targetSeen || !backgroundSeen ? "UnknownStack" : "Ready";
        if (status != "Ready") return false;
        if (Matches(target, targetBounds) && Matches(background, backgroundBounds)) return true;
        status = "OwnedGeometryChanged"; return false;
    }
    private sealed class PatternWindow : Form
    {
        internal int Pattern;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000088; return value; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var a = Color.FromArgb(210, 105, 150); var b = Color.FromArgb(70, 150, 220);
            e.Graphics.Clear(Pattern == 1 ? b : a);
            if (Pattern == 2)
            {
                using var brush = new SolidBrush(b);
                for (var x = 16; x < ClientSize.Width; x += 32) e.Graphics.FillRectangle(brush, x, 0, 16, ClientSize.Height);
            }
            base.OnPaint(e);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { internal byte AcLine, BatteryFlags, BatteryPercent, SystemStatusFlag; internal uint Life, FullLife; }
    private delegate bool WindowCallback(nint handle, nint parameter);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint handle, int attribute, in int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint handle, in Margins margins);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
}
