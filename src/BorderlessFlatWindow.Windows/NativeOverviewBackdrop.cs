using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows;

/// <summary>Documented Windows 11 desktop acrylic. Success describes API acceptance, not visible translucency.</summary>
public static class NativeOverviewBackdrop
{
    public readonly record struct Result(int FrameHResult, int SetHResult, int GetHResult, int ReportedType)
    {
        public bool Accepted => FrameHResult >= 0 && SetHResult >= 0 && GetHResult >= 0 && ReportedType == 3;
        public static Result Unavailable => new(unchecked((int)0x80004001), unchecked((int)0x80004001), unchecked((int)0x80004001), 0);
    }

    public static Result TryApply(nint destination)
    {
        Interop.NativeMethods.GetWindowThreadProcessId(destination, out var pid);
        if (pid != Environment.ProcessId || Interop.NativeMethods.GetAncestor(destination, 2) != destination)
            throw new ArgumentException("Backdrop destination must be a top-level window owned by this process.", nameof(destination));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return Result.Unavailable;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var frame = DwmExtendFrameIntoClientArea(destination, in margins);
        var requested = 3; // DWMSBT_TRANSIENTWINDOW, system desktop acrylic.
        var set = DwmSetWindowAttribute(destination, 38, in requested, sizeof(int));
        var get = DwmGetWindowAttribute(destination, 38, out var actual, sizeof(int));
        return new(frame, set, get, actual);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, in Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
