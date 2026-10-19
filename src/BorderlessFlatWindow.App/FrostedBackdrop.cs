using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

/// <summary>A short-lived blurred desktop snapshot. Never written to disk or included in diagnostics.</summary>
internal static class FrostedBackdrop
{
    internal static Bitmap? Capture(Rectangle bounds)
    {
        try
        {
            using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            // Downsample before a separable box blur; cost depends on overview size, not desktop pixel count.
            using var small = new Bitmap(Math.Max(1, bounds.Width / 8), Math.Max(1, bounds.Height / 8), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, new Rectangle(Point.Empty, small.Size)); }
            Blur(small, 2);
            var result = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(small, new Rectangle(Point.Empty, result.Size));
                using var tint = new SolidBrush(Color.FromArgb(28, ChromeTheme.Ink)); g.FillRectangle(tint, 0, 0, result.Width, result.Height);
            }
            return result;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
    private static void Blur(Bitmap bitmap, int radius)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            var temp = new byte[pixels.Length];
            for (var pass = 0; pass < 3; pass++)
            {
                for (var y = 0; y < data.Height; y++) for (var x = 0; x < data.Width; x++) for (var c = 0; c < 4; c++)
                {
                    var total = 0;
                    for (var k = -radius; k <= radius; k++) total += pixels[y * data.Stride + Math.Clamp(x + k, 0, data.Width - 1) * 4 + c];
                    temp[y * data.Stride + x * 4 + c] = (byte)(total / (radius * 2 + 1));
                }
                for (var y = 0; y < data.Height; y++) for (var x = 0; x < data.Width; x++) for (var c = 0; c < 4; c++)
                {
                    var total = 0;
                    for (var k = -radius; k <= radius; k++) total += temp[Math.Clamp(y + k, 0, data.Height - 1) * data.Stride + x * 4 + c];
                    pixels[y * data.Stride + x * 4 + c] = (byte)(total / (radius * 2 + 1));
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
