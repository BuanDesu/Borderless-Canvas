using System.Diagnostics;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class NavigationInputChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Dedicated navigation input starts inactive and never activates a desktop window", Inactive);
        check("Global hotkey conflicts are visible and disposal releases the registration", ConflictAndRelease);
        check("Closing overview clears its navigation suspension message and wakes the UI", OverviewState);
    }
    private static void OverviewState()
    {
        var wakes = 0; var foreground = WindowEventListener.ForegroundWindow;
        using var input = new GlobalNavigationInput(() => Interlocked.Increment(ref wakes));
        input.SetOverviewOpen(true); Wait(() => input.State.Message.Contains("总览中"));
        input.Drain(); // A UI frame acknowledges the coalesced wake before the next state change.
        var before = Volatile.Read(ref wakes);
        input.SetOverviewOpen(false); Wait(() => input.State.Message.Contains("总览已关闭") && Volatile.Read(ref wakes) > before);
        Require(!input.State.Active && Volatile.Read(ref wakes) > before && foreground == WindowEventListener.ForegroundWindow,
            "Overview closure retained stale state or changed foreground.");
    }
    private static void Inactive()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        var wakes = 0;
        using var input = new GlobalNavigationInput(() => Interlocked.Increment(ref wakes));
        Require(!input.State.Active && wakes > 0, "Input started in a capturing mode or did not notify UI.");
        input.SetAvailable(false); input.Toggle();
        Wait(() => input.State.Message.Contains("先勾选"));
        Require(!input.State.Active && input.Drain().Count == 0 && WindowEventListener.ForegroundWindow == foreground,
            "Unavailable input captured a gesture or changed foreground.");
    }
    private static void ConflictAndRelease()
    {
        var first = new GlobalNavigationInput(() => { });
        try
        {
            NavigationHotkey? chosen = null;
            foreach (var key in new uint[] { 0x79, 0x78, 0x77, 0x76 })
            {
                var candidate = new NavigationHotkey(7, key, "BFW native input check " + key);
                first.ChangeHotkey(candidate); Wait(() => first.State.Hotkey == candidate);
                if (first.State.Registered) { chosen = candidate; break; }
            }
            Require(chosen is not null, "No unused test shortcut; no active input was captured. " + first.State.Message);
            using var second = new GlobalNavigationInput(() => { });
            second.ChangeHotkey(chosen!); Wait(() => second.State.Hotkey == chosen);
            Require(!second.State.Registered && second.State.Message.Contains("注册失败") && !second.State.Active, "Conflict did not fail visibly.");
            first.Dispose(); second.ChangeHotkey(chosen!); Wait(() => second.State.Registered);
            Require(!second.State.Active, "Rebinding unexpectedly entered navigation.");
        }
        finally { first.Dispose(); }
    }
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(5);
        Require(condition(), "Navigation worker did not reach the expected state.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
