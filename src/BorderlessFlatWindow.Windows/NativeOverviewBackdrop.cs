using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows;

/// <summary>Documented Windows 11 desktop acrylic. Success describes API acceptance, not visible translucency.</summary>
public static class NativeOverviewBackdrop
{
    public readonly record struct Result(int FrameHResult, int SetHResult, int GetHResult, int ReportedType,
        int NonClientSetHResult, int NonClientReadHResult, bool? NonClientEnabled)
    {
        public bool Accepted => FrameHResult >= 0 && SetHResult >= 0 && GetHResult >= 0 && ReportedType == 3
            && NonClientSetHResult >= 0 && NonClientReadHResult >= 0 && NonClientEnabled == true;
        public static Result Unavailable => new(unchecked((int)0x80004001), unchecked((int)0x80004001), unchecked((int)0x80004001), 0,
            unchecked((int)0x80004001), unchecked((int)0x80004001), null);
    }

    public static Result TryApply(nint destination)
    {
        Interop.NativeMethods.GetWindowThreadProcessId(destination, out var pid);
        if (pid != Environment.ProcessId || Interop.NativeMethods.GetAncestor(destination, 2) != destination)
            throw new ArgumentException("Backdrop destination must be a top-level window owned by this process.", nameof(destination));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return Result.Unavailable;
        // A borderless WPF window defaults to disabled non-client rendering. The
        // backdrop attribute alone can succeed while the whole surface stays solid.
        var policy = 2; // DWMNCRP_ENABLED; do not inherit the caption-less style's policy.
        var nonClientSet = DwmSetWindowAttribute(destination, 2, in policy, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var frame = DwmExtendFrameIntoClientArea(destination, in margins);
        var requested = 3; // DWMSBT_TRANSIENTWINDOW, system desktop acrylic.
        var set = DwmSetWindowAttribute(destination, 38, in requested, sizeof(int));
        var get = DwmGetWindowAttribute(destination, 38, out var actual, sizeof(int));
        var nonClientRead = DwmGetWindowAttribute(destination, 1, out var nonClientEnabled, sizeof(int));
        return new(frame, set, get, actual, nonClientSet, nonClientRead, nonClientRead >= 0 ? nonClientEnabled != 0 : null);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, in Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
