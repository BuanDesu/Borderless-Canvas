using System.Security.Cryptography;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>No pixel freshness assertion: parked/partly offscreen sources are filtered by the caller.</summary>
public sealed class DwmOverviewPreviewPort(nint destination) : IOverviewPreviewPort
{
    public IOverviewPreview? Create(NativeWindowIdentity identity)
    {
        var resource = new Resource(identity);
        try
        {
            if (!resource.Matches || !resource.Mark() || !resource.IsCurrent) { resource.Dispose(); return null; }
            var thumbnail = DwmWindowPreview.TryCreate(destination, identity.Handle);
            if (thumbnail is null) { resource.Dispose(); return null; }
            resource.Thumbnail = thumbnail;
            if (resource.IsCurrent) return resource;
            resource.Dispose(); return null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException or ArgumentException)
        { resource.Dispose(); return null; }
        catch { resource.Dispose(); throw; }
    }
    private sealed class Resource(NativeWindowIdentity identity) : IOverviewPreview
    {
        private readonly string markerName = "BFW.Preview." + Guid.NewGuid().ToString("N");
        private readonly nint marker = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        private bool disposed, marked;
        internal DwmWindowPreview? Thumbnail;
        internal bool Matches
        {
            get
            {
                var current = new WindowsWindowObservationSource().ReadWindow(identity.Handle);
                return current.Identity == identity && current.IsVisible && !current.IsMinimized
                    && current.IsCloaked == false && current.IsOnCurrentDesktop == true;
            }
        }
        internal bool Mark() => marked = NativeMethods.SetPropW(identity.Handle, markerName, marker);
        public bool IsCurrent => !disposed && marked && NativeMethods.GetPropW(identity.Handle, markerName) == marker && Matches;
        public SizeD? SourceSize => Thumbnail?.GetSourceSize() is { } size ? new(size.Width, size.Height) : null;
        public bool Update(PixelRect bounds) => IsCurrent && Thumbnail?.Update(bounds) == true;
        public void Dispose()
        {
            if (disposed) return;
            Thumbnail?.Dispose(); Thumbnail = null;
            if (marked && NativeMethods.GetPropW(identity.Handle, markerName) == marker) NativeMethods.RemovePropW(identity.Handle, markerName);
            disposed = true;
        }
    }
}
