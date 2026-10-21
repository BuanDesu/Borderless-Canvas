using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>Owns an in-memory backdrop and optional focus crop until transferred. Never exports pixels.</summary>
internal sealed class OverviewBackdropCapture : IDisposable
{
    private Bitmap? backdrop, focus;
    internal Bitmap? Backdrop => backdrop;
    internal bool Captured => backdrop is not null;
    internal int FocusStatus { get; private set; } = -1;
    internal double RawMilliseconds { get; private set; }
    internal double EligibilityMilliseconds { get; private set; }
    internal double CloneMilliseconds { get; private set; }
    internal double RecheckMilliseconds { get; private set; }

    internal static OverviewBackdropCapture Capture(Rectangle bounds, Rectangle? focusBounds, Func<DesktopCropStatus>? eligibility,
        Func<Rectangle, Bitmap?>? capture = null)
    {
        var result = new OverviewBackdropCapture();
        try
        {
            if (focusBounds is { Width: > 0, Height: > 0 } crop && bounds.Contains(crop) && eligibility is not null)
            {
                var started = Stopwatch.GetTimestamp();
                result.FocusStatus = (int)eligibility();
                result.EligibilityMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            var rawStarted = Stopwatch.GetTimestamp();
            result.backdrop = (capture ?? FrostedBackdrop.CaptureRaw)(bounds);
            result.RawMilliseconds = Stopwatch.GetElapsedTime(rawStarted).TotalMilliseconds;
            if (result.backdrop is null || result.FocusStatus != (int)DesktopCropStatus.Ready) return result;
            try
            {
                var area = focusBounds!.Value;
                var started = Stopwatch.GetTimestamp();
                result.focus = result.backdrop.Clone(new Rectangle(area.Left - bounds.Left, area.Top - bounds.Top, area.Width, area.Height), PixelFormat.Format32bppPArgb);
                result.CloneMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            catch (Exception error) when (error is ArgumentException or OutOfMemoryException or ExternalException) { return result; }
            var recheck = Stopwatch.GetTimestamp();
            result.FocusStatus = (int)eligibility!();
            result.RecheckMilliseconds = Stopwatch.GetElapsedTime(recheck).TotalMilliseconds;
            if (result.FocusStatus != (int)DesktopCropStatus.Ready) { result.focus.Dispose(); result.focus = null; }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    internal Bitmap? TakeFocus() { var image = focus; focus = null; return image; }
    public void Dispose() { focus?.Dispose(); backdrop?.Dispose(); focus = backdrop = null; }
}
