using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private const int CrosshairId = 0x4BF5, PinId = 0x4BF6;
    private RawPointerInput? rawPointer;
    private PixelRect[] pointerScreens = [];
    private bool clipOwned, previousClipUnrestricted;
    private int crosshairButtons;
    private readonly HashSet<uint> pointerAuxButtons = [];
    private Point pointerCenter, pointerBefore;
    private long crosshairEntered, lastRelativeMotion, lastLegacyMotion;
    private nint crosshairForeground;
    private bool pendingCrosshair;
    private long crosshairMenuDeadline;
    private CursorRect previousClip, ownedClip;
    private string pointerMessage = "正在准备边缘推进与准星模式。";
    public string PointerMessage => Volatile.Read(ref pointerMessage);
    public void ToggleCrosshair(bool afterMenu = false) => Post(() =>
    {
        if (afterMenu && available && !crosshair && !pendingCrosshair && MenuActive())
        {
            pendingCrosshair = true; crosshairMenuDeadline = InputTime + 1200;
            Publish(gate.Active, State.Registered, State.Hotkey, "菜单关闭后进入准星模式。"); return;
        }
        ToggleCrosshairCore();
    });
    private void TryPendingCrosshair()
    {
        if (!pendingCrosshair) return;
        if (!available || overviewOpen || InputTime > crosshairMenuDeadline)
        { pendingCrosshair = false; Publish(gate.Active, State.Registered, State.Hotkey, "准星请求已取消，请关闭菜单后重试。"); return; }
        if (MenuActive()) return;
        pendingCrosshair = false; ToggleCrosshairCore();
    }
    public void ConfigureScreens(IEnumerable<PixelRect> screens)
    {
        var copy = screens.ToArray(); if (copy.Any(screen => !screen.IsValid)) throw new ArgumentException("Invalid pointer surface.", nameof(screens));
        Post(() => { if (!pointerScreens.SequenceEqual(copy)) { Leave("显示区域已更新，导航已退出。"); pointerScreens = copy; } });
    }
    private void InitializePointerInput()
    {
        try { if (nativeInput) rawPointer = new(); pointerMessage = "边缘向外推进 · Ctrl+Alt+C 准星 · Ctrl+Alt+P 便签"; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { pointerMessage = "相对鼠标不可用，保留滚轮和双键导航：" + error.Message; }
        if (!RegisterKey(CrosshairId, 3 | 0x4000, 0x43)) pointerMessage += "；准星快捷键注册失败，可从视角菜单进入。";
        if (!RegisterKey(PinId, 3 | 0x4000, 0x50)) pointerMessage += "；便签快捷键注册失败，可用窗口菜单。";
    }
    private void ProcessRelativePointer(nint packet)
    {
        if (rawPointer is null || !available || overviewOpen) return;
        if (rawPointer.ReadSample(packet, out var sample)) ProcessPointerSample(sample);
    }
    private void ProcessPointerMovement(PointD delta)
    {
        if (crosshair && delta != PointD.Zero) lastRelativeMotion = InputTime;
        if (!available || overviewOpen || MenuActive() || !GetPointer(out var pointer)) return;
        if (!crosshair && (Down(1) || Down(2) || Down(4) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C) || gate.HasHeldButtons)) return;
        if (crosshair && (!ReadPointerClip(out var current) || !SameClip(current, ownedClip)))
        { Leave("鼠标限制已被其他应用更改，准星模式已退出。"); return; }
        var movement = PointerNavigation.CameraDelta(delta, new(pointer.X, pointer.Y), pointerScreens, gate.Options, crosshair);
        if (movement != PointD.Zero) Queue(new(NavigationInputKind.CameraDelta, movement));
    }
    private void ToggleCrosshairCore()
    {
        if (pendingCrosshair) { pendingCrosshair = false; Publish(gate.Active, State.Registered, State.Hotkey, "已取消等待进入准星模式。"); return; }
        if (crosshair) { Leave("已退出准星模式，鼠标恢复自由移动。"); return; }
        if (!available || overviewOpen || pointerScreens.Length == 0 || MenuActive())
        { Publish(gate.Active, State.Registered, State.Hotkey, "请先开始画布并关闭总览或菜单。"); return; }
        if (Down(1) || Down(2) || Down(4) || Down(5) || Down(6) || gate.HasHeldButtons || !GetPointer(out var currentPointer))
        { Publish(gate.Active, State.Registered, State.Hotkey, "请先松开鼠标按键，再进入准星模式。"); return; }
        var screen = pointerScreens.FirstOrDefault(bounds => currentPointer.X >= bounds.Left && currentPointer.X < bounds.Right && currentPointer.Y >= bounds.Top && currentPointer.Y < bounds.Bottom);
        if (!screen.IsValid) return;
        Leave("正在进入准星模式。");
        if (clipOwned) { Publish(false, State.Registered, State.Hotkey, "鼠标限制仍待释放，请稍后再进入准星模式。"); return; }
        if (!RegisterKey(EscapeId, 0x4000, 0x1B) || !RegisterKey(HomeId, 0x4000, 0x24) || !RegisterKey(BackId, 0x4000, 0x08))
        { ReleaseModeKeys(); Publish(false, State.Registered, State.Hotkey, "退出快捷键冲突，未进入准星模式。"); return; }
        escapeRegistered = true;
        if (!ReadPointerClip(out previousClip)) { ReleaseModeKeys(); Publish(false, State.Registered, State.Hotkey, "无法读取鼠标限制，未进入准星模式。"); return; }
        var surface = platform.VirtualScreen;
        previousClipUnrestricted = previousClip.Left == surface.Left && previousClip.Top == surface.Top
            && previousClip.Right == surface.Right && previousClip.Bottom == surface.Bottom;
        pointerBefore = currentPointer; pointerCenter = new() { X = screen.Left + screen.Width / 2, Y = screen.Top + screen.Height / 2 };
        ownedClip = new() { Left = pointerCenter.X, Top = pointerCenter.Y, Right = pointerCenter.X + 1, Bottom = pointerCenter.Y + 1 };
        if (!WritePointerClip(ownedClip)) { ReleaseModeKeys(); Publish(false, State.Registered, State.Hotkey, "无法限制鼠标，未进入准星模式。"); return; }
        clipOwned = true;
        if (!SetPointer(pointerCenter)) { Leave("无法定位准星，已恢复鼠标。"); return; }
        mode = NavigationMode.Crosshair; crosshairEntered = InputTime; lastRelativeMotion = lastLegacyMotion = 0;
        ResetPointerFeeds(screen);
        crosshairForeground = ForegroundWindow;
        gate.TryEnter(false); Volatile.Write(ref heartbeat, InputTime);
        Publish(true, State.Registered, State.Hotkey, "准星模式 · 相对鼠标移动镜头 · Esc / Ctrl+Alt+C 退出");
    }
    private bool ConsumeCrosshairButton(NavigationInputKind kind)
    {
        var bit = kind is NavigationInputKind.LeftDown or NavigationInputKind.LeftUp ? 1 : kind is NavigationInputKind.RightDown or NavigationInputKind.RightUp ? 2 : 0;
        if (kind is NavigationInputKind.LeftUp or NavigationInputKind.RightUp && (crosshairButtons & bit) != 0)
        { crosshairButtons &= ~bit; return true; }
        if (crosshair && kind is NavigationInputKind.LeftDown or NavigationInputKind.RightDown)
        { crosshairButtons |= bit; return true; }
        return false;
    }
    private bool ConsumePointerAuxClick(nuint message, MouseInput input)
    {
        if (message is not (0x207 or 0x208 or 0x20B or 0x20C)) return false;
        var button = message is 0x207 or 0x208 ? 4u : (input.Data >> 16) == 1 ? 5u : 6u;
        var up = message is 0x208 or 0x20C;
        if (up && pointerAuxButtons.Remove(button)) return true;
        if (!up && crosshair) { pointerAuxButtons.Add(button); return true; }
        return false;
    }
    private void ReleasePointerCapture()
    {
        if (crosshair) mode = NavigationMode.Inactive;
        if (!clipOwned) return;
        // Release only our exact rectangle. Never overwrite a later application's cursor capture.
        if (!ReadPointerClip(out var current)) { if (WritePointerClip(null)) clipOwned = false; return; }
        if (!SameClip(current, ownedClip)) { clipOwned = false; return; }
        var released = WritePointerClip(previousClipUnrestricted ? null : previousClip);
        if (released) { clipOwned = false; SetPointer(pointerBefore); }
    }
    private static bool SameClip(CursorRect a, CursorRect b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    private void CheckRelativeSource()
    {
        if (crosshair && (ForegroundWindow != crosshairForeground || MenuActive()))
        { Leave("焦点或菜单已改变，已退出准星模式。"); return; }
        if (crosshair && pointerFeed == PointerFeed.Legacy) KeepPointerCentered();
        var lastRelative = Math.Max(lastRelativeMotion, crosshairEntered);
        if (crosshair && pointerFeed != PointerFeed.Legacy && lastLegacyMotion > lastRelative + 100 && InputTime - lastRelative > 600)
            SetPointerFeed(PointerFeed.Legacy);
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClipCursor(out CursorRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClipCursor(ref CursorRect rect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClipCursorNone(nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
}
