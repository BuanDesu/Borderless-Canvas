using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Discovery;

namespace BorderlessFlatWindow.Application.Ports;

/// <summary>Read-only discovery. Canvas identity must be separate from a reusable HWND.</summary>
public interface IDesktopWindowCatalog
{
    IReadOnlyList<DesktopWindowInfo> GetWindows();
    WindowCatalogSnapshot Inspect();
}

/// <summary>Incremental state; all operations run on the owning STA thread.</summary>
public interface ITrackedDesktopWindowCatalog : IDesktopWindowCatalog
{
    WindowCatalogSnapshot Current { get; }
    void RefreshWindow(nint handle);
    void ForgetWindow(nint handle);
}

/// <summary>Bounds are Win32 physical screen pixels, not canvas coordinates.</summary>
public sealed record DesktopWindowInfo(Guid Id, NativeWindowIdentity Identity, string Title, string ClassName, RectD Bounds)
{
    public nint Handle => Identity.Handle;
    public uint ProcessId => Identity.ProcessId;
}
