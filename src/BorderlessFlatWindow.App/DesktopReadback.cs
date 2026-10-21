using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

/// <summary>One synchronous GDI readback, copied into an independent opaque GDI+ bitmap.</summary>
internal static class DesktopReadback
{
    internal static Bitmap? Capture(Rectangle bounds)
    {
        var source = GetDC(0);
        if (source == 0) return null;
        try { return FromDeviceContext(source, bounds); }
        finally { ReleaseDC(0, source); }
    }
    internal static Bitmap? FromDeviceContext(nint source, Rectangle bounds)
    {
        if (source == 0 || bounds.Width <= 0 || bounds.Height <= 0) return null;
        nint memory = 0, bitmap = 0, previous = 0;
        try
        {
            var stride = checked(bounds.Width * 4); _ = checked(stride * bounds.Height);
            memory = CreateCompatibleDC(source); if (memory == 0) return null;
            var info = new BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(memory, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) return null;
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1) { previous = 0; return null; }
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, source, bounds.Left, bounds.Top, 0x00CC0020) || !GdiFlush()) return null;
            // BI_RGB's high byte is not alpha. Converting RGB to premultiplied ARGB
            // both normalizes alpha to 255 and detaches pixels before deleting DIB.
            using var view = new Bitmap(bounds.Width, bounds.Height, stride, PixelFormat.Format32bppRgb, pixels);
            return view.Clone(new Rectangle(Point.Empty, bounds.Size), PixelFormat.Format32bppPArgb);
        }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException or ExternalException or OverflowException) { return null; }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ImportantColors, Color;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}
