using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private readonly FixedLaunchTracker fixedLaunches = new();
    private readonly Dictionary<(uint Process, long Created), string> launchExecutables = [];
    private readonly System.Windows.Forms.Timer fixedLaunchTimer = new() { Interval = 220 };
    internal void LaunchFixedShortcut(string path)
    {
        if (!CanUsePanelCommands || HasOverviewWork || externalRecoveryRunning) { notice = "请先完成当前操作，再以便签模式打开。"; RefreshState(); return; }
        try
        {
            EndNavigationForEditing();
            var shortcut = ExecutableShortcut.Read(path);
            lastSnapshot = catalog.Inspect();
            var request = fixedLaunches.Begin(shortcut.Executable, lastSnapshot, DateTimeOffset.UtcNow);
            try { shortcut.Launch(); } catch { fixedLaunches.Cancel(request); throw; }
            notice = "正在以便签模式打开，等待新的普通窗口。";
            fixedLaunchTimer.Start(); RefreshState();
        }
        catch (Exception error) when (Expected(error) || error is System.Runtime.InteropServices.COMException)
        { notice = "未以便签模式打开：" + error.Message; RefreshState(); }
    }
    private void InitializeFixedLaunchTimer()
    {
        fixedLaunchTimer.Tick += (_, _) =>
        {
            if (fixedLaunches.Count == 0) { fixedLaunchTimer.Stop(); return; }
            if (!CanUsePanelCommands || HasOverviewWork || externalRecoveryRunning) return;
            try
            {
                lastSnapshot = catalog.Current; UpdateFixedLaunches(lastSnapshot); RefreshState();
            }
            catch (Exception error) when (Expected(error)) { notice = "新窗口识别未完成：" + error.Message; }
        };
    }
    private void UpdateFixedLaunches(WindowCatalogSnapshot snapshot)
    {
        if (fixedLaunches.Count == 0) return;
        string? Executable(NativeWindowIdentity identity)
        {
            var key = (identity.ProcessId, identity.ProcessCreationFileTime);
            if (launchExecutables.TryGetValue(key, out var known)) return known;
            var path = NativeWindowIdentityGuard.Executable(identity);
            if (path is not null) { if (launchExecutables.Count >= 1024) launchExecutables.Clear(); launchExecutables[key] = path; }
            return path;
        }
        var matched = fixedLaunches.Observe(snapshot, DateTimeOffset.UtcNow, Executable, fixedWindows);
        var pinned = 0;
        foreach (var target in matched)
        {
            try { WindowPinMarker.Set(target, true); fixedWindows.Add(target); pinned++; }
            catch (Exception error) when (Expected(error))
            { if (NativeWindowIdentityGuard.IsCurrent(target)) fixedWindows.Add(target); notice = "新窗口保持独立，但便签标记未保存：" + error.Message; }
        }
        if (pinned > 0) notice = "新窗口已作为独立便签打开。";
        else if (fixedLaunches.ExpiredLastObservation > 0) notice = "未识别到新窗口；若程序复用了已有窗口，请用便签快捷键固定。";
        if (fixedLaunches.Count == 0) { fixedLaunchTimer.Stop(); launchExecutables.Clear(); }
    }
    private IReadOnlySet<NativeWindowIdentity> AutomaticExclusions => fixedWindows.Concat(fixedLaunches.HeldIdentities).Concat(session?.SkippedUnavailable ?? []).ToHashSet();
}
