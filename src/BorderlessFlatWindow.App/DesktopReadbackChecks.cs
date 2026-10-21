using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BorderlessFlatWindow.App;

/// <summary>Native readback parity/resource measurements against synthetic memory DCs only.</summary>
internal static class DesktopReadbackChecks
{
    internal static void Run(string output)
    {
        if (DesktopReadback.FromDeviceContext(0, new(0, 0, 10, 10)) is not null)
            throw new InvalidOperationException("Invalid readback DC was accepted.");
        var results = new List<object>(); using var process = Process.GetCurrentProcess();
        foreach (var size in new[] { new Size(1920, 1080), new Size(3440, 1440), new Size(960, 720) })
        {
            using var source = new Bitmap(size.Width + 16, size.Height + 16, PixelFormat.Format24bppRgb);
            using (var paint = Graphics.FromImage(source))
            {
                paint.Clear(Color.Purple); using var brush = new SolidBrush(Color.Coral);
                paint.FillRectangle(brush, 0, size.Height / 2, source.Width, source.Height);
            }
            // A GDI+ Graphics.GetHdc bitmap DC is a write bridge, not a readable
            // copy of its existing pixels. Select an actual synthetic HBITMAP.
            var sourceBitmap = source.GetHbitmap(); var sourceDc = CreateCompatibleDC(0); nint previous = 0;
            try
            {
                if (sourceDc == 0 || (previous = SelectObject(sourceDc, sourceBitmap)) is 0 or -1)
                    throw new InvalidOperationException("Synthetic source DC creation failed.");
                if (DesktopReadback.FromDeviceContext(sourceDc, new(0, 0, int.MaxValue, 1)) is not null
                    || DesktopReadback.FromDeviceContext(sourceDc, new(0, 0, 0, 10)) is not null)
                    throw new InvalidOperationException("Invalid readback dimensions were accepted.");
                var bounds = new Rectangle(8, 8, size.Width, size.Height);
                for (var warm = 0; warm < 2; warm++) { using var a = DesktopReadback.FromDeviceContext(sourceDc, bounds); using var b = GraphicsCopy(sourceDc, bounds); }
                var before = GetGuiResources(process.Handle, 0);
                for (var sample = 0; sample < 5; sample++)
                foreach (var direct in sample % 2 == 0 ? new[] { false, true } : new[] { true, false })
                {
                    var started = Stopwatch.GetTimestamp();
                    using var image = direct ? DesktopReadback.FromDeviceContext(sourceDc, bounds) : GraphicsCopy(sourceDc, bounds);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (image is null || image.Size != size || image.PixelFormat != PixelFormat.Format32bppPArgb
                        || image.GetPixel(0, 0).ToArgb() != Color.Purple.ToArgb()
                        || image.GetPixel(size.Width - 1, size.Height - 1).ToArgb() != Color.Coral.ToArgb())
                        throw new InvalidOperationException($"Readback dimensions, orientation, color or alpha failed: direct={direct}, format={image?.PixelFormat}, top={image?.GetPixel(0, 0).ToArgb():X8}, bottom={image?.GetPixel(size.Width - 1, size.Height - 1).ToArgb():X8}.");
                    results.Add(new { size.Width, size.Height, Direct = direct, Sample = sample, Milliseconds = elapsed });
                }
                if (GetGuiResources(process.Handle, 0) != before) throw new InvalidOperationException("Readback retained GDI resources.");
            }
            finally
            {
                if (previous != 0 && previous != -1) SelectObject(sourceDc, previous);
                if (sourceDc != 0) DeleteDC(sourceDc);
                DeleteObject(sourceBitmap);
            }
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "readback-memory.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS Synthetic native readback preserves offset, dimensions, orientation, opaque colors and GDI resource counts");
    }
    private static Bitmap GraphicsCopy(nint source, Rectangle area)
    {
        var result = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (var graphics = Graphics.FromImage(result))
            {
                var target = graphics.GetHdc();
                try { if (!BitBlt(target, 0, 0, area.Width, area.Height, source, area.Left, area.Top, 0x00CC0020)) throw new InvalidOperationException("Synthetic baseline BitBlt failed."); }
                finally { graphics.ReleaseHdc(target); }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint kind);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
}
