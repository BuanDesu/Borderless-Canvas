using BorderlessFlatWindow.Core.Canvas;

internal static class OverviewInputChecks
{
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("A late overview close cannot disable a newer window-selection session", () =>
        {
            var input = new OverviewInputSession(); input.Begin(1); input.AttachHost(1, 101, 101);
            input.Begin(2); input.AttachHost(2, 202, 99);
            Require(!input.Close(1) && input.IsOpen && input.Generation == 2 && input.CapturesTab(99, false, false), "Old close disabled new preparation.");
            Require(input.CapturesTab(202, false, false) && input.Close(2) && !input.CapturesTab(202, false, false), "New session could not exit.");
        });
        yield return ("Shown preserves quick Tab until actual activation, then modal and application Tab pass through", () =>
        {
            var input = new OverviewInputSession(); input.Begin(1); input.AttachHost(1, 42, 99);
            Require(input.Preparing && input.CapturesTab(99, false, false), "Shown prematurely released Tab.");
            Require(input.CapturesTab(42, false, false) && !input.Preparing, "Activation did not finish preparation.");
            Require(!input.CapturesTab(43, false, false) && !input.CapturesTab(99, false, false), "Modal/application Tab intercepted after activation.");
        });
        yield return ("Delayed begin and host notifications cannot resurrect a closed or superseded overview", () =>
        {
            var input = new OverviewInputSession(); input.Begin(5); input.Close(5);
            Require(!input.Begin(5) && !input.AttachHost(5, 42, 42) && !input.IsOpen, "Closed session resurrected.");
            input.Begin(6); input.AttachHost(6, 60, 60);
            Require(!input.Begin(4) && !input.AttachHost(5, 50, 50) && input.Host == 60 && !input.Preparing, "Stale host replaced current ownership.");
        });
        yield return ("Modified and menu Tab remain native during overview preparation and repeated activations", () =>
        {
            var input = new OverviewInputSession(); input.Begin(1); input.AttachHost(1, 42, 99);
            Require(!input.CapturesTab(99, true, false) && !input.CapturesTab(99, false, true), "Preparation intercepted modifiers/menu.");
            input.ObserveForeground(42); input.AttachHost(1, 42, 99);
            Require(!input.Preparing && !input.CapturesTab(99, false, false), "A late Shown notification reopened capture.");
        });
        yield return ("Rapid overview sessions survive reordered cleanup and reject invalid generations", () =>
        {
            var input = new OverviewInputSession();
            for (var generation = 1; generation <= 64; generation++)
            {
                Require(input.Begin(generation), "Fresh generation rejected.");
                input.Close(generation - 1); input.AttachHost(generation - 1, 999, 999);
                Require(input.IsOpen && input.Preparing && input.Host == 0, "Previous session changed a new request.");
            }
            Require(input.Close(64) && !input.Close(64), "Close was not terminal/idempotent.");
            try { input.Begin(0); throw new Exception("Zero generation accepted."); }
            catch (ArgumentOutOfRangeException) { }
        });
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
