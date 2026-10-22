using BorderlessFlatWindow.Application.Management;

internal static class TrayChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Tray distinguishes unstarted, running, paused and retained recovery states", States);
        yield return ("Running journal ownership takes precedence over locked-file diagnostics", OwnedLease);
        yield return ("Modal/native busy and independent recovery gate tray actions", Busy);
        yield return ("Overview returning caption tracks preparation as well as a visible host", Overview);
        yield return ("Tray text stays bounded and contains state/count only at extreme counts", Bounded);
    }
    private static void States()
    {
        var idle = TrayPresentation.Create(CanvasSessionStatus.Idle, 0);
        Require(idle.CanConfigure && idle.CanOpen && !idle.CanPause && !idle.CanOverview && idle.Caption.Contains("尚未开始"), "Idle tray invited invalid management.");
        var running = TrayPresentation.Create(CanvasSessionStatus.Running, 3);
        Require(running.CanPause && running.CanOverview && running.Caption.Contains("3 个窗口") && running.ExitCaption.Contains("恢复"), "Running tray lost recovery controls.");
        var paused = TrayPresentation.Create(CanvasSessionStatus.Paused, 3);
        var failed = TrayPresentation.Create(CanvasSessionStatus.RecoveryRequired, 3);
        var inherited = TrayPresentation.Create(CanvasSessionStatus.Idle, 0, recoveryAttention: true);
        Require(paused.Caption.Contains("已暂停") && !paused.CanPause && failed.Caption.Contains("待恢复")
            && inherited.Caption.Contains("待恢复") && failed.ExitCaption.Contains("保留恢复记录"), "Recovery state was presented as success.");
    }
    private static void OwnedLease()
    {
        Require(TrayPresentation.Create(CanvasSessionStatus.Running, 2, recoveryAttention: true)
            == TrayPresentation.Create(CanvasSessionStatus.Running, 2), "Own journal lease was treated as failed recovery.");
    }
    private static void Busy()
    {
        foreach (var status in Enum.GetValues<CanvasSessionStatus>())
        {
            var modal = TrayPresentation.Create(status, 1, blocked: true);
            Require(!modal.CanOpen && !modal.CanPause && !modal.CanOverview && !modal.CanConfigure && !modal.CanExit, "Modal/busy owner remained actionable.");
            var recovery = TrayPresentation.Create(status, 1, recovering: true);
            Require(recovery.CanOpen && !recovery.CanPause && !recovery.CanOverview && !recovery.CanConfigure && !recovery.CanExit
                && recovery.Caption.Contains("正在恢复"), "Independent recovery could race management or exit.");
        }
    }
    private static void Overview()
    {
        var pending = TrayPresentation.Create(CanvasSessionStatus.Running, 1, overviewPending: true);
        Require(pending.CanOverview && pending.OverviewCaption == "返回画布"
            && TrayPresentation.Create(CanvasSessionStatus.Running, 1).OverviewCaption == "空间总览", "Overview lifecycle caption is stale.");
    }
    private static void Bounded()
    {
        foreach (var status in Enum.GetValues<CanvasSessionStatus>())
            foreach (var count in new[] { 0, 10000, int.MaxValue })
            {
                var first = TrayPresentation.Create(status, count);
                Require(first.Tooltip.Length <= 63 && !first.Tooltip.Contains('\n') && first == TrayPresentation.Create(status, count), "Tray tooltip exceeds shell bounds or is unstable.");
            }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
