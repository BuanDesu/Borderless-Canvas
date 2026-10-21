using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>Full retained overview on a real WPF HWND; no desktop bitmap readback.</summary>
internal sealed class NativeOverviewPresenter : IOverviewPresenter
{
    private readonly SpatialOverviewSession model;
    private readonly SpatialOverviewView view;
    private readonly SpatialOverviewWindow surface;
    private readonly PixelRect bounds;
    private readonly BookmarkLibrary? bookmarks;
    private readonly bool animate, showWithoutActivation;
    private readonly Func<nint, IOverviewPreviewPort> createPreviewPort;
    private readonly ContextMenuStrip options = new();
    private readonly List<ContextMenuStrip> menus = [];
    private readonly ToolStripMenuItem previewOption = new("窗口预览（可能停帧）") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem viewportOption = new("显示当前视口边界") { CheckOnClick = true };
    private IReadOnlyDictionary<Guid, OverviewPreviewSource> sources = new Dictionary<Guid, OverviewPreviewSource>();
    private OverviewPreviewSession? previews;
    private PreviewCard[] cards = [];
    private PreviewCard[]? synchronizedLayout;
    private OverviewPreviewSource? focusSource;
    private PixelRect? focusFrom, focusTo;
    private bool prepared, shown, disposed, syncing, desiredOpen = true, canConfirm = true;
    private bool previewsDirty = true, refreshRequested, refreshQueued;
    private int modalDepth, menuDepth, frames, previewSyncs, reversals;
    private int geometryRefreshSkips;
    private int metricsDpi;
    private double progress, hideMs, showMs, reconcileMs, constructionMs, dispatchMs, queueMs, prepareMs;
    private double? desktopFlushMs;
    private long requested, firstFrame, previousFrame;
    private bool firstFrameActive, firstFrameForeground;
    private long lastToggleRequested, lastToggleFrame;
    private readonly List<double> gaps = [];
    private readonly List<double> animationWork = [];
    private double previewLayoutMs, previewSyncMs;
    private NativeOverviewBackdrop.Result backdrop = NativeOverviewBackdrop.Result.Unavailable;
    public event EventHandler? Shown;
    public event EventHandler? Activated;
    public event EventHandler? Closed;
    public nint Handle => surface.Handle;
    public bool Visible => surface.Visible;
    public bool IsDisposed => disposed || surface.IsClosed;
    public bool IsTransitioning => !disposed && view.IsAnimating;
    public bool WantsPresentation => desiredOpen && !IsDisposed;
    public bool RequiresDesktopSnapshot => false;
    public bool ShowInTaskbar { get => surface.ShowInTaskbar; set => surface.ShowInTaskbar = value; }
    public double IdlePreparationMilliseconds { get; set; }
    public CanvasWindow? ConfirmedTarget { get; private set; }
    public PointD? ConfirmedCameraPosition { get; private set; }
    internal int LivePreviewCount => previews?.ResourceCount ?? 0;
    internal double Position => progress;
    internal bool NativeBackdropAccepted => surface.NativeBackdropAccepted;
    internal OverviewShowStages ShowStages => surface.ShowStages;
    internal bool AnimationsEnabled => animate;
    internal bool FramesStopped => !view.IsAnimating;
    internal int FrameCount => frames;
    internal int PreviewSynchronizationCount => previewSyncs;
    internal void SetFixtureQuery(string query) { if (!showWithoutActivation) throw new InvalidOperationException("Synthetic fixture only."); view.SetQuery(query); }
    internal void SetFixturePreviews(bool enabled) { if (!showWithoutActivation) throw new InvalidOperationException("Synthetic fixture only."); previewOption.Checked = enabled; }
    internal event Action<double>? FrameObserved;

