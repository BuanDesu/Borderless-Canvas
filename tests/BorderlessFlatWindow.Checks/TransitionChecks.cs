using BorderlessFlatWindow.Core.Canvas;

internal static class TransitionChecks
{
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Overview Tab capture is restricted to its host or requested preparation, preserving modal and application Tab", () =>
        {
            Require(OverviewTogglePolicy.CapturesTab(true, false, 42, 42, false, false), "Host Tab was lost.");
            Require(OverviewTogglePolicy.CapturesTab(true, true, 0, 99, false, false), "Pending toggle was lost.");
            Require(!OverviewTogglePolicy.CapturesTab(true, false, 42, 43, false, false), "Modal/application Tab intercepted.");
            Require(!OverviewTogglePolicy.CapturesTab(true, false, 0, 0, false, false), "Missing host captured Tab.");
            Require(!OverviewTogglePolicy.CapturesTab(true, false, 42, 42, true, false), "Ctrl+Tab intercepted.");
            Require(!OverviewTogglePolicy.CapturesTab(true, false, 42, 42, false, true), "Menu Tab intercepted.");
        });
        yield return ("An overview reversal continues from its current position and finishes at the new target", () =>
        {
            var motion = new ReversibleTransition(); motion.Retarget(true, 0, 220);
            var before = motion.Value(80); var plan = motion.Retarget(false, 80, 180);
            Require(before == plan.From && motion.Value(80) == before, "Reversal jumped.");
            Require(motion.Value(80 + plan.Duration) == 0 && !motion.TargetOpen, "Close missed target.");
        });
        yield return ("Rapid alternating overview requests preserve parity without completing stale targets", () =>
        {
            for (var count = 1; count <= 51; count++)
            {
                var motion = new ReversibleTransition();
                for (var i = 0; i < count; i++)
                {
                    var now = i * 11.0; var value = motion.Value(now);
                    motion.Retarget(i % 2 == 0, now, 220);
                    Require(motion.Value(now) == value, "Rapid retarget jumped.");
                }
                Require(motion.Value(2000) == (count % 2) && motion.TargetOpen == (count % 2 == 1), "Lost toggle parity.");
            }
        });
        yield return ("A partially closed overview can reopen without a position or direction discontinuity", () =>
        {
            var motion = new ReversibleTransition(); motion.Reset(1); motion.Retarget(false, 0, 180);
            var midway = motion.Value(60); motion.Retarget(true, 60, 220);
            Require(motion.Value(60) == midway && motion.Value(70) > midway && motion.Value(400) == 1, "Reopen failed.");
        });
        yield return ("Repeated requests at an endpoint remain stable and invalid motion parameters are rejected", () =>
        {
            var motion = new ReversibleTransition(); motion.Retarget(false, 0, 100);
            Require(motion.Value(0) == 0, "Closed endpoint moved.");
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d, 0d })
            {
                try { motion.Retarget(true, 0, invalid); throw new Exception("Invalid duration accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
        });
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
