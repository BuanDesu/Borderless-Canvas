using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

public sealed class DesktopDisplayCatalog : IDesktopDisplayCatalog
{
    public DisplayLayout ReadDisplays()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Display inspection requires an STA thread.");
        using var pixels = new PhysicalPixelScope();
        var handles = ReadHandles();
        var monitors = new List<DisplayMonitor>();
        foreach (var handle in handles)
        {
            var info = ReadInfo(handle);
            monitors.Add(new(info.DeviceName, (info.Flags & 1) != 0, ToPixels(info.Bounds),
                ToPixels(info.WorkArea), ReadDpi(handle, info.Bounds)));
        }
        // Reject an observed topology/work-area change instead of publishing mixed geometry.
        if (!handles.Order().SequenceEqual(ReadHandles().Order()))
            throw new InvalidOperationException("Display topology changed during inspection; refresh again.");
        for (var i = 0; i < handles.Count; i++)
        {
            var current = ReadInfo(handles[i]);
            var prior = monitors[i];
            if (current.DeviceName != prior.DeviceName || ToPixels(current.Bounds) != prior.Bounds
                || ToPixels(current.WorkArea) != prior.WorkArea || ((current.Flags & 1) != 0) != prior.IsPrimary)
                throw new InvalidOperationException("Display layout changed during inspection; refresh again.");
        }
        return new(monitors);
    }

    private static List<nint> ReadHandles()
    {
        var handles = new List<nint>();
        if (!NativeMethods.EnumDisplayMonitors(0, 0, (monitor, _, _, _) => { handles.Add(monitor); return true; }, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Monitor enumeration failed.");
        return handles;
    }

    private static NativeMethods.NativeMonitorInfo ReadInfo(nint monitor)
    {
        var info = new NativeMethods.NativeMonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.NativeMonitorInfo>(), DeviceName = "" };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Monitor information unavailable.");
        return info;
    }

    private static uint? ReadDpi(nint monitor, NativeMethods.NativeRect bounds)
    {
        // An unshown, non-activating popup on the target monitor gives GetDpiForWindow a PMv2 HWND.
        // No WS_VISIBLE, no ShowWindow, and no changes to user windows or desktop settings.
        var probe = NativeMethods.CreateWindowExW(0x08000080, "STATIC", "", 0x80000000,
            bounds.Left, bounds.Top, 1, 1, 0, 0, 0, 0); // NOACTIVATE | TOOLWINDOW; POPUP
        if (probe == 0) return null;
        try
        {
            if (NativeMethods.MonitorFromWindow(probe, 0) != monitor) return null;
            var dpi = NativeMethods.GetDpiForWindow(probe);
            return dpi == 0 ? null : dpi;
        }
        finally
        {
            if (!NativeMethods.DestroyWindow(probe))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot release DPI probe window.");
        }
    }

    private static PixelRect ToPixels(NativeMethods.NativeRect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
