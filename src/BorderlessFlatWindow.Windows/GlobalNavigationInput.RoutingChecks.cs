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
        => RoutingCheck(() => { before?.Invoke(); ProcessPointerMovement(delta); return true; });
    internal void TimerRoutingCheck(Action? before = null)
        => RoutingCheck(() => { before?.Invoke(); TryPendingCrosshair(); CheckRelativeSource(); return true; });
    private T RoutingCheck<T>(Func<T> action)
    {
        if (nativeInput) throw new InvalidOperationException("Routing fixtures cannot operate native desktop input.");
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => { try { result.SetResult(action()); } catch (Exception error) { result.SetException(error); } });
        return result.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
}
