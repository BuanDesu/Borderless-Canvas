using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Management;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private readonly RuntimeAdmissionTracker admissionTracker = new();
    private readonly System.Windows.Forms.Timer admissionTimer = new() { Interval = 220 };
    private readonly ChromeButton addWindows = new() { Text = "加入勾选窗口", AutoSize = true, Enabled = false };
    private readonly CheckBox automaticAdmission = new() { Text = "自动接入新窗口", AutoSize = true, Checked = true };
    private readonly HashSet<nint> admissionDestroyedHints = [];
    private bool admissionIdentityGap, renderingCandidates;
    private void InitializeAdmissionControls(FlowLayoutPanel toolbar)
    {
        toolbar.Controls.AddRange([addWindows, automaticAdmission]);
        addWindows.Click += (_, _) => AddSelectedWindows();
        automaticAdmission.CheckedChanged += (_, _) =>
        {
            admissionTimer.Stop();
            if (!automaticAdmission.Checked || session?.IsRunning != true) return;
            if (reading || session.IsBusy) { automaticAdmission.Checked = false; return; }
            try
            {
                reading = true; EndNavigationForEditing(); lastSnapshot = catalog.Inspect();
                if (IsDisposed || Disposing) return;
                session.Observe(lastSnapshot, BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
                ResetAutomaticAdmission(lastSnapshot);
            }
            catch (Exception exception) when (Expected(exception)) { automaticAdmission.Checked = false; notice = $"未开启自动接入：{exception.Message}"; }
            finally { reading = false; if (!IsDisposed && !Disposing) RefreshState(); }
        };
        admissionTimer.Tick += (_, _) => AddAutomaticWindows();
    }
    internal void RecordWindowChange(WindowChange change)
    {
        // Called by the WinEvent subscriber: enqueue only. No catalog, COM, placement or file work here.
        if (change.Kind != WindowChangeKind.Destroyed || change.Handle == 0) return;
        if (admissionDestroyedHints.Count >= 2048) { admissionIdentityGap = true; return; }
        admissionDestroyedHints.Add(change.Handle);
    }
    private void ResetAutomaticAdmission(WindowCatalogSnapshot snapshot)
    { admissionTracker.Reset(snapshot, includeExisting: true); admissionDestroyedHints.Clear(); admissionIdentityGap = false; }
    private void DrainAdmissionHints(WindowCatalogSnapshot snapshot)
    {
        if (admissionIdentityGap) { ResetAutomaticAdmission(snapshot); return; }
        var destroyed = admissionDestroyedHints.ToArray(); admissionDestroyedHints.Clear();
        foreach (var handle in destroyed) admissionTracker.ForgetDestroyed(handle);
    }
    private void OnCandidateItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (renderingCandidates) return;
        if (candidates.Items[e.Index] is Candidate item)
            e.NewValue = item.Managed && session?.IsRunning == true ? CheckState.Checked : !item.Supported ? CheckState.Unchecked : e.NewValue;
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) UpdateAdmissionState(); });
    }
    private void RenderCandidates(WindowCatalogSnapshot snapshot)
    {
        if (layout is null) return;
        var running = session?.IsRunning == true;
        var managedIds = session?.ManagedIds.ToHashSet() ?? [];
        var selected = candidates.CheckedItems.OfType<Candidate>().Select(item => item.Id).ToHashSet();
        var previous = candidates.SelectedItem is Candidate selectedItem ? selectedItem.Id : (Guid?)null;
        var next = snapshot.Entries.Where(entry => entry.Admission.IsAdmitted && entry.Id is not null).Select(entry =>
        {
            var reason = ManagedCanvasSession.Unsupported(entry, layout); var id = entry.Id!.Value;
            var owned = running && managedIds.Contains(id);
            var pinned = entry.Observation.Identity is { } identity && fixedWindows.Contains(identity);
            var denied = entry.Observation.Identity is { } current && session?.SkippedUnavailable.Contains(current) == true;
            var prefix = pinned ? "便签 · " : owned ? "画布 · " : denied ? "权限不足 · " : reason is not null ? "暂不可用 · " : "待接入 · ";
            return new Candidate(id, prefix + entry.Observation.Title + (reason is null ? "" : " · " + reason), reason is null && !pinned && !denied, owned);
        }).ToArray();
        if (candidates.Items.OfType<Candidate>().SequenceEqual(next)) return;
        renderingCandidates = true; candidates.BeginUpdate();
        try
        {
            candidates.Items.Clear();
            foreach (var item in next)
            {
                var index = candidates.Items.Add(item, item.Managed || selected.Contains(item.Id) && item.Supported);
                if (item.Id == previous) candidates.SelectedIndex = index;
            }
        }
        finally { candidates.EndUpdate(); renderingCandidates = false; }
    }
    private void UpdateAdmissionState()
    {
        if (WorkspaceRestoring) { admissionTimer.Stop(); addWindows.Enabled = false; return; }
        var canAdd = session?.IsRunning == true && !reading && !session.IsBusy && !session.HasPending && overview is null && !openingOverview;
        addWindows.Visible = session?.IsRunning == true;
        addWindows.Enabled = canAdd && candidates.CheckedItems.OfType<Candidate>().Any(item => !item.Managed && item.Supported);
        if (!automaticAdmission.Checked || session?.IsRunning != true || layout is null || lastSnapshot is null)
        { admissionTimer.Stop(); return; }
        admissionTracker.Observe(lastSnapshot, session.ManagedIds.ToHashSet(), layout, DateTimeOffset.UtcNow, AutomaticExclusions);
        if (admissionTracker.PendingHandles.Count > 0) admissionTimer.Start(); else admissionTimer.Stop();
    }
    private void AddSelectedWindows()
    {
        if (session?.IsRunning != true || reading || session.IsBusy || session.HasPending || overview is not null) return;
        var ids = candidates.CheckedItems.OfType<Candidate>().Where(item => !item.Managed && item.Supported).Select(item => item.Id).ToHashSet();
        if (ids.Count == 0) return;
        try
        {
            EndNavigationForEditing(); reading = true; lastSnapshot = catalog.Inspect(); DrainAdmissionHints(lastSnapshot);
            if (IsDisposed || Disposing) return;
            session.Observe(lastSnapshot, BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            if (!session.IsRunning) { notice = session.Message; return; }
            var selection = lastSnapshot.Entries.Where(entry => entry.Id is { } id && ids.Contains(id)).ToArray();
            if (selection.Length != ids.Count) { notice = "新窗口已经关闭或身份改变，请重新选择。"; return; }
            session.AddWindows(selection, DateTimeOffset.UtcNow); notice = null;
        }
        catch (Exception exception) when (Expected(exception)) { session.Pause($"接入异常，已暂停恢复：{exception.Message}"); notice = session.Message; }
        finally { reading = false; if (!IsDisposed && !Disposing) RefreshState(); }
    }
    private void AddAutomaticWindows()
    {
        if (WorkspaceRestoring) { admissionTimer.Stop(); return; }
        if (!automaticAdmission.Checked || session?.IsRunning != true) { admissionTimer.Stop(); return; }
        if (reading || editingSettings || desktopTransitionPending || session.IsBusy || session.HasPending || overview is not null || openingOverview
            || gestures?.HasGesture == true || navigationState is { Crosshair: true }) return;
        admissionTimer.Stop();
        try
        {
            reading = true;
            foreach (var handle in admissionTracker.PendingHandles) catalog.RefreshWindow(handle);
            lastSnapshot = catalog.Current; DrainAdmissionHints(lastSnapshot);
            if (IsDisposed || Disposing || !automaticAdmission.Checked) return;
            session.Observe(lastSnapshot, BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow, DateTimeOffset.UtcNow);
            if (!session.IsRunning || layout is null) return;
            var now = DateTimeOffset.UtcNow;
            UpdateFixedLaunches(lastSnapshot);
            admissionTracker.Observe(lastSnapshot, session.ManagedIds.ToHashSet(), layout, now, AutomaticExclusions);
            var ready = admissionTracker.Ready(now);
            if (ready.Count > 0) { session.AddWindows(ready, now, skipUnavailable: true); notice = null; }
        }
        catch (Exception exception) when (Expected(exception)) { session.Pause($"自动接入异常，已暂停恢复：{exception.Message}"); notice = session.Message; }
        finally { reading = false; if (!IsDisposed && !Disposing) RefreshState(); }
    }
}
