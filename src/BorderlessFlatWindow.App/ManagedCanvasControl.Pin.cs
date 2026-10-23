using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private readonly HashSet<NativeWindowIdentity> fixedWindows = [];
    private readonly HashSet<NativeWindowIdentity> checkedPinMarkers = [];
    private readonly WindowPinMenus pinMenus = new();
    private (NativeWindowIdentity Identity, bool Pinned, Guid Id)[] lastPinMenuWindows = [];
    private WindowPinTarget[] lastPinTargets = [];
    private bool? lastPinAvailable;
    private NativeWindowIdentity? lastApplicationFocus;
    private sealed record PinRequest(NativeWindowIdentity Identity, Guid CatalogId);
    private PinRequest? pendingPin;
    private bool pinScheduled;
    private bool pinDisposed;
    internal Control WindowScopeControls => selector;
    private void InitializePinControls()
    {
        var button = new ChromeButton { Text = "切换所选便签", AutoSize = true, Dock = DockStyle.Bottom };
        button.Click += (_, _) =>
        {
            if (candidates.SelectedItem is Candidate selected && lastSnapshot?.Entries.FirstOrDefault(entry => entry.Id == selected.Id)?.Observation.Identity is { } identity)
                RequestPinToggle(identity.Handle, identity);
        };
        selector.Controls.Add(button);
    }
    private void ToggleFocusedPin()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        var entry = lastSnapshot?.Entries.FirstOrDefault(item => item.Observation.Handle == foreground && item.Admission.IsAdmitted);
        var identity = entry?.Observation.Identity ?? lastApplicationFocus;
        if (identity is { } target) RequestPinToggle(target.Handle, target);
    }
    private void RequestPinToggle(nint handle, NativeWindowIdentity? expected = null, Guid targetId = default)
    {
        if (!CanUsePanelCommands || HasOverviewWork || externalRecoveryRunning || layout is null || handle == 0) return;
        try
        {
            catalog.RefreshWindow(handle); lastSnapshot = catalog.Current;
            var entry = lastSnapshot.Entries.FirstOrDefault(item => item.Observation.Handle == handle);
            if (entry?.Observation.Identity is not { } identity || entry.Id is not { } id || !entry.Admission.IsAdmitted
                || expected is { } requested && requested != identity || targetId != Guid.Empty && targetId != id)
            { notice = "窗口已关闭、身份改变或不支持便签。"; RefreshState(); return; }
            pendingPin = new(identity, id); EndNavigationForEditing();
            session?.SuspendNavigation(true); session?.SuspendNavigation(false);
            SchedulePinToggle();
        }
        catch (Exception error) when (Expected(error)) { notice = "未切换便签：" + error.Message; RefreshState(); }
    }
    private void SchedulePinToggle()
    {
        if (pendingPin is null || pinScheduled || !IsHandleCreated || IsDisposed || Disposing || !CanUsePanelCommands || HasOverviewWork
            || externalRecoveryRunning || session?.HasPending == true || session?.HasCameraTransition == true) return;
        pinScheduled = true;
        BeginInvoke(() =>
        {
            pinScheduled = false;
            if (IsDisposed || Disposing || pendingPin is not { } request || !CanUsePanelCommands || HasOverviewWork || externalRecoveryRunning || session?.HasPending == true) return;
            var identity = request.Identity;
            pendingPin = null;
            try
            {
                reading = true; catalog.RefreshWindow(identity.Handle); lastSnapshot = catalog.Current;
                var entry = lastSnapshot.Entries.FirstOrDefault(item => item.Observation.Handle == identity.Handle);
                if (entry?.Observation.Identity != identity || entry.Id != request.CatalogId || !entry.Admission.IsAdmitted || !NativeWindowIdentityGuard.IsCurrent(identity))
                { notice = "窗口身份或状态已变化，未切换便签。"; return; }
                if (fixedWindows.Contains(identity))
                {
                    WindowPinMarker.Set(identity, false);
                    if (session?.IsRunning == true && (!session.AddWindows([entry], DateTimeOffset.UtcNow, preservePhysicalPosition: true)))
                    { WindowPinMarker.Set(identity, true); notice = session.Message; return; }
                    fixedWindows.Remove(identity); notice = "已取消便签，从当前位置返回画布。";
                }
                else
                {
                    WindowPinMarker.Set(identity, true);
                    if (entry.Id is { } id && session?.IsRunning == true && session.ManagedIds.Contains(id) && !session.DetachWindow(id))
                    { WindowPinMarker.Set(identity, false); notice = session.Message; return; }
                    fixedWindows.Add(identity); notice = "已固定为便签，保留当前位置与层级。";
                }
            }
            catch (Exception error) when (Expected(error)) { notice = "便签切换未完成：" + error.Message; }
            finally { reading = false; if (!IsDisposed && !Disposing) RefreshState(); }
        });
    }
    private void PruneFixedWindows(WindowCatalogSnapshot snapshot)
    {
        var byHandle = snapshot.Entries.ToDictionary(entry => entry.Observation.Handle);
        foreach (var entry in snapshot.Entries)
            if (entry.Admission.IsAdmitted && entry.Observation.Identity is { IsValid: true } identity && checkedPinMarkers.Add(identity) && WindowPinMarker.IsPinned(identity)) fixedWindows.Add(identity);
        fixedWindows.RemoveWhere(identity => byHandle.TryGetValue(identity.Handle, out var entry)
            ? !entry.Observation.IsAlive || entry.Observation.Identity is { } current && current != identity
            : !NativeWindowIdentityGuard.IsCurrent(identity));
        checkedPinMarkers.RemoveWhere(identity => !byHandle.ContainsKey(identity.Handle));
        var foreground = WindowEventListener.ForegroundWindow;
        if (byHandle.TryGetValue(foreground, out var active) && active.Admission.IsAdmitted && active.Observation.Identity is { } focused) lastApplicationFocus = focused;
    }
    private void RefreshPinMenuTargets()
    {
        if (pinDisposed) return;
        var enabled = globalInput is not null && CanUsePanelCommands && !HasOverviewWork && !externalRecoveryRunning && layout is not null
            && session?.Status != CanvasSessionStatus.RecoveryRequired && !IsDisposed && !Disposing;
        var windows = enabled ? lastSnapshot?.Entries.Where(entry => entry.Admission.IsAdmitted && entry.Observation.Identity is { IsValid: true })
            .Select(entry => (Identity: entry.Observation.Identity!.Value, Pinned: fixedWindows.Contains(entry.Observation.Identity.Value), Id: entry.Id!.Value)).OrderBy(item => item.Identity.Handle).ToArray() ?? [] : [];
        if (!lastPinMenuWindows.SequenceEqual(windows))
        {
            lastPinMenuWindows = windows;
            lastPinTargets = pinMenus.Synchronize(windows.Select(item => (item.Identity, item.Pinned))).Select(target => target with
                { CatalogId = windows.First(item => item.Identity == target.Identity).Id }).ToArray();
            globalInput?.ConfigurePinTargets(lastPinTargets, enabled); lastPinAvailable = enabled;
        }
        else if (lastPinAvailable != enabled || globalInput is not null && !pinTargetsConfigured)
        { globalInput?.ConfigurePinTargets(lastPinTargets, enabled); lastPinAvailable = enabled; }
        pinTargetsConfigured = globalInput is not null;
    }
    private bool pinTargetsConfigured;
    private void DisposePinControls() { pinDisposed = true; pendingPin = null; pinMenus.Dispose(); fixedLaunchTimer.Dispose(); }
}
