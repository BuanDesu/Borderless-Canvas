using BorderlessFlatWindow.Application.Management;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class DesktopSessionChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Session notifications register only an own UI window and release idempotently", Registration);
        check("Session notification registration rejects invalid and foreign-thread handles", InvalidOwner);
        check("Native lifecycle mapping recognizes lock/disconnect/power/display and ignores unrelated messages", Mapping);
    }
    private sealed class Fixture : Form
    { protected override bool ShowWithoutActivation => true; internal Fixture() { Opacity = 0; ShowInTaskbar = false; Bounds = new(100, 100, 220, 140); } }
    private static void Registration()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var window = new Fixture(); window.Show();
        using var notifications = new DesktopSessionNotifications(window.Handle);
        Require(notifications.IsRegistered, "Own window did not receive a session subscription.");
        notifications.Dispose(); notifications.Dispose();
        Require(!notifications.IsRegistered && WindowEventListener.ForegroundWindow == foreground, "Subscription disposal leaked or changed foreground.");
    }
    private static void InvalidOwner()
    {
        try { using var invalid = new DesktopSessionNotifications(0); throw new InvalidOperationException("Zero HWND registered."); }
        catch (ArgumentException) { }
        using var window = new Fixture(); window.Show(); var handle = window.Handle;
        var rejected = Task.Run(() =>
        {
            try { using var wrongThread = new DesktopSessionNotifications(handle); return false; }
            catch (ArgumentException) { return true; }
        }).GetAwaiter().GetResult();
        Require(rejected, "Foreign thread registered an unrelated notification receiver.");
    }
    private static void Mapping()
    {
        (int Message, nuint Value, DesktopLifecycleSignal Expected)[] expected = [
            (0x007E, 32, DesktopLifecycleSignal.DisplayChanged), (0x02E0, 144, DesktopLifecycleSignal.DpiChanged),
            (0x0218, 4, DesktopLifecycleSignal.PowerSuspending), (0x0218, 6, DesktopLifecycleSignal.PowerResumed),
            (0x0218, 7, DesktopLifecycleSignal.PowerResumed), (0x0218, 0x12, DesktopLifecycleSignal.PowerResumed),
            (0x02B1, 1, DesktopLifecycleSignal.SessionConnected), (0x02B1, 3, DesktopLifecycleSignal.SessionConnected),
            (0x02B1, 5, DesktopLifecycleSignal.SessionConnected), (0x02B1, 15, DesktopLifecycleSignal.SessionConnected),
            (0x02B1, 2, DesktopLifecycleSignal.SessionDisconnected), (0x02B1, 4, DesktopLifecycleSignal.SessionDisconnected),
            (0x02B1, 6, DesktopLifecycleSignal.SessionDisconnected), (0x02B1, 7, DesktopLifecycleSignal.SessionLocked), (0x02B1, 8, DesktopLifecycleSignal.SessionUnlocked)];
        foreach (var item in expected) Require(DesktopSessionNotifications.TryRead(item.Message, item.Value, out var actual) && actual == item.Expected, "Required native system event was lost.");
        foreach (var item in new[] { (0x000F, (nuint)4), (0x0218, (nuint)10), (0x0218, (nuint)0x8013), (0x02B1, nuint.MaxValue) })
            Require(!DesktopSessionNotifications.TryRead(item.Item1, item.Item2, out _), "Paint/power-status/unknown event paused management.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
