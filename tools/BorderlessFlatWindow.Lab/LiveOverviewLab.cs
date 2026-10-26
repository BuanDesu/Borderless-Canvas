using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

using System.Drawing;
using System.Windows.Forms;

namespace BorderlessFlatWindow.App;

/// <summary>Bounded own-window native-surface experiment. Its reports contain no desktop pixels or external identities.</summary>
internal static class LiveOverviewLab
{
    internal static int Run(string output, bool interactive, bool forceFallback)
    {
        if (File.Exists(output)) throw new IOException("Use a new lab report path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var work = Screen.PrimaryScreen!.WorkingArea;
        var bounds = new PixelRect(work.Left, work.Top, work.Right, work.Bottom);
        using var process = Process.GetCurrentProcess();
        var foreground = GetForegroundWindow();
        var sources = new List<SourceWindow>();
        var results = new List<object>();
        uint warmGdi = 0, warmUser = 0;
        var samples = interactive ? 1 : 6;
        var rejectedInvalidDestination = false;
        try { NativeOverviewBackdrop.TryApply(0); }
        catch (ArgumentException) { rejectedInvalidDestination = true; }
        if (!rejectedInvalidDestination) throw new InvalidOperationException("The backdrop accepted an invalid destination.");
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var window = new SourceWindow(i) { StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(work.Left + 80 + i * 300, work.Top + 110 + i * 80, 360, 240),
                    ShowInTaskbar = false, Text = "BFW synthetic live source" };
                sources.Add(window); window.Show();
            }
            System.Windows.Forms.Application.DoEvents();
            for (var sample = 0; sample < samples; sample++)
            {
                var cancelDuringMotion = sample == 3;
                var backdrop = NativeOverviewBackdrop.Result.Unavailable; var actualBounds = false;
                var beforeGdi = GetGuiResources(process.Handle, 0); var beforeUser = GetGuiResources(process.Handle, 1);
                var model = sources.Select((source, index) => new LiveOverviewLabSurface.Source(
                    new NativeWindowIdentity(source.Handle, (uint)Environment.ProcessId, process.StartTime.ToUniversalTime().ToFileTimeUtc()),
                    new RectD(new PointD(index switch { 0 => 80, 1 => work.Width + 180, _ => -650 }, 110 + index * 280), new SizeD(source.Width, source.Height)),
                    new PixelRect(source.Left, source.Top, source.Right, source.Bottom))).ToArray();
                var result = LiveOverviewLabSurface.Run(bounds, model, handle => new DwmOverviewPreviewPort(handle), (handle, allowNative) =>
                {
                    // Configure only this process's new lab destination, without activation or z-order changes.
                    if (!SetWindowPos(handle, 0, work.Left, work.Top, work.Width, work.Height, 0x214))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                    actualBounds = GetWindowRect(handle, out var rect) && rect.Left == work.Left && rect.Top == work.Top
                        && rect.Right == work.Right && rect.Bottom == work.Bottom;
                    if (!forceFallback && allowNative) backdrop = NativeOverviewBackdrop.TryApply(handle);
                    return backdrop.Accepted;
                }, interactive, cancelDuringMotion);
                var gdiDelta = (long)GetGuiResources(process.Handle, 0) - beforeGdi;
                var userDelta = (long)GetGuiResources(process.Handle, 1) - beforeUser;
                // Lab-only diagnostic, after all measured motion/show phases. Never force collection in the product presenter.
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                System.Windows.Forms.Application.DoEvents();
                var afterGdi = GetGuiResources(process.Handle, 0); var afterUser = GetGuiResources(process.Handle, 1);
                if (sample == 0) { warmGdi = afterGdi; warmUser = afterUser; }
                var withinWarmBaseline = afterGdi <= warmGdi && afterUser <= warmUser;
                results.Add(new { Sample = sample, work.Width, work.Height, RejectedInvalidDestination = rejectedInvalidDestination,
                    ForcedFallback = forceFallback, CancelDuringMotion = cancelDuringMotion, DesktopReadback = false, ForegroundUnchanged = foreground == GetForegroundWindow(),
                    BoundsMatched = actualBounds, Backdrop = backdrop, BackdropAccepted = backdrop.Accepted, Motion = result,
                    SourcePaints = sources.Sum(source => source.Paints), GdiDelta = gdiDelta, UserDelta = userDelta,
                    GdiAfterCollectionDelta = (long)afterGdi - beforeGdi,
                    UserAfterCollectionDelta = (long)afterUser - beforeUser, WithinWarmResourceBaseline = withinWarmBaseline });
                if (result.TimedOut || result.Created != 3 || result.Created != result.Released || result.Failures != 0
                    || result.Callbacks < 3 || result.LateCallbacks != 0 || !result.RenderSubscriptionReleased || !actualBounds
                    || (!interactive && (foreground != GetForegroundWindow()
                        || (cancelDuringMotion ? !result.ClosedDuringMotion || result.Reversals != 0 : result.Reversals != 2 || !result.IdleVerified)
                        || (sample == samples - 1 && !withinWarmBaseline))))
                    throw new InvalidOperationException("Live surface lifecycle, reversal, placement or non-activation check failed.");
            }
        }
        finally
        {
            foreach (var source in sources) source.Dispose();
            File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }

    private sealed class SourceWindow : Form
    {
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
        private readonly int index;
        private int tick;
        internal int Paints { get; private set; }
        internal SourceWindow(int index)
        {
            this.index = index; DoubleBuffered = true;
            timer.Tick += (_, _) => { tick++; Invalidate(); }; timer.Start();
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000088; return value; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            Paints++; var g = e.Graphics; g.Clear(index switch { 0 => Color.MediumPurple, 1 => Color.Coral, _ => Color.Turquoise });
            using var brush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillEllipse(brush, 20 + tick % 130, 35, 60, 60);
            g.DrawString($"LIVE {index + 1} · {tick}", SystemFonts.MessageBoxFont!, Brushes.White, 22, 130);
            base.OnPaint(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect bounds);
}
