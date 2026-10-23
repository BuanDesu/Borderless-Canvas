using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows.Checks;

/// <summary>Places only this test process's windows; never switches/creates desktops or moves a user window.</summary>
internal static class FixtureDesktop
{
    private static nint anchor;
    internal static void SetAnchor(nint window) => anchor = window;
    internal static void PlaceOwnWindow(nint window)
    {
        if (anchor == 0) return;
        GetWindowThreadProcessId(window, out var process);
        GetWindowThreadProcessId(anchor, out var anchorProcess);
        if (process != (uint)Environment.ProcessId || anchorProcess != process) throw new InvalidOperationException("Fixture desktop placement requires own windows.");
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!)!;
        try
        {
            var desktops = (IDesktops)instance;
            Marshal.ThrowExceptionForHR(desktops.IsWindowOnCurrentVirtualDesktop(window, out var current));
            if (current) return;
            Marshal.ThrowExceptionForHR(desktops.IsWindowOnCurrentVirtualDesktop(anchor, out var anchorCurrent));
            if (!anchorCurrent) throw new InvalidOperationException("The verified fixture desktop is no longer current; no desktop switch was attempted.");
            Marshal.ThrowExceptionForHR(desktops.GetWindowDesktopId(anchor, out var desktop));
            if (desktop == Guid.Empty) throw new InvalidOperationException("Fixture desktop identity is unavailable.");
            var foreground = WindowEventListener.ForegroundWindow;
            Marshal.ThrowExceptionForHR(desktops.MoveWindowToDesktop(window, in desktop));
            if (WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Own fixture desktop placement changed foreground.");
        }
        finally { if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktops
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }
}
