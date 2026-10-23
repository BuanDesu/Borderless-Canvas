using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace BorderlessFlatWindow.Windows;

public sealed record ExecutableShortcut(string Shortcut, string Executable)
{
    public static ExecutableShortcut Read(string path)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("快捷方式读取需要 STA 线程。");
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(full) || new FileInfo(full).Length > 1024 * 1024)
            throw new InvalidDataException("请选择有效的软件 .lnk 快捷方式。");
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
        try
        {
            ((IPersistFile)instance).Load(full, 0);
            var target = new StringBuilder(32768);
            Marshal.ThrowExceptionForHR(((IShellLink)instance).GetPath(target, target.Capacity, 0, 4));
            var executable = Environment.ExpandEnvironmentVariables(target.ToString());
            if (!Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
                throw new InvalidOperationException("此快捷方式使用商店或系统代理，无法可靠识别新窗口；请正常打开后用 Ctrl+Alt+P 固定。");
            return new(full, Path.GetFullPath(executable));
        }
        finally { if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    public void Launch()
    {
        // Let Windows preserve shortcut arguments, working directory and launch/elevation behavior.
        using var process = Process.Start(new ProcessStartInfo(Shortcut) { UseShellExecute = true });
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    { [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, nint findData, uint flags); }
}

public static class ShortcutPinVerb
{
    private const string Key = @"Software\Classes\lnkfile\shell\BorderlessFlatWindow.Sticky";
    private const string Owner = "BorderlessFlatWindow.Sticky.v1";
    public static string Command(string executable)
    {
        var full = Path.GetFullPath(executable);
        if (full.Contains('"') || !string.Equals(Path.GetFileName(full), "BorderlessFlatWindow.App.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("快捷方式入口需要实际主程序 EXE。", nameof(executable));
        return $"\"{full}\" --launch-fixed-shortcut \"%1\"";
    }
    public static void Install(string executable)
    {
        var command = Command(executable);
        using var existing = Registry.CurrentUser.OpenSubKey(Key);
        if (existing is not null && existing.GetValue("BFWOwner") as string != Owner) throw new InvalidOperationException("快捷方式菜单位置已被其他数据占用，未覆盖。");
        using var verb = Registry.CurrentUser.CreateSubKey(Key);
        verb.SetValue("", "以画布便签模式打开"); verb.SetValue("BFWOwner", Owner);
        verb.SetValue("Icon", Path.GetFullPath(executable)); verb.SetValue("MultiSelectModel", "Single");
        using var child = verb.CreateSubKey("command"); child.SetValue("", command);
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    public static void Remove()
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(Key))
            if (existing?.GetValue("BFWOwner") as string != Owner) return;
        Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint item, uint flags, nint first, nint second);
}
