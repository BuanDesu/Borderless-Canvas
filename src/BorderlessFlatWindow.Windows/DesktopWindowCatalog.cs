using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>Basic read-only catalog; these results are candidates, not an admission policy.</summary>
public sealed class DesktopWindowCatalog : IDesktopWindowCatalog
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
    };

    public IReadOnlyList<DesktopWindowInfo> GetWindows()
    {
        var result = new List<DesktopWindowInfo>();
        var succeeded = NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == (uint)Environment.ProcessId)
                return true;
            var title = new StringBuilder(1024);
            var className = new StringBuilder(256);
            NativeMethods.GetWindowText(handle, title, title.Capacity);
            NativeMethods.GetClassName(handle, className, className.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString()) || ExcludedClasses.Contains(className.ToString()))
                return true;
            if (!NativeMethods.GetWindowRect(handle, out var rect))
                return true;
            var width = (double)rect.Right - rect.Left;
            var height = (double)rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
                result.Add(new(handle, processId, title.ToString(), className.ToString(),
                    new(new PointD(rect.Left, rect.Top), new SizeD(width, height))));
            return true;
        }, 0);
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Window enumeration failed.");
        return result;
    }
}
