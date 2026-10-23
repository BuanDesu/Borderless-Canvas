using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

public sealed partial class ManagedCanvasSession
{
    private sealed record FocusMotion(Guid Target, PointD From, PointD To, PointD Origin, DateTimeOffset Started);
    private FocusMotion? focusMotion;
    public bool HasCameraTransition => focusMotion is not null;
    public static readonly TimeSpan WindowCycleDuration = TimeSpan.FromMilliseconds(150);
    public void StopCameraTransition() { CancelCameraTransition(); activation = cycleTarget = null; }

    public bool AdvanceCameraTransition(DateTimeOffset now)
    {
        if (focusMotion is not { } motion || !IsRunning || busy || NavigationSuspended) return false;
        if (!managed.ContainsKey(motion.Target)) { CancelCameraTransition(); return false; }
        busy = true;
        try
        {
            var progress = Math.Clamp((now - motion.Started).TotalMilliseconds / WindowCycleDuration.TotalMilliseconds, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);
            var position = new PointD(motion.From.X + (motion.To.X - motion.From.X) * eased, motion.From.Y + (motion.To.Y - motion.From.Y) * eased);
            if (!ApplyCamera(position, now, recordHistory: false))
            { CancelCameraTransition(); return false; }
            if (progress >= 1)
            {
                focusMotion = null; Workspace.RecordNavigationOrigin(motion.Origin);
                activation = motion.Target; CompleteActivation();
            }
            return true;
        }
        catch (Exception error) when (IsExpected(error)) { PauseInternal($"轮选过渡失败，已暂停恢复：{error.Message}"); return false; }
        finally { ExitBusy(); }
    }
    private void CancelCameraTransition()
    {
        if (focusMotion is { } motion) Workspace.RecordNavigationOrigin(motion.Origin);
        focusMotion = null;
    }
}
