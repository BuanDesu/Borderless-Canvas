using BorderlessFlatWindow.Application.Management;

internal static class DesktopLifecycleChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Desktop lifecycle notifications coalesce without losing distinct causes", Coalescing);
        yield return ("Lifecycle work stays active until its exact batch completes", Ownership);
        yield return ("Reentrant desktop notifications survive an older completion", Reentrant);
        yield return ("Lock and suspend pause without a display read; reconnect and resume revalidate", RefreshPolicy);
        yield return ("Lifecycle signal snapshots cannot be mutated after dispatch", Immutable);
    }
    private static void Coalescing()
    {
        var queue = new DesktopLifecycleQueue();
        for (var i = 0; i < 10000; i++) queue.Request(i % 2 == 0 ? DesktopLifecycleSignal.DisplayChanged : DesktopLifecycleSignal.SessionLocked);
        var work = queue.Begin()!;
        Require(work.Signals.Count == 2 && queue.HasWork && work.PauseReason.Contains("会话"), "Event burst lost causes or grew unbounded.");
        queue.Complete(work); Require(!queue.HasWork && queue.Begin() is null, "Completed batch remained pending.");
    }
    private static void Ownership()
    {
        var queue = new DesktopLifecycleQueue(); var other = new DesktopLifecycleQueue();
        queue.Request(DesktopLifecycleSignal.PowerSuspending); other.Request(DesktopLifecycleSignal.PowerSuspending);
        var active = queue.Begin()!; var foreign = other.Begin()!;
        Require(queue.Begin() is null && !queue.Complete(foreign) && !queue.Complete(null!) && queue.HasWork, "A foreign/null completion ended active work.");
        Require(queue.Complete(active) && !queue.Complete(active), "Completion was not single-owner/single-use.");
        try { queue.Request((DesktopLifecycleSignal)(-1)); } catch (ArgumentOutOfRangeException) { Require(!queue.HasWork, "Invalid signal mutated queue."); return; }
        throw new InvalidOperationException("Invalid lifecycle signal accepted.");
    }
    private static void Reentrant()
    {
        var queue = new DesktopLifecycleQueue(); queue.Request(DesktopLifecycleSignal.SessionLocked); var first = queue.Begin()!;
        queue.Request(DesktopLifecycleSignal.SessionUnlocked); queue.Request(DesktopLifecycleSignal.PowerResumed);
        Require(queue.Begin() is null, "Nested consumer started during an active batch.");
        queue.Complete(first); var next = queue.Begin()!;
        Require(next.Signals.Count == 2 && next.RequiresRefresh && !queue.Complete(first) && queue.HasWork, "Late cleanup erased newer system work.");
        queue.Complete(next); Require(!queue.HasWork, "Reentrant batch leaked.");
    }
    private static void RefreshPolicy()
    {
        foreach (var signal in Enum.GetValues<DesktopLifecycleSignal>())
        {
            var queue = new DesktopLifecycleQueue(); queue.Request(signal); var work = queue.Begin()!;
            var pauseOnly = signal is DesktopLifecycleSignal.SessionLocked or DesktopLifecycleSignal.SessionDisconnected or DesktopLifecycleSignal.PowerSuspending;
            Require(work.RequiresRefresh != pauseOnly && !string.IsNullOrWhiteSpace(work.PauseReason), "Suspend/lock read the desktop or resume skipped revalidation.");
        }
    }
    private static void Immutable()
    {
        var queue = new DesktopLifecycleQueue(); queue.Request(DesktopLifecycleSignal.DpiChanged); var work = queue.Begin()!;
        queue.Request(DesktopLifecycleSignal.PowerResumed);
        try { ((IList<DesktopLifecycleSignal>)work.Signals)[0] = DesktopLifecycleSignal.PowerSuspending; }
        catch (NotSupportedException) { Require(work.Signals.Single() == DesktopLifecycleSignal.DpiChanged, "Published work changed."); return; }
        throw new InvalidOperationException("Published work was mutable.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
