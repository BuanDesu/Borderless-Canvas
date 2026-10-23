using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Discovery;

namespace BorderlessFlatWindow.Windows;

public sealed record WindowPinTarget(NativeWindowIdentity Identity, nint Menu, uint Command, nuint Token, Guid CatalogId = default);

/// <summary>Adds only tagged private entries. Never resets or subclasses a foreign application's menu.</summary>
public sealed class WindowPinMenus : IDisposable
{
    private sealed record Binding(WindowPinTarget Target, uint Separator, bool Pinned);
    private readonly Dictionary<NativeWindowIdentity, Binding> bindings = [];
    private bool disposed;
    public IReadOnlyList<WindowPinTarget> Synchronize(IEnumerable<(NativeWindowIdentity Identity, bool Pinned)> windows)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var requested = windows.ToDictionary(item => item.Identity, item => item.Pinned);
        foreach (var identity in bindings.Keys.Where(identity => !requested.ContainsKey(identity)).ToArray())
        { Remove(bindings[identity]); bindings.Remove(identity); }
        var targets = new List<WindowPinTarget>();
        foreach (var (identity, pinned) in requested)
        {
            if (!NativeWindowIdentityGuard.IsCurrent(identity)) continue;
            if (!bindings.TryGetValue(identity, out var binding) || GetSystemMenu(identity.Handle, false) != binding.Target.Menu || !Owns(binding.Target))
            {
                if (binding is not null) Remove(binding);
                bindings.Remove(identity); binding = Install(identity, pinned);
                if (binding is not null) bindings[identity] = binding;
            }
            if (binding is { } current)
            {
                if (current.Pinned != pinned)
                {
                    var item = Item(current.Target.Command, current.Target.Token, pinned);
                    if (SetMenuItemInfo(current.Target.Menu, current.Target.Command, false, ref item)) bindings[identity] = current with { Pinned = pinned };
                }
                targets.Add(current.Target);
            }
            else targets.Add(new(identity, 0, 0, 0)); // The independent hotkey remains available when an app has no system menu.
        }
        return targets;
    }
    private static Binding? Install(NativeWindowIdentity identity, bool pinned)
    {
        var menu = GetSystemMenu(identity.Handle, false); if (menu == 0) return null;
        uint command = 0;
        for (uint candidate = 0x2BF0; candidate <= 0x2EF0; candidate += 0x10)
            if (GetMenuState(menu, candidate, 0) == uint.MaxValue && GetMenuState(menu, candidate + 1, 0) == uint.MaxValue) { command = candidate; break; }
        if (command == 0) return null;
        var token = (nuint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var separator = new MenuItem { Size = (uint)Marshal.SizeOf<MenuItem>(), Mask = 0x100 | 2 | 0x20, Type = 0x800, Id = command + 1, Data = token };
        if (!InsertMenuItem(menu, uint.MaxValue, true, ref separator)) return null;
        var target = new WindowPinTarget(identity, menu, command, token);
        var item = Item(command, token, pinned);
        if (!InsertMenuItem(menu, uint.MaxValue, true, ref item))
        { var temporary = new WindowPinTarget(identity, menu, command + 1, token); if (Owns(temporary)) RemoveMenu(menu, command + 1, 0); return null; }
        return new(target, command + 1, pinned);
    }
    private static MenuItem Item(uint command, nuint token, bool pinned) => new()
    { Size = (uint)Marshal.SizeOf<MenuItem>(), Mask = 0x40 | 2 | 0x20 | 1, Id = command, Data = token, State = pinned ? 8u : 0,
        Text = pinned ? "取消便签 · 返回画布\tCtrl+Alt+P" : "固定为画布便签\tCtrl+Alt+P" };
    private static void Remove(Binding binding)
    {
        var target = binding.Target;
        if (!NativeWindowIdentityGuard.IsCurrent(target.Identity) || GetSystemMenu(target.Identity.Handle, false) != target.Menu) return;
        if (Owns(target)) RemoveMenu(target.Menu, target.Command, 0);
        if (Owns(target with { Command = binding.Separator })) RemoveMenu(target.Menu, binding.Separator, 0);
    }
    internal static bool Owns(WindowPinTarget target)
    {
        var item = new MenuItem { Size = (uint)Marshal.SizeOf<MenuItem>(), Mask = 0x20 | 2 };
        return target.Menu != 0 && target.Token != 0 && GetMenuItemInfo(target.Menu, target.Command, false, ref item) && item.Id == target.Command && item.Data == target.Token;
    }
    public void Dispose()
    { if (disposed) return; disposed = true; foreach (var binding in bindings.Values) Remove(binding); bindings.Clear(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MenuItem
    { internal uint Size, Mask, Type, State, Id; internal nint Submenu, Checked, Unchecked; internal nuint Data; [MarshalAs(UnmanagedType.LPWStr)] internal string? Text; internal uint Length; internal nint Bitmap; }
    [DllImport("user32.dll")] internal static extern nint GetSystemMenu(nint window, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll")] internal static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll", EntryPoint = "InsertMenuItemW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InsertMenuItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItem info);
    [DllImport("user32.dll", EntryPoint = "SetMenuItemInfoW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenuItemInfo(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItem info);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMenuItemInfo(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItem info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveMenu(nint menu, uint position, uint flags);
}
