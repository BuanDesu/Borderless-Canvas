using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows.Interop;

internal sealed class VirtualDesktopQuery : IDisposable
{
    private object? instance;
    private IVirtualDesktopManager? manager;

    internal VirtualDesktopQuery()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!;
            instance = Activator.CreateInstance(type);
            manager = instance as IVirtualDesktopManager;
        }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal bool? IsOnCurrentDesktop(nint handle)
    {
        if (manager is null) return null;
        try
        {
            return manager.IsWindowOnCurrentVirtualDesktop(handle, out var current) >= 0 ? current : null;
        }
        catch (COMException) { return null; }
    }

    public void Dispose()
    {
        manager = null;
        if (instance is not null && Marshal.IsComObject(instance))
            Marshal.FinalReleaseComObject(instance);
        instance = null;
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig]
        int GetWindowDesktopId(nint window, out Guid desktopId);
        [PreserveSig]
        int MoveWindowToDesktop(nint window, in Guid desktopId);
    }
}
