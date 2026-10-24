using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private enum PointerFeed { Awaiting, Relative, Absolute, Legacy }
    private PointerFeed pointerFeed;
    private readonly AbsolutePointerMotion absolutePointer = new(), legacyPointer = new();
    private PixelRect pointerCaptureScreen;
    private bool centerWarpPending;
    private long centerWarpUntil;
    private string pointerSourceMessage = "尚未进入准星模式。";
    public string PointerSourceMessage => Volatile.Read(ref pointerSourceMessage);

    private void ResetPointerFeeds(PixelRect screen)
    {
        pointerCaptureScreen = screen; pointerFeed = PointerFeed.Awaiting;
        absolutePointer.Reset(); legacyPointer.Reset(); centerWarpPending = false;
        Volatile.Write(ref pointerSourceMessage, "等待鼠标位移；自动选择相对或兼容输入。");
    }
    private bool SetPointerFeed(PointerFeed feed)
    {
        if (pointerFeed == feed) return true;
        pointerFeed = feed; absolutePointer.Reset(); legacyPointer.Reset(); centerWarpPending = false;
        var rectangle = feed == PointerFeed.Legacy
            ? new CursorRect { Left = pointerCaptureScreen.Left, Top = pointerCaptureScreen.Top, Right = pointerCaptureScreen.Right, Bottom = pointerCaptureScreen.Bottom }
            : new CursorRect { Left = pointerCenter.X, Top = pointerCenter.Y, Right = pointerCenter.X + 1, Bottom = pointerCenter.Y + 1 };
        if (!ReadPointerClip(out var current) || !SameClip(current, ownedClip))
        { Leave("鼠标限制已被其他应用更改，准星模式已退出。"); return false; }
        if (!SameClip(rectangle, ownedClip))
        {
            if (!WritePointerClip(rectangle)) { Leave("无法切换鼠标输入范围，准星已退出。"); return false; }
            ownedClip = rectangle;
        }
        var label = feed switch { PointerFeed.Relative => "原生相对位移", PointerFeed.Absolute => "绝对坐标增量", _ => "普通鼠标坐标兼容" };
        Volatile.Write(ref pointerSourceMessage, label);
        Publish(true, State.Registered, State.Hotkey, $"准星模式 · {label} · Esc / {Describe(bindings.Crosshair)} 退出"); return true;
    }
    private void ProcessPointerSample(RawPointerSample sample)
    {
        if (!available || overviewOpen) return;
        if (!crosshair)
        {
            if (sample.Absolute) { edgeMotion.Reset(); return; }
            if (!sample.Absolute && (sample.Value == PointD.Zero || ObserveRawHookHealth())) ProcessPointerMovement(sample.Value);
            return;
        }
        if (sample.Absolute)
        {
            if (sample.Buttons != 0) return; // Transition/wheel packets need not carry a fresh absolute position.
            var bounds = sample.VirtualDesktop ? platform.VirtualScreen : platform.PrimaryScreen;
            if (!bounds.IsValid || !SetPointerFeed(PointerFeed.Absolute)) return;
            var point = AbsolutePointerMotion.MapNormalized(sample.Value, bounds);
            ProcessPointerMovement(absolutePointer.Observe(point, bounds, sample.Device, InputTime));
            return;
        }
        if (sample.Value == PointD.Zero) return;
        var switching = pointerFeed is PointerFeed.Legacy or PointerFeed.Absolute;
        if (!SetPointerFeed(PointerFeed.Relative) || !ObserveRawHookHealth()) return;
        // A legacy hook may have already accepted the sample that announces a new
        // relative device. Rebaseline the handover rather than double-count it.
        if (!switching) ProcessPointerMovement(sample.Value);
        else lastRelativeMotion = InputTime;
    }
    private bool ConsumeCrosshairMove(Point point)
    {
        if (!crosshair) return false;
        if (centerWarpPending && point.X == pointerCenter.X && point.Y == pointerCenter.Y && InputTime <= centerWarpUntil)
        { centerWarpPending = false; return true; }
        lastLegacyMotion = InputTime;
        if (pointerFeed != PointerFeed.Legacy) return false;
        if (point.X == pointerCenter.X && point.Y == pointerCenter.Y) return true;
        var bounds = platform.VirtualScreen;
        if (!bounds.IsValid) bounds = pointerCaptureScreen;
        var delta = legacyPointer.Observe(new(point.X, point.Y), bounds, -1, InputTime);
        ProcessPointerMovement(delta);
        if (!crosshair) return true;
        // Legacy/remote position messages cannot report motion beyond a one-pixel
        // ClipCursor. Suppress their delivery, retain the center, and use successive
        // positions rather than repeatedly adding distance from the center.
        KeepPointerCentered();
        return true;
    }
    private void KeepPointerCentered()
    {
        if (!GetPointer(out var current)) { Leave("无法读取中心光标，准星模式已退出。"); return; }
        if (current.X == pointerCenter.X && current.Y == pointerCenter.Y) return;
        centerWarpPending = true; centerWarpUntil = InputTime + 100;
        if (!SetPointer(pointerCenter)) Leave("无法保持中心光标，准星模式已退出。");
    }
}
