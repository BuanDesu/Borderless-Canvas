namespace BorderlessFlatWindow.Application.Management;

/// <summary>Shell text contains only state and counts, never window identities or camera coordinates.</summary>
public sealed record TrayPresentation(string Caption, string Tooltip, string OverviewCaption, string ExitCaption,
    bool CanOpen, bool CanOverview, bool CanPause, bool CanConfigure, bool CanExit)
{
    public static TrayPresentation Create(CanvasSessionStatus status, int count, bool overviewPending = false,
        bool blocked = false, bool recoveryAttention = false, bool recovering = false)
    {
        if (!Enum.IsDefined(status) || count < 0) throw new ArgumentOutOfRangeException(nameof(status));
        var running = status == CanvasSessionStatus.Running;
        // A running manager owns the journal lease; a locked-file diagnostic does not imply failed recovery.
        var pending = !running && (status == CanvasSessionStatus.RecoveryRequired || recoveryAttention);
        var caption = recovering ? "正在恢复窗口" : running ? $"运行中 · {count} 个窗口"
            : pending ? "待恢复 · 请打开恢复中心" : status == CanvasSessionStatus.Paused ? "已暂停 · 窗口已恢复" : "就绪 · 尚未开始画布";
        var available = !blocked && !recovering;
        return new(caption, $"Borderless · {caption}", overviewPending ? "返回画布" : "空间总览",
            pending ? "退出（保留恢复记录）" : running ? "退出并恢复窗口" : "退出",
            !blocked, available && running, available && running, available, available);
    }
}
