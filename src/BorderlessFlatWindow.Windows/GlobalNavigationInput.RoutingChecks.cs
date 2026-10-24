using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    // Fixture access is internal and unavailable on the public/native constructor.
    // It drives the same worker and routing functions, with no global hooks, hotkeys,
    // Raw Input registration, synthetic OS input or real cursor clipping.
    internal void SynchronizeRoutingCheck(Action? before = null) => RoutingCheck(() => { before?.Invoke(); return true; });
    internal bool MouseRoutingCheck(nuint message, PointD position, int wheel = 0, Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); return RouteMouse(message, new() { Point = new() { X = (int)position.X, Y = (int)position.Y }, Data = unchecked((uint)(wheel << 16)) }); });
    internal bool KeyRoutingCheck(uint key, bool up = false, Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); return RouteKeyboard(up ? 0x101u : 0x100u, key); });
    internal void RelativeRoutingCheck(PointD delta, Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); ProcessPointerSample(new(delta)); return true; });
    internal void SampleRoutingCheck(RawPointerSample sample, Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); ProcessPointerSample(sample); return true; });
    internal void TimerRoutingCheck(Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); RetryInputWake(); RefreshTouchReceivers(); TryPendingCrosshair(); CheckRelativeSource(); return true; });
    internal void TouchWheelMessageRoutingCheck(uint message, int delta, bool shift = false, bool control = false)
        => RoutingCheck(() => { ReceiveTouchWheel(message, unchecked((nuint)((delta << 16) | (shift ? 4 : 0) | (control ? 8 : 0)))); return true; });
    internal nint CreateHiddenTouchReceiverForCheck() => RoutingCheck(() =>
    {
        if (!touchEnabled) throw new InvalidOperationException("Touch mode fixture is not running.");
        var receiver = new TouchWheelReceiver(new(-3000, -3000, -2600, -2700), ReceiveTouchWheel, () => { }, show: false);
        touchReceivers.Add(receiver); return receiver.Handle;
    });
    internal bool TouchReceiverTransparentForCheck() => RoutingCheck(() => touchReceivers.Single().NativePassThrough);
    internal void TouchReceiverMessageForCheck(uint message, int delta)
        => RoutingCheck(() => { touchReceivers.Single().SendFixtureMessage(message, unchecked((nuint)(delta << 16))); return true; });
    private T RoutingCheck<T>(Func<T> action)
    {
        if (nativeInput) throw new InvalidOperationException("Routing fixtures cannot operate native desktop input.");
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => { try { result.SetResult(action()); } catch (Exception error) { result.SetException(error); } });
        return result.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
}
