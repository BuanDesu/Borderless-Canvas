using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Discovery;

namespace BorderlessFlatWindow.Application.Navigation;

public enum NavigationInputKind { Move, LeftDown, LeftUp, RightDown, RightUp, Wheel, HorizontalWheel, Home, Back, Overview, CycleWindow, Exit, Fault, CameraDelta, TogglePin, PanStart, PanMove, PanEnd }
public readonly record struct NavigationInput(NavigationInputKind Kind, PointD Position, int WheelDelta = 0, bool Shift = false, nint TargetWindow = 0, NativeWindowIdentity? TargetIdentity = null, Guid TargetId = default);
public sealed record NavigationInputOptions
{
    public NavigationInputOptions(bool verticalWheel = true, bool horizontalWheel = true, bool twoButtonDrag = true, int wheelDistance = 120, bool edgePan = true, double pointerSpeed = 1)
    {
        if (wheelDistance is < 30 or > 600) throw new ArgumentOutOfRangeException(nameof(wheelDistance));
        if (!double.IsFinite(pointerSpeed) || pointerSpeed is < .25 or > 4) throw new ArgumentOutOfRangeException(nameof(pointerSpeed));
        VerticalWheel = verticalWheel; HorizontalWheel = horizontalWheel; TwoButtonDrag = twoButtonDrag; WheelDistance = wheelDistance;
        EdgePan = edgePan; PointerSpeed = pointerSpeed;
    }
    public bool VerticalWheel { get; }
    public bool HorizontalWheel { get; }
    public bool TwoButtonDrag { get; }
    public int WheelDistance { get; }
    public bool EdgePan { get; }
    public double PointerSpeed { get; }
}

/// <summary>Desktop-only automatic input. An application drag may cross the desktop without being captured.</summary>
public sealed class DesktopNavigationPolicy
{
    private int applicationButtons;
    private bool suspended;
    public bool ShouldNavigate(bool desktop, bool available, bool overview, bool capturedButtons, bool physicalButtons)
    {
        if (!desktop) suspended = false;
        return available && !overview && !suspended && (capturedButtons || desktop && applicationButtons == 0 && !physicalButtons);
    }
    public void Suspend() => suspended = true;
    public void ObserveButton(NavigationInputKind kind, bool captured)
    {
        var bit = kind is NavigationInputKind.LeftDown or NavigationInputKind.LeftUp ? 1
            : kind is NavigationInputKind.RightDown or NavigationInputKind.RightUp ? 2 : 0;
        if (kind is NavigationInputKind.LeftUp or NavigationInputKind.RightUp) applicationButtons &= ~bit;
        else if (!captured) applicationButtons |= bit;
    }
    public static bool OpensOverview(bool desktop, bool available, bool overview, bool control, bool alt, bool windows)
        => desktop && available && !overview && !control && !alt && !windows;
}

/// <summary>Pairs swallowed downs/ups even when navigation ends with a button still held.</summary>
public sealed class NavigationInputGate
{
    private int blocked;
    public NavigationInputOptions Options { get; set; } = new();
    public bool Active { get; private set; }
    public bool HasHeldButtons => blocked != 0;
    public bool TryEnter(bool physicalButtonHeld)
    {
        if (physicalButtonHeld || blocked != 0) return false;
        Active = true; return true;
    }
    public void Exit() => Active = false;
    public (bool Suppress, bool Queue) Route(NavigationInputKind kind, bool shift = false)
    {
        var down = kind switch { NavigationInputKind.LeftDown => 1, NavigationInputKind.RightDown => 2, _ => 0 };
        var up = kind switch { NavigationInputKind.LeftUp => 1, NavigationInputKind.RightUp => 2, _ => 0 };
        if (!Active)
        {
            var paired = up != 0 && (blocked & up) != 0;
            if (paired) blocked &= ~up;
            return (paired, false);
        }
        if (kind is NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)
        {
            var enabled = kind == NavigationInputKind.HorizontalWheel || shift ? Options.HorizontalWheel : Options.VerticalWheel;
            return (enabled, enabled);
        }
        if (!Options.TwoButtonDrag) return (false, false);
        if (down != 0) blocked |= down;
        if (up != 0) blocked &= ~up;
        if (down != 0 || up != 0 || kind is NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)
            return (true, true);
        var drag = kind == NavigationInputKind.Move && blocked == 3;
        // Let the system pointer advance; swallowing movement can pin its next absolute position.
        // Applications never receive the navigation button downs/ups or wheel events.
        return (false, drag);
    }
}

