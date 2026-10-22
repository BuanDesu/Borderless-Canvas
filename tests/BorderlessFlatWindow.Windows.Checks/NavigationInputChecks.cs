using System.Diagnostics;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class NavigationInputChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Dedicated navigation input starts inactive and never activates a desktop window", Inactive);
        check("Global hotkey conflicts are visible and disposal releases the registration", ConflictAndRelease);
        check("Closing overview clears its navigation suspension message and wakes the UI", OverviewState);
        check("A delayed overview close on the input thread cannot clear a newer session", OverviewOwnership);
        check("Pausing availability clears pending overview input without enabling desktop capture", OverviewUnavailable);
        check("Initial navigation preferences register the selected shortcut directly and report conflicts without capture", InitialPreferences);
    }
    private static void InitialPreferences()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var occupied = IsolatedInput(() => { }); var selected = occupied.State.Hotkey;
        using (var conflict = new GlobalNavigationInput(() => { }, new(false, false, false, 600), selected))
            Require(conflict.State.Hotkey == selected && !conflict.State.Registered && !conflict.State.Active && conflict.Drain().Count == 0, "Startup conflict silently fell back or enabled navigation.");
        occupied.Dispose();
        using var loaded = new GlobalNavigationInput(() => { }, new(false, true, false, 330), selected);
        Require(loaded.State.Hotkey == selected && loaded.State.Registered && !loaded.State.Active && loaded.Drain().Count == 0
            && WindowEventListener.ForegroundWindow == foreground, "Initial saved shortcut was not registered directly or changed foreground.");
    }
    private static void OverviewUnavailable()
    {
        using var input = IsolatedInput(() => { });
        input.BeginOverview(); input.SetAvailable(false);
        Wait(() => input.State.Message.Contains("画布未运行"));
        input.Toggle(); Wait(() => input.State.Message.Contains("先勾选"));
        Require(!input.State.Active && input.Drain().Count == 0, "Unavailable overview retained input or entered navigation.");
    }
    private static void OverviewOwnership()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var input = IsolatedInput(() => { });
        var previous = input.BeginOverview();
        var current = input.BeginOverview();
        input.EndOverview(previous); input.SetOverviewHost(previous, 0);
        // Toggle is an ordered worker-thread barrier. Overview must still block navigation;
        // availability remains false, so this test never enables desktop input capture.
        input.Toggle(); Wait(() => input.State.Message.Contains("先关闭总览"));
        input.EndOverview(current); Wait(() => input.State.Message.Contains("总览已关闭"));
        Require(!input.State.Active && input.Drain().Count == 0 && WindowEventListener.ForegroundWindow == foreground,
            "Session callbacks changed foreground, enabled navigation, or queued an input.");
    }
    private static void OverviewState()
    {
        var wakes = 0; var foreground = WindowEventListener.ForegroundWindow;
        using var input = IsolatedInput(() => Interlocked.Increment(ref wakes));
        var session = input.BeginOverview(); Wait(() => input.State.Message.Contains("总览中"));
        input.Drain(); // A UI frame acknowledges the coalesced wake before the next state change.
        var before = Volatile.Read(ref wakes);
        input.EndOverview(session); Wait(() => input.State.Message.Contains("总览已关闭") && Volatile.Read(ref wakes) > before);
        Require(!input.State.Active && Volatile.Read(ref wakes) > before && foreground == WindowEventListener.ForegroundWindow,
            "Overview closure retained stale state or changed foreground.");
    }
    private static void Inactive()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        var wakes = 0;
        using var input = IsolatedInput(() => Interlocked.Increment(ref wakes));
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
    private static GlobalNavigationInput IsolatedInput(Action wake)
    {
        var input = new GlobalNavigationInput(wake);
        try
        {
            // A user's trial may own the default chord. State checks require an
            // actual registration, but never enable capture or send the shortcut.
            foreach (var key in new uint[] { 0x79, 0x78, 0x77, 0x76 })
            {
                var candidate = new NavigationHotkey(7, key, "BFW isolated state check " + key);
                input.ChangeHotkey(candidate); Wait(() => input.State.Hotkey == candidate);
                if (input.State.Registered) return input;
            }
            throw new InvalidOperationException("No unused fixture shortcut: " + input.State.Message);
        }
        catch { input.Dispose(); throw; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
