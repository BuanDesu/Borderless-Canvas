using System.Security.Cryptography;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>DWM supplies every available source, including parked windows. Offscreen content can stop repainting.</summary>
public sealed class DwmOverviewPreviewPort(nint destination) : IOverviewPreviewPort
{
    private Batch? batch;
    public IDisposable BeginBatch() => new Batch(this);
    public IOverviewPreview? Create(NativeWindowIdentity identity)
    {
        using var localBatch = batch is null ? BeginBatch() : null;
        var resource = new Resource(identity);
        try
        {
            // Reuse the COM query service, but run both complete live validations for
            // every relation. No desktop membership or identity result is cached.
            var desktops = batch!.Desktops;
            if (!resource.Matches(desktops) || !resource.Mark() || !resource.IsCurrent || !resource.Matches(desktops)) { resource.Dispose(); return null; }
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
    private sealed class Batch : IDisposable
    {
        private readonly DwmOverviewPreviewPort owner;
        private readonly Batch? previous;
        private readonly int thread = Environment.CurrentManagedThreadId;
        private VirtualDesktopQuery? desktops;
        private bool disposed;
        // Laziness keeps idle, disabled and already-registered synchronization free
        // of COM activation. The scope never crosses an await or message loop.
        internal VirtualDesktopQuery Desktops => previous?.Desktops ?? (desktops ??= new());
        internal Batch(DwmOverviewPreviewPort owner)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Preview registration requires an STA thread.");
            this.owner = owner; previous = owner.batch; owner.batch = this;
        }
        public void Dispose()
        {
            if (disposed) return;
            if (thread != Environment.CurrentManagedThreadId || !ReferenceEquals(owner.batch, this))
                throw new InvalidOperationException("Preview batches must close in order on their original thread.");
            disposed = true; owner.batch = previous; desktops?.Dispose(); desktops = null;
        }
    }
    private sealed class Resource(NativeWindowIdentity identity) : IOverviewPreview
    {
        private readonly string markerName = "BFW.Preview." + Guid.NewGuid().ToString("N");
        private readonly nint marker = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        private bool disposed, marked;
        internal DwmWindowPreview? Thumbnail;
        internal bool Matches(VirtualDesktopQuery desktops)
        {
            var current = new WindowsWindowObservationSource().ReadWindow(identity.Handle, desktops);
            return current.Identity == identity && current.IsVisible && !current.IsMinimized
                && current.IsCloaked == false && current.IsOnCurrentDesktop == true;
        }
        internal bool Mark() => marked = NativeMethods.SetPropW(identity.Handle, markerName, marker);
        public bool IsCurrent
        {
            get
            {
                if (disposed || !marked || NativeMethods.GetPropW(identity.Handle, markerName) != marker) return false;
                NativeMethods.GetWindowThreadProcessId(identity.Handle, out var pid);
                return pid == identity.ProcessId;
            }
        }
        public SizeD? SourceSize => Thumbnail?.GetSourceSize() is { } size ? new(size.Width, size.Height) : null;
        public bool Update(PixelRect bounds) => IsCurrent && Thumbnail?.Update(bounds, useCachedSourceSize: true) == true;
        public bool Update(PixelRect bounds, RectD? sourceFraction, byte opacity) => IsCurrent && Thumbnail?.Update(bounds, true, sourceFraction, opacity, useCachedSourceSize: true) == true;
        public void Dispose()
        {
            if (disposed) return;
            Thumbnail?.Dispose(); Thumbnail = null;
            if (marked && NativeMethods.GetPropW(identity.Handle, markerName) == marker) NativeMethods.RemovePropW(identity.Handle, markerName);
            disposed = true;
        }
    }
}
