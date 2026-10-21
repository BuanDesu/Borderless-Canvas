using System.Drawing.Imaging;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>Capture ordering/resource checks use synthetic bitmap delegates, never the desktop.</summary>
internal static class OverviewBackdropChecks
{
    internal static void Run()
    {
        var bounds = new Rectangle(-100, 50, 120, 90); var area = new Rectangle(-80, 65, 20, 30);
        var order = new List<string>(); var checks = 0;
        using (var frame = OverviewBackdropCapture.Capture(bounds, area,
            () => { order.Add(++checks == 1 ? "before" : "after"); return DesktopCropStatus.Ready; },
            _ => { order.Add("pixels"); return Pixels(); }))
        {
            Require(order.SequenceEqual(["before", "pixels", "after"]), "Focus checks did not bracket capture.");
            using var focus = frame.TakeFocus();
            Require(focus?.Size == area.Size && focus.GetPixel(0, 0).ToArgb() == Color.Purple.ToArgb() && frame.TakeFocus() is null, "Crop or ownership transfer failed.");
        }

        // Regression: an occluder removed immediately after the capture must not
        // make its already captured pixels eligible as the source window image.
        var status = DesktopCropStatus.Occluded; checks = 0;
        using (var frame = OverviewBackdropCapture.Capture(bounds, area,
            () => { checks++; return status; }, _ => { status = DesktopCropStatus.Ready; return Pixels(); }))
            Require(frame.Captured && frame.TakeFocus() is null && checks == 1 && frame.FocusStatus == (int)DesktopCropStatus.Occluded, "Previously obscured pixels were accepted after removal.");

        status = DesktopCropStatus.Ready;
        using (var frame = OverviewBackdropCapture.Capture(bounds, area, () => status,
            _ => { status = DesktopCropStatus.SourceChanged; return Pixels(); }))
            Require(frame.Captured && frame.TakeFocus() is null && frame.FocusStatus == (int)DesktopCropStatus.SourceChanged, "Changed source crop was retained.");

        using (var frame = OverviewBackdropCapture.Capture(bounds, area, () => DesktopCropStatus.Ready, _ => null))
            Require(!frame.Captured && frame.TakeFocus() is null, "Failed capture retained a focus image.");

        checks = 0;
        using (var frame = OverviewBackdropCapture.Capture(bounds, new(500, 500, 20, 20),
            () => { checks++; return DesktopCropStatus.Ready; }, _ => Pixels()))
            Require(frame.Captured && frame.TakeFocus() is null && checks == 0 && frame.FocusStatus == -1, "Out-of-bounds candidate probed or cropped.");

        Bitmap? owned = null; checks = 0; var failed = false;
        try
        {
            using var frame = OverviewBackdropCapture.Capture(bounds, area,
                () => ++checks == 1 ? DesktopCropStatus.Ready : throw new InvalidOperationException("Injected recheck failure"), _ => owned = Pixels());
        }
        catch (InvalidOperationException) { failed = true; }
        Require(failed && owned is not null, "Recheck failure was not exercised.");
        try { _ = owned!.GetPixel(0, 0); throw new InvalidOperationException("Failed capture leaked its bitmap."); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS Synthetic focus capture brackets pixels, rejects changed/obscured sources, and releases failed resources");
    }
    private static Bitmap Pixels()
    {
        var bitmap = new Bitmap(120, 90, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Purple); return bitmap;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
