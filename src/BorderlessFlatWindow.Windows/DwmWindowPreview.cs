using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

/// <summary>Best-effort DWM preview. API availability never implies fresh pixels for an offscreen source.</summary>
public sealed class DwmWindowPreview : IDisposable
{
    private nint thumbnail;
    private (int Width, int Height)? sourceSize;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private DwmWindowPreview(nint thumbnail) => this.thumbnail = thumbnail;

    public static DwmWindowPreview? TryCreate(nint destination, nint source)
    {
        Interop.NativeMethods.GetWindowThreadProcessId(destination, out var pid);
        if (pid != Environment.ProcessId || Interop.NativeMethods.GetAncestor(destination, 2) != destination)
            throw new ArgumentException("DWM destination must be a top-level window owned by this process.");
        return DwmRegisterThumbnail(destination, source, out var handle) >= 0 && handle != 0 ? new(handle) : null;
    }

    public (int Width, int Height)? GetSourceSize()
    {
        EnsureOpen();
        return sourceSize = DwmQueryThumbnailSourceSize(thumbnail, out var size) >= 0 && size.Width > 0 && size.Height > 0
            ? (size.Width, size.Height) : null;
    }

    public bool Update(PixelRect destination, bool visible = true, RectD? sourceFraction = null, byte opacity = 255, bool useCachedSourceSize = false)
    {
        EnsureOpen();
        if (!destination.IsValid) return false;
        var properties = new ThumbnailProperties
        {
            Flags = 1 | 4 | 8 | 16,
            Opacity = opacity,
            Visible = visible,
            ClientOnly = false,
            Destination = new() { Left = destination.Left, Top = destination.Top, Right = destination.Right, Bottom = destination.Bottom }
        };
        // Ordinary callers may resize their sources. Overview animation opts into the size read by its last synchronization.
        if ((!useCachedSourceSize || sourceSize is null) && GetSourceSize() is null) return false;
        var size = sourceSize!.Value;
        var crop = sourceFraction ?? new RectD(PointD.Zero, new(1, 1));
        {
            if (!crop.Size.IsPositive || crop.Position.X < 0 || crop.Position.Y < 0 || crop.Right > 1.000001 || crop.Bottom > 1.000001
                ) return false;
            var l = Math.Clamp((int)Math.Round(crop.Position.X * size.Width), 0, size.Width - 1);
            var t = Math.Clamp((int)Math.Round(crop.Position.Y * size.Height), 0, size.Height - 1);
            properties.Flags |= 2;
            properties.Source = new() { Left = l, Top = t, Right = Math.Clamp((int)Math.Round(crop.Right * size.Width), l + 1, size.Width), Bottom = Math.Clamp((int)Math.Round(crop.Bottom * size.Height), t + 1, size.Height) };
        }
        return DwmUpdateThumbnailProperties(thumbnail, in properties) >= 0;
    }

    private void EnsureOpen()
    {
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Preview must stay on its owning thread.");
        ObjectDisposedException.ThrowIf(thumbnail == 0, this);
    }
    public void Dispose()
    {
        if (thumbnail == 0) return;
        EnsureOpen();
        DwmUnregisterThumbnail(thumbnail); // Source destruction can already have invalidated the resource.
        thumbnail = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width; internal int Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        internal uint Flags;
        internal Interop.NativeMethods.NativeRect Destination;
        internal Interop.NativeMethods.NativeRect Source;
        internal byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] internal bool Visible;
        [MarshalAs(UnmanagedType.Bool)] internal bool ClientOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, in ThumbnailProperties properties);
}