    internal NativeOverviewPresenter(IEnumerable<CanvasWindow> windows, Camera2D camera, PixelRect bounds, BookmarkLibrary? bookmarks,
        RectD[] viewports, bool animate = true, bool showInTaskbar = false, bool showWithoutActivation = false,
        Func<nint, IOverviewPreviewPort>? fixturePreviewPort = null)
    {
        if (fixturePreviewPort is not null && !showWithoutActivation) throw new ArgumentException("Preview instrumentation requires a synthetic fixture.", nameof(fixturePreviewPort));
        this.bounds = bounds; this.bookmarks = bookmarks; this.showWithoutActivation = showWithoutActivation;
        createPreviewPort = fixturePreviewPort ?? (handle => new DwmOverviewPreviewPort(handle));
        this.animate = animate && CompositionSurface.RenderingTier > 0 && SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled != 0;
        model = new(windows, camera, new(bounds.Width, bounds.Height));
        view = new(model, camera.Position, camera.ViewportSize, viewports.Select(r => new RectD(r.Position - camera.Position, r.Size)).ToArray(), ChromeTheme.Candy);
        surface = new(view, bounds, (handle, allowNative) =>
        {
            if (allowNative && !OverviewRuntimeOptions.ForceSolid) backdrop = NativeOverviewBackdrop.TryApply(handle);
            return backdrop.Accepted;
        }, showInTaskbar, measureShow: OverviewRuntimeOptions.MetricsPath is not null);
        view.GeometryChanged += RefreshPreviews; view.Frame += Advance;
        view.ConfirmRequested += Confirm; view.CancelRequested += RequestClose; view.Command += HandleCommand;
        view.WorkspaceRequested += position => { if (!canConfirm || disposed || modalDepth > 0) return; ConfirmedCameraPosition = position; CloseNow(); };
        view.SaveRequested += SaveWorkspace; view.ManageRequested += ManageWorkspaces; view.OptionsRequested += () => ShowMenu(options, Cursor.Position, true);
        view.WorkspaceMenuRequested += WorkspaceMenu;
        surface.Shown += (_, _) => OnShown();
        surface.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        surface.Closed += (_, _) => OnClosed();
        options.Items.AddRange([previewOption, viewportOption]);
        previewOption.CheckedChanged += (_, _) => { previews?.RetryFailed(); previewsDirty = true; RefreshPreviews(); };
        viewportOption.CheckedChanged += (_, _) => view.ShowViewports(viewportOption.Checked);
        ChromeTheme.StyleMenu(options); RefreshWorkspaces();
    }
    public double PrepareHiddenHost()
    {
        if (shown || disposed || prepared) throw new InvalidOperationException("Only an unused overview can be prepared.");
        var started = Stopwatch.GetTimestamp(); _ = surface.Handle; metricsDpi = surface.Dpi; surface.EnsureLayout();
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    public void SetInitialSelection(Guid? selected) { if (shown) throw new InvalidOperationException("Selection must precede presentation."); if (selected is { } id) model.Select(id); view.RefreshWindows(); }
    public void SetPreviewSources(IEnumerable<OverviewPreviewSource> values)
    {
        var next = values.ToDictionary(source => source.Id);
        if (next.Count == sources.Count && next.All(pair => sources.GetValueOrDefault(pair.Key) == pair.Value)) return;
        sources = next; previewsDirty = true;
        if (focusSource is { } old && sources.GetValueOrDefault(old.Id) != old) { focusSource = null; focusFrom = focusTo = null; }
        RefreshPreviews();
    }
    public void UpdateWindows(IEnumerable<CanvasWindow> windows, bool running, string message)
    {
        if (disposed || model.Status != OverviewStatus.Open) return;
        var next = windows.ToArray();
        var windowsChanged = !model.Windows.SequenceEqual(next);
        var availabilityChanged = canConfirm != running;
        canConfirm = running;
        if (windowsChanged || availabilityChanged) previewsDirty = true;
        if (windowsChanged) { model.Synchronize(next); view.RefreshWindows(); }
        view.SetStatus(running ? bookmarks is { CanWrite: false } ? bookmarks.Message : null : "管理已暂停，暂不可跳转。" + message);
        if (windowsChanged || availabilityChanged) RefreshPreviews();
    }
    public void SetOpenTimings(long requested, double reconcile, double construction, double dispatch = 0, double queue = 0)
    { this.requested = requested; reconcileMs = reconcile; constructionMs = construction; dispatchMs = dispatch; queueMs = queue; }
    public void SetHostTimings(double hide, double show, double? desktopFlush = null) { hideMs = hide; showMs = show; desktopFlushMs = desktopFlush; }
    public void PreparePresentation()
    {
        var started = Stopwatch.GetTimestamp(); _ = surface.Handle; metricsDpi = surface.Dpi;
        previews ??= new(createPreviewPort(surface.Handle));
        // Finalize the hidden geometry before acquiring native relations. Layout
        // notifications during this preparation must not synchronize intermediate states.
        surface.EnsureLayout(); view.RefreshWindows(); prepared = true; RefreshPreviews();
        if (animate && model.SelectedId is { } id && sources.GetValueOrDefault(id) is { FullyOnscreen: true, ScreenBounds: { } screen } source)
        {
            var rect = new PixelRect((int)screen.Position.X, (int)screen.Position.Y, (int)screen.Right, (int)screen.Bottom);
            if (bounds.Contains(rect) && DesktopCropEligibility.Evaluate(source.Identity, rect, rect) == DesktopCropStatus.Ready
                && previews.States.GetValueOrDefault(id) == PreviewState.Preview
                && DesktopCropEligibility.Evaluate(source.Identity, rect, rect) == DesktopCropStatus.Ready)
            {
                focusSource = source; focusFrom = new(rect.Left - bounds.Left, rect.Top - bounds.Top, rect.Right - bounds.Left, rect.Bottom - bounds.Top);
                focusTo = cards.FirstOrDefault(card => card.Id == id)?.ContentBounds;
            }
        }
        view.Seek(0); AnimatePreviews(0); prepareMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    public void Show(bool activate) => surface.Show(activate && !showWithoutActivation);
    private void OnShown()
    {
        shown = true; Shown?.Invoke(this, EventArgs.Empty);
        if (!desiredOpen || disposed) { CloseNow(); return; }
        if (animate) view.Retarget(true, 220); else { progress = 1; view.Seek(1); AnimatePreviews(1); if (!showWithoutActivation) view.FocusSearch(); }
        if (!showWithoutActivation) _ = LoadWallpaper();
    }
    public void ToggleOverview(long capturedTimestamp = 0)
    {
        if (disposed || modalDepth > 0 || menuDepth > 0 || !canConfirm) return;
        if (capturedTimestamp > 0) { lastToggleRequested = capturedTimestamp; lastToggleFrame = 0; }
        desiredOpen = !desiredOpen;
        if (!shown) return;
        reversals++;
        if (animate) { previousFrame = 0; view.Retarget(desiredOpen, desiredOpen ? 220 : 160); }
        else if (!desiredOpen) CloseNow();
    }
    private void RequestClose() { if (!disposed && desiredOpen) ToggleOverview(); }
    public void CancelOverview() { desiredOpen = false; CloseNow(); }
    public bool HandleCommand(Keys keys)
    {
        if (disposed || modalDepth > 0 || menuDepth > 0 || !surface.Enabled) return false;
        if (keys is Keys.Tab or (Keys.Shift | Keys.Tab)) { ToggleOverview(); return true; }
        if (keys == Keys.Escape) { RequestClose(); return true; }
        if (!desiredOpen) return true;
        if (view.IsAnimating) { view.Seek(1); progress = 1; AnimatePreviews(1); }
        switch (keys)
        {
            case Keys.Enter: Confirm(); return true;
            case Keys.Left: model.MoveSelection(OverviewDirection.Left); break;
            case Keys.Right: model.MoveSelection(OverviewDirection.Right); break;
            case Keys.Up: model.MoveSelection(OverviewDirection.Up); break;
            case Keys.Down: model.MoveSelection(OverviewDirection.Down); break;
            case Keys.Control | Keys.Tab: model.Cycle(1); break;
            case Keys.Control | Keys.Shift | Keys.Tab: model.Cycle(-1); break;
            case Keys.Control | Keys.F: view.FocusSearch(true); return true;
            case Keys.F5: previews?.RetryFailed(); previewsDirty = true; surface.EnsureLayout(); break;
            default: return false;
        }
        if (!disposed) view.RefreshWindows(); return true;
    }
    private void Confirm() { if (!disposed && canConfirm && modalDepth == 0 && model.Confirm() is { } target) { ConfirmedTarget = target; CloseNow(); } }
    private void RefreshPreviews()
    {
        if (disposed || !prepared || previews is null) return;
        if (syncing) { refreshRequested = true; return; }
        syncing = true;
        try
        {
            var measuring = OverviewRuntimeOptions.MetricsPath is not null;
            var started = measuring ? Stopwatch.GetTimestamp() : 0;
            var next = view.PreviewLayout().Select(card => new PreviewCard(card.Id, sources.GetValueOrDefault(card.Window)?.Identity, card.Bounds, card.Fallback, card.Fraction)).ToArray();
            if (measuring) previewLayoutMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // This cache describes requested geometry only. Semantic source changes,
            // availability, preview options and retries still force live validation.
            if (!previewsDirty && synchronizedLayout is { } previous && next.SequenceEqual(previous)) { geometryRefreshSkips++; return; }
            previewsDirty = false;
            if (measuring) started = Stopwatch.GetTimestamp();
            previews.Synchronize(next, null, previewOption.Checked && canConfirm, shown ? (byte)Math.Clamp((int)Math.Round(progress * 255), 0, 255) : (byte)0);
            if (measuring) previewSyncMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (disposed) return;
            previewSyncs++; synchronizedLayout = next;
            cards = next.Select(card => card.SourceFraction is null && card.ContentBounds is { } box && previews.SourceSize(card.Id) is { } size
                ? card with { ContentBounds = OverviewPreviewSession.Fit(size, box) } : card).ToArray();
            if (focusSource is { } source) focusTo = cards.FirstOrDefault(card => card.Id == source.Id)?.ContentBounds;
            AnimatePreviews(progress);
        }
        finally
        {
            syncing = false;
            // Native validation can dispatch nested UI messages. Preserve a refresh
            // requested during that work, but never recursively enter the native batch.
            if (!disposed && (refreshRequested || previewsDirty) && !refreshQueued)
            {
                refreshRequested = false; refreshQueued = true;
                surface.Post(() => { refreshQueued = false; RefreshPreviews(); });
            }
        }
    }
    private void AnimatePreviews(double value)
    {
        if (disposed || previews is null) return;
        var opacity = (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
        var focus = focusSource is { } source && sources.GetValueOrDefault(source.Id) == source
            && previews.States.GetValueOrDefault(source.Id) == PreviewState.Preview ? source.Id : (Guid?)null;
        previews.Animate(cards.Where(card => card.Id != focus), opacity);
        if (focus is { } id && focusFrom is { } from && focusTo is { } to && cards.FirstOrDefault(card => card.Id == id) is { } card)
        {
            int Mix(int a, int b) => (int)Math.Round(a + (b - a) * value);
            var destination = new PixelRect(Mix(from.Left, to.Left), Mix(from.Top, to.Top), Mix(from.Right, to.Right), Mix(from.Bottom, to.Bottom));
            previews.Animate([card with { ContentBounds = destination }], 255);
        }
        else if (focus is { } ordinary) previews.Animate(cards.Where(card => card.Id == ordinary), opacity);
    }
    private void Advance(double value)
    {
        if (disposed) return;
        progress = value; var now = Stopwatch.GetTimestamp();
        if (firstFrame == 0) { firstFrame = now; firstFrameActive = surface.Active; firstFrameForeground = WindowEventListener.ForegroundWindow == surface.Handle; }
        if (lastToggleRequested > 0 && lastToggleFrame == 0 && now >= lastToggleRequested) lastToggleFrame = now;
        if (previousFrame > 0) gaps.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
        previousFrame = now; frames++; AnimatePreviews(value);
        if (OverviewRuntimeOptions.MetricsPath is not null) animationWork.Add(Stopwatch.GetElapsedTime(now).TotalMilliseconds);
        FrameObserved?.Invoke(value);
        if (disposed) return;
        if (desiredOpen && value >= 1) { view.StopMotion(); if (!showWithoutActivation) view.FocusSearch(); }
        else if (!desiredOpen && value <= 0) CloseNow();
    }
    private async Task LoadWallpaper()
    {
        using var wallpaper = await WallpaperPaletteReader.ReadPreviewAsync();
        if (!disposed && wallpaper is not null) view.SetWallpaper(wallpaper, includeBackdrop: false);
    }
    private void RefreshWorkspaces()
    {
        view.SetWorkspaces(new[] { new SceneWorkspace(null, "原点", PointD.Zero) }.Concat(bookmarks?.Collection.Items.Select(b => new SceneWorkspace(b.Id, b.Name, b.CameraPosition)) ?? []));
        if (bookmarks is { CanWrite: false }) view.SetStatus(bookmarks.Message);
    }
    private void SaveWorkspace() { if (bookmarks?.CanWrite != true) return; var name = AskName("保存工作区", "工作区"); if (name is not null && !disposed) { bookmarks.Add(name, model.StartCamera.Position); RefreshWorkspaces(); } }
    private void ManageWorkspaces()
    {
        if (bookmarks is null) return;
        using var dialog = new Form { Text = "工作区管理", ClientSize = new(820, 580), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = SystemFonts.MessageBoxFont, TopMost = true };
        dialog.Controls.Add(new BookmarkControl(bookmarks, () => model.StartCamera.Position, () => false, _ => "关闭编辑器后选择总览中的工作区。", () => { }) { Dock = DockStyle.Fill });
        ChromeTheme.Apply(dialog); ShowOwnedDialog(dialog); if (!disposed) RefreshWorkspaces();
    }
    private string? AskName(string title, string current)
    {
        using var dialog = new Form { Text = title, ClientSize = new(400, 136), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = SystemFonts.MessageBoxFont, TopMost = true };
        var input = new TextBox { Text = current, Location = new(16, 16), Width = 368, MaxLength = 80 };
        var save = new ChromeButton { Text = "保存", DialogResult = DialogResult.OK, Location = new(280, 72), Size = new(104, 40) };
        var cancel = new ChromeButton { Text = "取消", DialogResult = DialogResult.Cancel, Location = new(168, 72), Size = new(104, 40) };
        dialog.Controls.AddRange([input, save, cancel]); dialog.AcceptButton = save; dialog.CancelButton = cancel; ChromeTheme.Apply(dialog);
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return ShowOwnedDialog(dialog) == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }
    internal DialogResult ShowOwnedDialog(Form dialog)
    { if (disposed) return DialogResult.Cancel; modalDepth++; try { return dialog.ShowDialog(new WindowOwner(Handle)); } finally { modalDepth--; } }
    private void WorkspaceMenu(Guid id, Point location)
    {
        var entry = bookmarks?.Collection.Items.FirstOrDefault(item => item.Id == id); if (entry is null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("重命名", null, (_, _) => { var name = AskName("重命名工作区", entry.Name); if (name is not null && !disposed) { bookmarks!.Rename(id, name); RefreshWorkspaces(); } });
        menu.Items.Add("删除工作区", null, (_, _) => { bookmarks!.Remove(id); if (!disposed) RefreshWorkspaces(); });
        foreach (ToolStripItem item in menu.Items) item.Enabled = bookmarks!.CanWrite;
        ChromeTheme.StyleMenu(menu); ShowMenu(menu, new(bounds.Left + location.X, bounds.Top + location.Y), false);
    }
    private void ShowMenu(ContextMenuStrip menu, Point location, bool permanent)
    {
        if (disposed) { if (!permanent) menu.Dispose(); return; }
        if (!permanent) menus.Add(menu);
        menuDepth++;
        ToolStripDropDownClosedEventHandler? handler = null;
        handler = (_, _) => { menu.Closed -= handler; menuDepth--; if (!permanent) surface.Post(() => { menus.Remove(menu); menu.Dispose(); }); };
        menu.Closed += handler; menu.Show(location);
    }
    private void CloseNow() { if (!disposed) { desiredOpen = false; view.StopMotion(); surface.Close(); } }
    private void OnClosed()
    {
        if (disposed) return;
        disposed = true; previews?.Dispose(); previews = null; synchronizedLayout = null;
        options.Dispose(); foreach (var menu in menus.ToArray()) menu.Dispose(); menus.Clear();
        if (ConfirmedTarget is null && ConfirmedCameraPosition is null) model.Cancel();
        WriteMetrics(); FrameObserved = null;
        Closed?.Invoke(this, EventArgs.Empty); Shown = null; Activated = null; Closed = null;
    }
    public void Dispose() { if (!disposed) surface.Dispose(); }
    private void WriteMetrics()
    {
        if (OverviewRuntimeOptions.MetricsPath is not { } path) return;
        var sorted = gaps.Order().ToArray(); var work = animationWork.Order().ToArray();
        var record = new { Renderer = "NativeRetained", bounds.Width, bounds.Height, Dpi = metricsDpi,
            DesktopReadback = false, BackdropAccepted = backdrop.Accepted, BackdropSetHResult = backdrop.SetHResult,
            BackdropNonClientSetHResult = backdrop.NonClientSetHResult, BackdropNonClientReadHResult = backdrop.NonClientReadHResult,
            BackdropNonClientEnabled = backdrop.NonClientEnabled,
            FirstFrameActive = firstFrame > 0 ? (bool?)firstFrameActive : null,
            FirstFrameForegroundMatches = firstFrame > 0 ? (bool?)firstFrameForeground : null,
            PreviewCount = cards.Length, Frames = frames, PreviewSyncCalls = previewSyncs, Reversals = reversals,
            PreviewGeometrySkips = geometryRefreshSkips, PreviewLayoutMs = previewLayoutMs, PreviewSyncMs = previewSyncMs,
            AnimationWorkP95Ms = work.Length > 0 ? (double?)work[(int)Math.Ceiling(work.Length * .95) - 1] : null,
            AnimationWorkMaxMs = work.Length > 0 ? (double?)work[^1] : null,
            HostPrepared = IdlePreparationMilliseconds > 0, IdlePreparationMs = IdlePreparationMilliseconds,
            PrepareMs = prepareMs, HostHideMs = hideMs, HostShowMs = showMs, ReconcileMs = reconcileMs, ConstructionMs = constructionMs,
            DesktopFlushMs = desktopFlushMs,
            WindowShowCallMs = surface.ShowStages.WindowShowMs, PostShowLayoutMs = surface.ShowStages.LayoutMs,
            ShownNotificationMs = surface.ShowStages.NotificationMs, ActivateCallMs = surface.ShowStages.ActivationMs,
            InputDispatchMs = dispatchMs, UiQueueMs = queueMs, FocusEligible = focusSource is not null,
            RequestToFirstCallbackMs = requested > 0 && firstFrame >= requested ? (double?)Stopwatch.GetElapsedTime(requested, firstFrame).TotalMilliseconds : null,
            LastToggleToCallbackMs = lastToggleRequested > 0 && lastToggleFrame >= lastToggleRequested ? (double?)Stopwatch.GetElapsedTime(lastToggleRequested, lastToggleFrame).TotalMilliseconds : null,
            FrameGapP95Ms = sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], FrameGapMaxMs = sorted.LastOrDefault() };
        try { File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private sealed record WindowOwner(nint Handle) : IWin32Window;
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
}
