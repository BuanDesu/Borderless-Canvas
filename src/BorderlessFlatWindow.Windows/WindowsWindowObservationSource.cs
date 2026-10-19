using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

internal sealed class WindowsWindowObservationSource : IWindowObservationSource
{
    public IReadOnlyList<WindowObservation> ReadWindows()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Window inspection requires an STA thread.");
        using var pixels = new PhysicalPixelScope();
        // Keep native callbacks short; do all probing outside the unmanaged callback.
        var handles = new List<nint>();
        var succeeded = NativeMethods.EnumWindows((handle, _) => { handles.Add(handle); return true; }, 0);
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Window enumeration failed.");
        using var desktops = new VirtualDesktopQuery();
        return handles.Select(handle => ReadWindow(handle, desktops)).ToArray();
    }

    private static WindowObservation ReadWindow(nint handle, VirtualDesktopQuery desktops)
    {
        if (!NativeMethods.IsWindow(handle) || NativeMethods.GetAncestor(handle, 2) != handle)
            return new() { Handle = handle }; // Only top-level windows; child events cannot grow the catalog.
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        var created = ReadCreationTime(processId);
        var title = new StringBuilder(4096);
        var className = new StringBuilder(256);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        var hasClass = NativeMethods.GetClassName(handle, className, className.Capacity) > 0;
        Marshal.SetLastPInvokeError(0);
        var style = NativeMethods.GetWindowStyle(handle, -16).ToInt64();
        var hasStyle = style != 0 || Marshal.GetLastPInvokeError() == 0;
        Marshal.SetLastPInvokeError(0);
        var extendedStyle = NativeMethods.GetWindowStyle(handle, -20).ToInt64();
        var hasExtendedStyle = extendedStyle != 0 || Marshal.GetLastPInvokeError() == 0;
        RectD? bounds = null;
        var hasBounds = NativeMethods.GetWindowRect(handle, out var rectangle);
        if (hasBounds && rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top)
            bounds = new(new PointD(rectangle.Left, rectangle.Top),
                new SizeD((double)rectangle.Right - rectangle.Left, (double)rectangle.Bottom - rectangle.Top));
        var owner = NativeMethods.GetWindow(handle, 4); // GW_OWNER
        var visible = NativeMethods.IsWindowVisible(handle);
        var minimized = NativeMethods.IsIconic(handle);
        bool? cloaked = NativeMethods.DwmGetWindowAttribute(handle, 14, out var cloak, sizeof(int)) >= 0
            ? cloak != 0 : null; // DWMWA_CLOAKED
        var currentDesktop = visible ? desktops.IsOnCurrentDesktop(handle) : null;

        // Detect close/PID reuse during probing. Revalidate again before any future movement.
        NativeMethods.GetWindowThreadProcessId(handle, out var finalProcessId);
        var stillSame = NativeMethods.IsWindow(handle) && processId != 0 && processId == finalProcessId
            && created == ReadCreationTime(finalProcessId);
        return new()
        {
            Handle = handle, ProcessId = processId, ProcessCreationFileTime = stillSame ? created : null,
            Title = title.ToString(), ClassName = className.ToString(), Bounds = bounds, OwnerHandle = owner,
            IsAlive = stillSame, MetadataAvailable = hasClass && hasStyle && hasExtendedStyle && hasBounds,
            IsVisible = visible, IsChildWindow = (style & 0x40000000L) != 0,
            IsToolWindow = (extendedStyle & 0x80) != 0, IsMinimized = minimized,
            IsMaximized = NativeMethods.IsZoomed(handle), IsTopMost = (extendedStyle & 8) != 0,
            IsCloaked = cloaked, IsOnCurrentDesktop = currentDesktop
        };
    }

    internal WindowObservation ReadWindow(nint handle)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Window inspection requires an STA thread.");
        using var pixels = new PhysicalPixelScope();
        using var desktops = new VirtualDesktopQuery();
        return ReadWindow(handle, desktops);
    }

    internal static long? ReadCreationTime(uint processId)
    {
        if (processId == 0) return null;
        using var process = NativeMethods.OpenProcess(0x1000, false, processId);
        return !process.IsInvalid && NativeMethods.GetProcessTimes(process, out var created, out _, out _, out _)
            ? created.Value : null;
    }
}
