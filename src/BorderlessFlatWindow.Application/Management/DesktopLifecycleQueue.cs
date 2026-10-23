namespace BorderlessFlatWindow.Application.Management;

public enum DesktopLifecycleSignal { DisplayChanged, DpiChanged, SessionLocked, SessionUnlocked, SessionDisconnected, SessionConnected, PowerSuspending, PowerResumed }

/// <summary>Single UI-thread queue; a completion can acknowledge only its own batch.</summary>
public sealed class DesktopLifecycleQueue
{
    private readonly HashSet<DesktopLifecycleSignal> pending = [];
    private DesktopLifecycleWork? active;
    public bool HasWork => active is not null || pending.Count > 0;
    public void Request(DesktopLifecycleSignal signal)
    {
        if (!Enum.IsDefined(signal)) throw new ArgumentOutOfRangeException(nameof(signal));
        pending.Add(signal);
    }
    public DesktopLifecycleWork? Begin()
    {
        if (active is not null || pending.Count == 0) return null;
        active = new(Array.AsReadOnly(pending.Order().ToArray())); pending.Clear(); return active;
    }
    public bool Complete(DesktopLifecycleWork work)
    {
        if (active is null || !ReferenceEquals(active, work)) return false;
        active = null; return true;
    }
}

public sealed class DesktopLifecycleWork
{
    internal DesktopLifecycleWork(IReadOnlyList<DesktopLifecycleSignal> signals) => Signals = signals;
    public IReadOnlyList<DesktopLifecycleSignal> Signals { get; }
    public bool RequiresRefresh => Signals.Any(signal => signal is DesktopLifecycleSignal.DisplayChanged or DesktopLifecycleSignal.DpiChanged
        or DesktopLifecycleSignal.SessionUnlocked or DesktopLifecycleSignal.SessionConnected or DesktopLifecycleSignal.PowerResumed);
    public string PauseReason => Signals.Any(signal => signal is DesktopLifecycleSignal.PowerSuspending or DesktopLifecycleSignal.PowerResumed)
        ? "电源状态已变化，画布已暂停；确认设备状态后可重新开始。"
        : Signals.Any(signal => signal is DesktopLifecycleSignal.SessionLocked or DesktopLifecycleSignal.SessionUnlocked or DesktopLifecycleSignal.SessionConnected or DesktopLifecycleSignal.SessionDisconnected)
            ? "会话状态已变化，画布已暂停；确认设备状态后可重新开始。"
            : "显示配置已变化，画布已暂停；检查显示器后可重新开始。";
}