/// <summary>One history entry per two-button drag or wheel burst. No native calls in this model.</summary>
public sealed class NavigationGestureSession
{
    private readonly Func<PointD> camera;
    private readonly Func<PointD, DateTimeOffset, bool> navigate;
    private readonly Action<PointD> recordOrigin;
    private PointD? origin;
    private PointD pending;
    private PointD pointer;
    private bool left, right, dragging, directPan;
    private DateTimeOffset? wheelAt;
    public bool HasGesture => origin is not null;
    public NavigationInputOptions Options { get; set; } = new();
    public NavigationGestureSession(Func<PointD> camera, Func<PointD, DateTimeOffset, bool> navigate, Action<PointD> recordOrigin)
    { this.camera = camera; this.navigate = navigate; this.recordOrigin = recordOrigin; }
    public void Accept(NavigationInput input, DateTimeOffset now)
    {
        if (input.Kind is NavigationInputKind.Exit or NavigationInputKind.Fault) { End(now); left = right = dragging = false; return; }
        if (input.Kind == NavigationInputKind.PanStart)
        {
            if (directPan) return;
            End(now); left = right = dragging = false;
            Begin(); pointer = input.Position; directPan = true; return;
        }
        if (input.Kind is NavigationInputKind.PanMove or NavigationInputKind.PanEnd)
        {
            if (!directPan) return;
            pending += pointer - input.Position; pointer = input.Position;
            if (input.Kind == NavigationInputKind.PanEnd) End(now);
            return;
        }
        if (directPan) return;
        if (input.Kind == NavigationInputKind.CameraDelta)
        {
            if (dragging) return;
            if (wheelAt is { } previous && now - previous >= TimeSpan.FromMilliseconds(250)) End(now);
            Begin(); wheelAt = now; pending += input.Position; return;
        }
        if (input.Kind is NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)
        {
            if (dragging) return;
            if (input.Kind == NavigationInputKind.HorizontalWheel || input.Shift)
            { if (!Options.HorizontalWheel) return; }
            else if (!Options.VerticalWheel) return;
            if (wheelAt is { } last && now - last >= TimeSpan.FromMilliseconds(250)) End(now);
            Begin(); wheelAt = now;
            var distance = input.WheelDelta / 120d * Options.WheelDistance;
            pending += input.Kind == NavigationInputKind.HorizontalWheel ? new PointD(distance, 0)
                : input.Shift ? new PointD(-distance, 0) : new PointD(0, -distance);
            return;
        }
        if (!Options.TwoButtonDrag) return;
        if (input.Kind == NavigationInputKind.LeftDown) left = true;
        if (input.Kind == NavigationInputKind.RightDown) right = true;
        if (input.Kind == NavigationInputKind.LeftUp) left = false;
        if (input.Kind == NavigationInputKind.RightUp) right = false;
        if (!dragging && left && right) { End(now); Begin(); pointer = input.Position; dragging = true; }
        else if (dragging && (!left || !right)) { End(now); dragging = false; }
        else if (dragging && input.Kind == NavigationInputKind.Move)
        { pending += pointer - input.Position; pointer = input.Position; }
    }
    public void Tick(DateTimeOffset now)
    {
        Flush(now);
        if (wheelAt is { } last && now - last >= TimeSpan.FromMilliseconds(250)) End(now);
    }
    public void End(DateTimeOffset now)
    {
        Flush(now);
        if (origin is { } start) recordOrigin(start);
        origin = null; wheelAt = null; pending = PointD.Zero; directPan = false;
    }
    public void Cancel()
    {
        if (origin is { } start) recordOrigin(start);
        origin = null; wheelAt = null; pending = PointD.Zero; left = right = dragging = directPan = false;
    }
    private void Begin() => origin ??= camera();
    private void Flush(DateTimeOffset now)
    {
        if (pending == PointD.Zero) return;
        var delta = pending; pending = PointD.Zero;
        if (!navigate(camera() + delta, now)) { if (origin is { } start) recordOrigin(start); origin = null; wheelAt = null; directPan = false; }
    }
}
