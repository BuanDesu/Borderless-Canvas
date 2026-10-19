using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

/// <summary>Best-effort DWM preview. API availability never implies fresh pixels for an offscreen source.</summary>
public sealed class DwmWindowPreview : IDisposable
{
    private nint thumbnail;
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
        return DwmQueryThumbnailSourceSize(thumbnail, out var size) >= 0 && size.Width > 0 && size.Height > 0
            ? (size.Width, size.Height) : null;
    }

    public bool Update(PixelRect destination, bool visible = true)
    {
        EnsureOpen();
        if (!destination.IsValid) return false;
        var properties = new ThumbnailProperties
        {
            Flags = 1 | 4 | 8 | 16, Opacity = 255, Visible = visible, ClientOnly = false,
            Destination = new() { Left = destination.Left, Top = destination.Top, Right = destination.Right, Bottom = destination.Bottom }
        };
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
    [StructLayout(LayoutKind.Sequential)] private struct ThumbnailProperties
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
