using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Bookmarks;

public sealed record WorkspaceCandidate(Guid Id, NativeWindowIdentity Identity, string? Executable, string WindowClass,
    bool Minimized = false, bool Eligible = true, string Label = "", bool Retryable = false);
public enum WorkspacePlacement { Pending, Confirmed, Failed }
public interface IWorkspaceRestorePort
{
    bool Available => true;
    void Launch(WorkspaceLaunch launch);
    bool RestoreMinimized(WorkspaceCandidate candidate);
    bool Apply(WorkspaceCandidate candidate, RectD bounds);
    WorkspacePlacement Check(WorkspaceCandidate candidate);
    void CancelPrepared();
}
public sealed record WorkspaceRestoreResult(Guid Member, string Label, string State, bool Finished, bool Success);

/// <summary>User-triggered, bounded coordinator. It never enumerates the desktop or runs while idle.</summary>
public sealed class WorkspaceRestorer(IWorkspaceRestorePort port)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private sealed class Item(WorkspaceMember member)
    {
        internal readonly WorkspaceMember Member = member;
        internal WorkspaceCandidate? Target;
        internal bool Launched, Restoring, Submitted, Finished, Success;
        internal string State = "等待匹配";
    }
    private Item[] items = [];
    private readonly Dictionary<string, DateTimeOffset> launches = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<NativeWindowIdentity> claimed = [];
    private Guid sourceSession, currentSession;
    private DateTimeOffset deadline;
    private Func<WorkspaceMember, RectD>? project;
    private IReadOnlyDictionary<Guid, Guid> bindings = new Dictionary<Guid, Guid>();
    private int generation;
    private bool active;
    public Guid? WorkspaceId { get; private set; }
    public bool Active => active;
    public IReadOnlyList<WorkspaceRestoreResult> Results => items.Select(i => new WorkspaceRestoreResult(i.Member.Id, i.Member.Label, i.State, i.Finished, i.Success)).ToArray();
    public IReadOnlySet<string> RequestedExecutables => items.Where(i => i.Member.Executable is not null).Select(i => i.Member.Executable!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool Begin(LocationBookmark bookmark, Guid session, DateTimeOffset now, Func<WorkspaceMember, RectD> projection, IReadOnlyDictionary<Guid, Guid>? selected = null)
    {
        if (Active && WorkspaceId == bookmark.Id) return false;
        Cancel();
        bookmark.Layout?.Validate();
        sourceSession = bookmark.Layout?.SourceSession ?? Guid.Empty; currentSession = session; WorkspaceId = bookmark.Id;
        items = bookmark.Layout?.Windows.Select(m => new Item(m)).ToArray() ?? [];
        active = items.Length > 0;
        project = projection; bindings = selected ?? new Dictionary<Guid, Guid>(); deadline = now + Timeout;
        claimed.Clear();
        foreach (var key in launches.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) launches.Remove(key);
        return true;
    }
    public void Advance(IReadOnlyList<WorkspaceCandidate> candidates, DateTimeOffset now)
    {
        if (!Active) return;
        var current = generation;
        foreach (var item in items.Where(i => !i.Finished))
        {
            if (current != generation) return;
            if (!port.Available) { Cancel(); return; }
            if (now >= deadline) { Finish(item, false, "等待超时；可检查启动项后重试"); continue; }
            try { Advance(item, candidates, now, current); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            { if (current == generation) Finish(item, false, "未完成；请检查软件、权限和启动项"); }
        }
        if (current != generation) return;
        active = items.Any(i => !i.Finished);
        if (!active) port.CancelPrepared();
    }
    private void Advance(Item item, IReadOnlyList<WorkspaceCandidate> candidates, DateTimeOffset now, int current)
    {
        var member = item.Member;
        if (item.Target is { } retained)
        {
            var live = candidates.FirstOrDefault(c => c.Id == retained.Id && c.Identity == retained.Identity);
            if (live is null) { Finish(item, false, "目标已关闭或身份改变；未接管替代窗口"); return; }
            if (!live.Eligible)
            {
                if (live.Retryable) { item.State = "等待窗口就绪"; return; }
                Finish(item, false, "窗口当前不可管理或已固定为便签"); return;
            }
            if (item.Submitted)
            {
                var state = port.Check(live);
                if (current != generation) return;
                if (state != WorkspacePlacement.Pending) Finish(item, state == WorkspacePlacement.Confirmed,
                    state == WorkspacePlacement.Confirmed ? "布局已核验" : "布局未确认；请检查恢复中心");
                return;
            }
            if (live.Minimized)
            {
                if (!item.Restoring)
                {
                    var restored = port.RestoreMinimized(live);
                    if (current != generation) return;
                    if (!restored) { Finish(item, false, "无法还原最小化窗口"); return; }
                    item.Restoring = true;
                }
                item.State = "等待窗口还原"; return;
            }
            var accepted = port.Apply(live, project!(member));
            if (current != generation) return;
            if (!accepted) { Finish(item, false, "未能复原布局；请检查恢复中心"); return; }
            item.Submitted = true; item.State = "正在核验布局"; return;
        }
        var available = candidates.Where(c => !claimed.Contains(c.Identity)).ToArray();
        WorkspaceCandidate? match = null;
        if (bindings.TryGetValue(member.Id, out var chosen))
        {
            match = available.FirstOrDefault(c => c.Id == chosen && string.Equals(c.Executable, member.Executable, StringComparison.OrdinalIgnoreCase));
            // An explicit selection is narrower than an application match. Its
            // disappearance must never authorize moving a sibling or launching.
            if (match is null) { Finish(item, false, "明确选择的窗口已关闭或不可用；请重新选择当前窗口"); return; }
        }
        else if (sourceSession == currentSession) match = available.FirstOrDefault(c => c.Id == member.SourceWindow && SameApp(c, member));
        var app = available.Where(c => member.Executable is not null && string.Equals(c.Executable, member.Executable, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (match is null && app.Length == 1 && !app[0].Eligible && app[0].Retryable) { item.State = "等待已有窗口就绪"; return; }
        if (match is null && app.Length == 1 && items.Count(i => string.Equals(i.Member.Executable, member.Executable, StringComparison.OrdinalIgnoreCase)) == 1
            && (member.Launch?.Arguments.Count is null or 0 || item.Launched) && SameApp(app[0], member)) match = app[0];
        if (match is not null)
        {
            item.Target = match; claimed.Add(match.Identity); item.State = "已复用窗口";
            Advance(item, candidates, now, current); return;
        }
        if (app.Length > 0)
        {
            // An existing instance is not permission to guess its document or start another copy.
            Finish(item, false, "同一程序窗口不明确；请在成员中选择当前窗口"); return;
        }
        if (member.Executable is null || member.Launch is null) { Finish(item, false, "没有可信启动项；请在成员中调整"); return; }
        if (available.Any(c => c.Executable is null && ClassFamily(c.WindowClass) == ClassFamily(member.WindowClass)))
        { Finish(item, false, "已有窗口无法核验程序；请检查权限并明确选择窗口"); return; }
        if (items.Count(i => string.Equals(i.Member.Executable, member.Executable, StringComparison.OrdinalIgnoreCase)) > 1)
        { Finish(item, false, "同程序多窗口缺少可核验文档上下文；请打开并选择当前窗口"); return; }
        if (!item.Launched)
        {
            if (launches.TryGetValue(member.Executable, out var until) && until > now)
            { item.State = "等待先前启动；未重复启动"; return; }
            // Record before calling the port: an exception must not cause a launch storm.
            item.Launched = true; launches[member.Executable] = now + Timeout;
            member.Launch.Validate(); port.Launch(member.Launch);
            if (current == generation) item.State = "已启动，等待可用窗口";
        }
        else item.State = "等待软件窗口；可取消";
    }
    private static bool SameApp(WorkspaceCandidate candidate, WorkspaceMember member) =>
        (member.Executable is null || string.Equals(candidate.Executable, member.Executable, StringComparison.OrdinalIgnoreCase))
        && string.Equals(ClassFamily(candidate.WindowClass), ClassFamily(member.WindowClass), StringComparison.Ordinal);
    private static string ClassFamily(string value) => value.StartsWith("WindowsForms10.Window.", StringComparison.Ordinal) ? "WindowsForms10.Window"
        : value.StartsWith("HwndWrapper[", StringComparison.Ordinal) ? "HwndWrapper" : value;
    private static void Finish(Item item, bool success, string state) { item.Finished = true; item.Success = success; item.State = state; }
    public void Cancel()
    {
        generation++;
        foreach (var item in items.Where(i => !i.Finished)) Finish(item, false, "已取消；已启动的软件保持打开");
        active = false;
        port.CancelPrepared();
    }
}
