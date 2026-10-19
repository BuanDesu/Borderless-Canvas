using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed record DesktopWallpaperInfo(string ImagePath, WallpaperPlacement Placement, Rgb Background, RectD Monitor, RectD Desktop);

/// <summary>Read-only Windows wallpaper metadata. Paths are transient and must never be logged.</summary>
public static class DesktopWallpaperSource
{
    public static DesktopWallpaperInfo? Read(PixelRect target)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Wallpaper inspection requires STA.");
        IDesktopWallpaper? source = null;
        try
        {
            source = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!)!;
            if (source.GetMonitorDevicePathCount(out var count) < 0 || count is 0 or > 64) return null;
            var monitors = new List<(string Id, RectD Bounds)>();
            for (uint i = 0; i < count; i++)
                if (source.GetMonitorDevicePathAt(i, out var id) >= 0 && source.GetMonitorRECT(id, out var bounds) == 0 && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                    monitors.Add((id, new(new(bounds.Left, bounds.Top), new(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))));
            if (monitors.Count == 0) return null;
            var chosen = monitors.FirstOrDefault(m => m.Bounds.Position == target.Position && m.Bounds.Size == target.Size);
            if (chosen.Id is null) chosen = monitors[0];
            var left = monitors.Min(m => m.Bounds.Position.X); var top = monitors.Min(m => m.Bounds.Position.Y);
            var desktop = new RectD(new(left, top), new(monitors.Max(m => m.Bounds.Right) - left, monitors.Max(m => m.Bounds.Bottom) - top));
            source.GetWallpaper(chosen.Id, out var path);
            var placement = source.GetPosition(out var position) >= 0 && Enum.IsDefined(position) ? position : WallpaperPlacement.Fill;
            source.GetBackgroundColor(out var color);
            return new(path ?? "", Enum.IsDefined(placement) ? placement : WallpaperPlacement.Fill,
                new((byte)(color & 255), (byte)(color >> 8 & 255), (byte)(color >> 16 & 255)), chosen.Bounds, desktop);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or TypeLoadException or UnauthorizedAccessException) { return null; }
        finally { if (source is not null) Marshal.FinalReleaseComObject(source); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    // ABI order follows the Windows SDK. Only the read methods are called.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitor);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out Rect rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(WallpaperPlacement position);
        [PreserveSig] int GetPosition(out WallpaperPlacement position);
    }
}
