using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private WindowPinTarget[] pinTargets = [];
    private bool pinAvailable, pinMenuEnterHeld;
    private WindowPinTarget? pinMenuDown;
    public void ConfigurePinTargets(IEnumerable<WindowPinTarget> targets, bool enabled)
    { var copy = targets.ToArray(); Post(() => { pinTargets = copy; pinAvailable = enabled; }); }
    private void QueueFocusedPin()
    {
        if (!pinAvailable || overviewOpen || MenuActive()) return;
        var handle = GetAncestor(WindowEventListener.ForegroundWindow, 2);
        if (pinTargets.FirstOrDefault(item => item.Identity.Handle == handle) is { } target) QueuePin(target);
    }
    private void QueuePin(WindowPinTarget target)
        => Queue(new(NavigationInputKind.TogglePin, PointD.Zero, TargetWindow: target.Identity.Handle, TargetIdentity: target.Identity, TargetId: target.CatalogId));
    private WindowPinTarget? CurrentPinMenu()
    {
        if (!pinAvailable || overviewOpen) return null;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || (info.Flags & 8) == 0) return null;
        return pinTargets.FirstOrDefault(target => target.Identity.Handle == info.MenuOwner && target.Menu != 0
            && WindowPinMenus.GetSystemMenu(info.MenuOwner, false) == target.Menu && WindowPinMenus.Owns(target));
    }
    private static bool OverPinItem(WindowPinTarget target, Point point)
    {
        var count = GetMenuItemCount(target.Menu);
        for (uint position = 0; position < count; position++)
            if (GetMenuItemID(target.Menu, position) == target.Command && GetMenuItemRect(0, target.Menu, position, out var rectangle))
                return point.X >= rectangle.Left && point.X < rectangle.Right && point.Y >= rectangle.Top && point.Y < rectangle.Bottom;
        return false;
    }
    private bool ConsumePinMenuClick(NavigationInputKind kind, Point point)
    {
        if (kind == NavigationInputKind.LeftUp && pinMenuDown is { } pressed)
        {
            pinMenuDown = null;
            if (CurrentPinMenu() == pressed && OverPinItem(pressed, point))
            { PostMessage(pressed.Identity.Handle, 0x1F, 0, 0); QueuePin(pressed); }
            return true;
        }
        if (kind == NavigationInputKind.LeftDown && CurrentPinMenu() is { } target && OverPinItem(target, point))
        { pinMenuDown = target; return true; }
        return false;
    }
    private bool ConsumePinMenuEnter(uint key, bool up)
    {
        if (key != 0x0D) return false;
        if (up && pinMenuEnterHeld) { pinMenuEnterHeld = false; return true; }
        if (!up && pinMenuEnterHeld) return true;
        if (!up && CurrentPinMenu() is { } target)
        {
            var state = WindowPinMenus.GetMenuState(target.Menu, target.Command, 0);
            if (state != uint.MaxValue && (state & 0x80) != 0)
            { pinMenuEnterHeld = true; PostMessage(target.Identity.Handle, 0x1F, 0, 0); QueuePin(target); return true; }
        }
        return false;
    }
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, uint position);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMenuItemRect(nint window, nint menu, uint item, out CursorRect rect);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint parameter, nint data);
}
