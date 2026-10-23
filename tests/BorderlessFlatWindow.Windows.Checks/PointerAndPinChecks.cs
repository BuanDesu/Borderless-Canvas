using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class PointerAndPinChecks
{
    internal static void Run(Form fixture, Action<string, Action> check)
    {
        check("Raw pointer packets decode both native layouts and reject absolute, malformed and non-mouse data", Packets);
        check("Raw mouse sink stays invisible, never disables legacy input, rejects a duplicate owner and releases registration", RawLifecycle);
        check("Pin menu entries preserve application commands, update state and remove only their own tagged items", () => Menu(fixture));
        check("Window pin markers survive manager objects, reject changed identity and preserve placement and foreground", () => Marker(fixture));
        check("A separate test process accepts and releases pin menu entries without subclassing or activation", ForeignMenu);
        check("Executable shortcut inspection preserves the link and never launches a target", Shortcut);
        check("Shortcut context commands quote native paths and pass one literal link argument", Command);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static NativeWindowIdentity Identity(Form form) => FixtureReadiness.Wait(form.Handle).Identity;
    private static void Packets()
    {
        foreach (var size in new[] { 4, 8 })
        {
            var header = 8 + size * 2; var bytes = new byte[header + 24]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 12), 24); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 16), -17);
            Require(RawPointerInput.Decode(bytes, size, out var delta, out var absolute) && delta == new PointD(24, -17) && !absolute, "Relative packet layout decoded incorrectly.");
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(header), 1);
            Require(!RawPointerInput.Decode(bytes, size, out _, out absolute) && absolute, "Absolute coordinates were treated as relative displacement.");
            bytes[0] = 1; Require(!RawPointerInput.Decode(bytes, size, out _, out _), "Non-mouse packet accepted.");
            Require(!RawPointerInput.Decode(bytes.AsSpan(0, 8), size, out _, out _), "Truncated packet accepted.");
        }
    }
    private static void RawLifecycle()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var source = new RawPointerInput();
        Require(source.Handle != 0 && !IsWindowVisible(source.Handle), "Raw sink was visible or absent.");
        var devices = ReadDevices(); var device = devices.Single(item => item.Target == source.Handle && item.Page == 1 && item.Usage == 2);
        Require((device.Flags & 0x100) != 0 && (device.Flags & 0x30) == 0, "Raw sink disabled ordinary application mouse messages.");
        try { using var duplicate = new RawPointerInput(); throw new Exception("Duplicate raw owner accepted."); } catch (InvalidOperationException) { }
        Require(ReadDevices().Any(item => item.Target == source.Handle), "A rejected duplicate removed the original raw registration.");
        var handle = source.Handle; source.Dispose(); source.Dispose();
        Require(ReadDevices().All(item => item.Target != handle) && WindowEventListener.ForegroundWindow == foreground, "Raw registration leaked or changed foreground.");
    }
    private static RawDevice[] ReadDevices()
    {
        uint count = 0; Require(GetRegisteredRawInputDevicesSize(0, ref count, (uint)Marshal.SizeOf<RawDevice>()) == 0, "Could not inspect own raw registration.");
        if (count == 0) return [];
        var devices = new RawDevice[count]; Require(GetRegisteredRawInputDevices(devices, ref count, (uint)Marshal.SizeOf<RawDevice>()) != uint.MaxValue, "Could not read own raw registration."); return devices;
    }
    private static void Menu(Form fixture)
    {
        var identity = Identity(fixture); var menu = GetSystemMenu(identity.Handle, false); var before = GetMenuItemCount(menu); var foreground = WindowEventListener.ForegroundWindow;
        Require(AppendMenu(menu, 0, 0x2BF0, "Fixture application command"), "Could not prepare own application menu.");
        try
        {
            using var integration = new WindowPinMenus(); var target = integration.Synchronize([(identity, false)]).Single();
            Require(target.Menu == menu && target.Command != 0x2BF0 && GetMenuItemCount(menu) == before + 3 && GetMenuState(menu, target.Command, 0) != uint.MaxValue, "Pin command collided or was not installed.");
            integration.Synchronize([(identity, true)]);
            Require((GetMenuState(menu, target.Command, 0) & 8) != 0, "Pinned check state was not updated.");
            integration.Dispose();
            Require(GetMenuItemCount(menu) == before + 1 && GetMenuState(menu, 0x2BF0, 0) != uint.MaxValue && WindowEventListener.ForegroundWindow == foreground, "Menu disposal removed an application command or changed foreground.");
        }
        finally { RemoveMenu(menu, 0x2BF0, 0); }
    }
    private static void Marker(Form fixture)
    {
        var identity = Identity(fixture); var before = fixture.Bounds; var foreground = WindowEventListener.ForegroundWindow;
        WindowPinMarker.Set(identity, true);
        try
        {
            Require(WindowPinMarker.IsPinned(identity), "Pin intent was not readable after registration.");
            try { WindowPinMarker.Set(identity with { ProcessCreationFileTime = identity.ProcessCreationFileTime + 1 }, false); throw new Exception("Changed identity cleared pin intent."); }
            catch (InvalidOperationException) { }
            Require(WindowPinMarker.IsPinned(identity) && fixture.Bounds == before && WindowEventListener.ForegroundWindow == foreground, "Pin marker changed native placement or foreground.");
        }
        finally { WindowPinMarker.Set(identity, false); }
        Require(!WindowPinMarker.IsPinned(identity), "Pin marker did not release.");
    }
    private static void ForeignMenu()
    {
        var self = Path.Combine(AppContext.BaseDirectory, "BorderlessFlatWindow.Windows.Checks.exe");
        using var process = Process.Start(new ProcessStartInfo(self, "--serve-pin-menu") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })
            ?? throw new InvalidOperationException("Could not start own menu fixture.");
        var foreground = WindowEventListener.ForegroundWindow;
        try
        {
            var ready = process.StandardOutput.ReadLineAsync(); var watch = Stopwatch.StartNew();
            while (!ready.IsCompleted && watch.ElapsedMilliseconds < 6000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
            Require(ready.IsCompleted && long.TryParse(ready.GetAwaiter().GetResult(), out _), "Own menu fixture did not become ready.");
            var handle = (nint)long.Parse(ready.GetAwaiter().GetResult()!);
            var entry = new DesktopWindowCatalog(0).Inspect().Entries.Single(item => item.Observation.Handle == handle);
            Require(entry.Observation.Identity is { } identity && identity.ProcessId == process.Id, "Menu fixture identity was not the exact child.");
            var targetIdentity = entry.Observation.Identity!.Value; var menu = GetSystemMenu(handle, false); var before = GetMenuItemCount(menu);
            using (var integration = new WindowPinMenus())
            {
                var target = integration.Synchronize([(targetIdentity, false)]).Single();
                Require(target.Menu != 0 && GetMenuItemCount(menu) == before + 2 && GetMenuState(menu, target.Command, 0) != uint.MaxValue, "Foreign own-process menu could not be extended.");
            }
            Require(GetMenuItemCount(menu) == before && WindowEventListener.ForegroundWindow == foreground, "Foreign menu release changed commands or foreground.");
            PostMessage(handle, 0x10, 0, 0);
        }
        finally { if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(); } }
    }
    internal static int ServeMenu()
    {
        ApplicationConfiguration.Initialize(); using var form = new MenuFixture();
        form.Shown += (_, _) => { Console.WriteLine(form.Handle.ToInt64()); Console.Out.Flush(); };
        System.Windows.Forms.Application.Run(form); return 0;
    }
    private sealed class MenuFixture : Form
    { internal MenuFixture() { Text = "BFW own menu fixture"; Opacity = 0; ShowInTaskbar = false; Bounds = new(120, 120, 400, 300); } protected override bool ShowWithoutActivation => true; }
    private static void Shortcut()
    {
        var path = Path.ChangeExtension(StorageChecks.NewPath("shortcut"), ".lnk"); var target = Path.Combine(AppContext.BaseDirectory, "BorderlessFlatWindow.Windows.Checks.exe");
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
        try { Marshal.ThrowExceptionForHR(((ShellLink)instance).SetPath(target)); ((IPersistFile)instance).Save(path, true); }
        finally { if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
        var bytes = File.ReadAllBytes(path); var read = ExecutableShortcut.Read(path);
        Require(string.Equals(read.Executable, target, StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(path).SequenceEqual(bytes), "Shortcut read changed bytes or target.");
        try { ExecutableShortcut.Read(path + ".txt"); throw new Exception("Non-shortcut file accepted."); } catch (InvalidDataException) { }
    }
    private static void Command()
    {
        const string path = @"C:\Program Files\BFW & Test\BorderlessFlatWindow.App.exe";
        Require(ShortcutPinVerb.Command(path) == "\"" + path + "\" --launch-fixed-shortcut \"%1\"", "Context command lost literal argument quoting.");
        try { ShortcutPinVerb.Command(@"C:\Fixture\other.exe"); throw new Exception("Non-apphost command accepted."); } catch (ArgumentException) { }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { internal ushort Page, Usage; internal uint Flags; internal nint Target; }
    [DllImport("user32.dll", EntryPoint = "GetRegisteredRawInputDevices")] private static extern uint GetRegisteredRawInputDevicesSize(nint devices, ref uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRegisteredRawInputDevices([Out] RawDevice[] devices, ref uint count, uint size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetSystemMenu(nint window, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint item, string text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveMenu(nint menu, uint item, uint flags);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint parameter, nint data);
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ShellLink
    {
        [PreserveSig] int GetPath(StringBuilder path, int count, nint data, uint flags);
        [PreserveSig] int GetIDList(out nint value); [PreserveSig] int SetIDList(nint value);
        [PreserveSig] int GetDescription(StringBuilder value, int count); [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetWorkingDirectory(StringBuilder value, int count); [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetArguments(StringBuilder value, int count); [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetHotkey(out ushort key); [PreserveSig] int SetHotkey(ushort key); [PreserveSig] int GetShowCmd(out int command); [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation(StringBuilder value, int count, out int index); [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string value, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string value, uint reserved); [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string value);
    }
}
