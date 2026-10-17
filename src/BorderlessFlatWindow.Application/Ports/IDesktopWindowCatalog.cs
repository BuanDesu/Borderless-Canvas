using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Ports;

/// <summary>Read-only discovery. Canvas identity must be separate from a reusable HWND.</summary>
public interface IDesktopWindowCatalog
{
    IReadOnlyList<DesktopWindowInfo> GetWindows();
}

/// <summary>Bounds are Win32 physical screen pixels, not canvas coordinates.</summary>
public sealed record DesktopWindowInfo(nint Handle, uint ProcessId, string Title, string ClassName, RectD Bounds);
