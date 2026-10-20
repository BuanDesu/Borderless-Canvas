using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

public enum DesktopCropStatus { Ready, SourceChanged, LayeredSource, Occluded, UnknownBounds, EnumerationLimit, SourceNotFound }

/// <summary>Conservative eligibility for reusing a desktop crop as a short-lived window image.</summary>
public static class DesktopCropEligibility
{
    public static bool IsUnobscured(NativeWindowIdentity identity, PixelRect expected)
        => IsUnobscuredRegion(identity, expected, expected);
    public static bool IsUnobscuredRegion(NativeWindowIdentity identity, PixelRect expected, PixelRect region)
        => Evaluate(identity, expected, region) == DesktopCropStatus.Ready;
    public static DesktopCropStatus Evaluate(NativeWindowIdentity identity, PixelRect expected, PixelRect region)
    {
        using var pixels = new PhysicalPixelScope();
        bool Matches()
        {
            NativeMethods.GetWindowThreadProcessId(identity.Handle, out var pid);
            return identity.IsValid && pid == identity.ProcessId && NativeMethods.IsWindowVisible(identity.Handle)
                && !NativeMethods.IsIconic(identity.Handle) && WindowsWindowObservationSource.ReadCreationTime(pid) == identity.ProcessCreationFileTime
                && NativeMethods.DwmGetWindowAttribute(identity.Handle, 14, out var cloak, sizeof(int)) >= 0 && cloak == 0
                && NativeMethods.GetWindowRect(identity.Handle, out var r) && r.Left == expected.Left && r.Top == expected.Top
                && r.Right == expected.Right && r.Bottom == expected.Bottom;
        }
        if (!expected.IsValid || !expected.Contains(region) || !Matches()) return DesktopCropStatus.SourceChanged;
        if ((NativeMethods.GetWindowStyle(identity.Handle, -20).ToInt64() & 0x80000) != 0) return DesktopCropStatus.LayeredSource;
        var found = false; var status = DesktopCropStatus.Ready; var count = 0;
        // EnumWindows is bounded and does not read window titles or send messages to applications.
        NativeMethods.EnumWindows((window, _) =>
        {
            if (++count > 8192) { status = DesktopCropStatus.EnumerationLimit; return false; }
            if (window == identity.Handle) { found = true; return false; }
            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return true;
            if (!NativeMethods.GetWindowRect(window, out var r)) { status = DesktopCropStatus.UnknownBounds; return true; }
            if (r.Right > region.Left && r.Left < region.Right && r.Bottom > region.Top && r.Top < region.Bottom)
                if (NativeMethods.DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) < 0 || cloaked == 0) status = DesktopCropStatus.Occluded;
            return true;
        }, 0);
        return !found ? DesktopCropStatus.SourceNotFound : !Matches() ? DesktopCropStatus.SourceChanged : status;
    }
}
