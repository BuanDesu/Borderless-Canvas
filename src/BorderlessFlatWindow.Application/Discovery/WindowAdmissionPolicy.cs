namespace BorderlessFlatWindow.Application.Discovery;

public enum WindowExclusionReason
{
    None, WindowDisappeared, OwnProcess, DesktopShell, NotVisible, ChildWindow,
    ToolWindow, OwnedWindow, Untitled, MetadataUnavailable, IdentityUnavailable,
    CloakingUnknown, Cloaked, VirtualDesktopUnknown, OtherVirtualDesktop, Minimized, InvalidBounds
}

public sealed record WindowAdmissionDecision(WindowExclusionReason Reason)
{
    public bool IsAdmitted => Reason == WindowExclusionReason.None;
    public string Description => Reason switch
    {
        WindowExclusionReason.None => "可纳入（尚未接管）",
        WindowExclusionReason.WindowDisappeared => "窗口已关闭或读取期间身份改变",
        WindowExclusionReason.OwnProcess => "管理器自身窗口",
        WindowExclusionReason.DesktopShell => "桌面、任务栏或系统浮层",
        WindowExclusionReason.NotVisible => "窗口不可见",
        WindowExclusionReason.ChildWindow => "子窗口不单独纳入",
        WindowExclusionReason.ToolWindow => "工具窗口不单独纳入",
        WindowExclusionReason.OwnedWindow => "关联窗口随所属窗口处理",
        WindowExclusionReason.Untitled => "无标题窗口暂不纳入",
        WindowExclusionReason.MetadataUnavailable => "无法读取窗口元数据",
        WindowExclusionReason.IdentityUnavailable => "无法验证进程启动时间",
        WindowExclusionReason.CloakingUnknown => "无法验证 DWM 可见状态",
        WindowExclusionReason.Cloaked => "窗口被 DWM cloak",
        WindowExclusionReason.VirtualDesktopUnknown => "无法验证所属虚拟桌面",
        WindowExclusionReason.OtherVirtualDesktop => "其他 Windows 虚拟桌面的窗口",
        WindowExclusionReason.Minimized => "已最小化，暂不纳入",
        WindowExclusionReason.InvalidBounds => "窗口尺寸不可用",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason))
    };
}

/// <summary>Read-only eligibility, not authorization to move a window.</summary>
public sealed class WindowAdmissionPolicy(uint ownProcessId)
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "tooltips_class32", "#32768", "SysShadow"
    };

    public WindowAdmissionDecision Evaluate(WindowObservation window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var reason = window switch
        {
            { IsAlive: false } => WindowExclusionReason.WindowDisappeared,
            _ when window.Handle == 0 => WindowExclusionReason.WindowDisappeared,
            _ when ownProcessId != 0 && window.ProcessId == ownProcessId => WindowExclusionReason.OwnProcess,
            _ when ShellClasses.Contains(window.ClassName) => WindowExclusionReason.DesktopShell,
            { IsVisible: false } => WindowExclusionReason.NotVisible,
            { IsChildWindow: true } => WindowExclusionReason.ChildWindow,
            { IsToolWindow: true } => WindowExclusionReason.ToolWindow,
            _ when window.OwnerHandle != 0 => WindowExclusionReason.OwnedWindow,
            _ when string.IsNullOrWhiteSpace(window.Title) => WindowExclusionReason.Untitled,
            { MetadataAvailable: false } => WindowExclusionReason.MetadataUnavailable,
            _ when window.Identity is null => WindowExclusionReason.IdentityUnavailable,
            { IsCloaked: null } => WindowExclusionReason.CloakingUnknown,
            { IsCloaked: true } => WindowExclusionReason.Cloaked,
            { IsOnCurrentDesktop: null } => WindowExclusionReason.VirtualDesktopUnknown,
            { IsOnCurrentDesktop: false } => WindowExclusionReason.OtherVirtualDesktop,
            { IsMinimized: true } => WindowExclusionReason.Minimized,
            _ when window.Bounds is not { Size.IsPositive: true } => WindowExclusionReason.InvalidBounds,
            _ => WindowExclusionReason.None
        };
        return new(reason);
    }
}
