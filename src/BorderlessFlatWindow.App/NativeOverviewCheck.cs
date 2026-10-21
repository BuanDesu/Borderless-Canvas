using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Bookmarks;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Application.Overview;

namespace BorderlessFlatWindow.App;

/// <summary>Full native presenter on a real WinForms message loop, with synthetic owned sources only.</summary>
internal static class NativeOverviewCheck
{
    internal static int Run(string output, bool compareSystemTransitions = false)
    {
        if (File.Exists(output)) throw new IOException("Use a new native overview report path.");
        var directory = Path.Combine(Path.GetDirectoryName(output)!, "native-presenter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var labels = new BookmarkLibrary(() => new JsonBookmarkStore(Path.Combine(directory, "bookmarks.json")));
        labels.Reload(); labels.Add("近处", new(0, 0)); labels.Add("远处", new(2400, -200)); labels.Add("空视窗", new(20000, 20000));
        using var process = Process.GetCurrentProcess();
        var before = WindowEventListener.ForegroundWindow;
        var work = Screen.PrimaryScreen!.WorkingArea;
        var sourceForms = Enumerable.Range(0, 3).Select(index => new SourceWindow(index)
            { Bounds = new(work.Left + 70 + index * 400, work.Top + 90, 340, 250), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual }).ToArray();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var models = ids.Select((id, index) => new CanvasWindow(id, "Synthetic " + index,
            new(new PointD(index switch { 0 => 70, 1 => 2450, _ => -800 }, 90 + index * 260), new SizeD(340, 250)))).ToArray();
        var camera = new Camera2D(PointD.Zero, new(work.Width, work.Height));
        var results = new List<object>();
        var forceSolid = OverviewRuntimeOptions.ForceSolid;
        var previousMetrics = OverviewRuntimeOptions.MetricsPath;
        var metricPath = Path.Combine(directory, "metrics.jsonl"); OverviewRuntimeOptions.MetricsPath = metricPath;
        NativeOverviewPresenter? current = null;
        Exception? failure = null; var index = -1; var phase = 0; var shown = 0; var closed = 0;
        var totalFrames = 0; var animatedCases = 0; var idleFrames = 0; long idleStarted = 0; var maxResources = 0;
        var disabledPassed = false; var selectionPassed = false; var searchPassed = false;
        using var context = new ApplicationContext();
        using var driver = new System.Windows.Forms.Timer { Interval = 20 };
        using var watchdog = new System.Windows.Forms.Timer { Interval = compareSystemTransitions ? 120000 : 30000 };
        var sizes = new[] { new Size(1920, 1080), new Size(3440, 1440), new Size(960, 720) };
        var caseCount = compareSystemTransitions ? 24 : 6;
        Size activeSize = default;
        bool? transitionsDisabled = null;
        int? transitionsHResult = null;
        OverviewPreviewSource[] Sources() => sourceForms.Select((source, position) => new OverviewPreviewSource(ids[position],
            new NativeWindowIdentity(source.Handle, (uint)Environment.ProcessId, process.StartTime.ToUniversalTime().ToFileTimeUtc()), false)).ToArray();
        void Fail(Exception error)
        { failure = error; driver.Stop(); watchdog.Stop(); current?.Dispose(); current = null; context.ExitThread(); }
        void StartCase()
        {
            if (++index >= caseCount) { driver.Stop(); CheckNoAnimation(); CheckModalInput(); context.ExitThread(); return; }
            phase = 0; shown = closed = maxResources = 0; idleStarted = 0;
            var size = activeSize = sizes[compareSystemTransitions ? index / 8 : index % sizes.Length];
            OverviewRuntimeOptions.ForceSolid = compareSystemTransitions ? index % 8 >= 4 : index >= 3;
            transitionsDisabled = compareSystemTransitions ? index % 4 is 1 or 2 : null;
            transitionsHResult = null;
            current = new(models, camera, new(work.Left, work.Top, work.Left + size.Width, work.Top + size.Height), labels,
                [camera.Bounds], animate: true, showWithoutActivation: true);
            var local = current;
            local.SetOpenTimings(Stopwatch.GetTimestamp(), 0, 0);
            local.SetInitialSelection(ids[0]); local.SetPreviewSources(Sources());
            local.Shown += (_, _) => shown++;
            local.Closed += (_, _) => closed++;
            local.FrameObserved += value =>
            {
                try
                {
                    totalFrames++;
                    if (phase == 0 && value > .45) { phase = 1; local.HandleCommand(Keys.Tab); }
                    else if (phase == 1 && value < .2) { phase = 2; local.HandleCommand(Keys.Shift | Keys.Tab); }
                    else if (phase == 2 && value >= 1) { phase = 3; idleFrames = local.FrameCount; idleStarted = Stopwatch.GetTimestamp(); }
                    else if (phase == 4 && value < .6) { phase = 5; local.ToggleOverview(Stopwatch.GetTimestamp()); }
                    else if (phase == 5 && value >= 1) { phase = 6; idleFrames = local.FrameCount; idleStarted = Stopwatch.GetTimestamp(); }
                }
                catch (Exception error) { Fail(error); }
            };
            local.PrepareHiddenHost();
            if (transitionsDisabled is { } disabled)
            {
                var value = disabled ? 1 : 0;
                transitionsHResult = DwmSetWindowAttribute(local.Handle, 3, in value, sizeof(int));
            }
            if (local.Visible || local.LivePreviewCount != 0 || WindowEventListener.ForegroundWindow != before)
                throw new InvalidOperationException("Native hidden preparation activated or acquired previews.");
            local.PreparePresentation(); local.Show(false); maxResources = local.LivePreviewCount;
            if (local.NativeBackdropAccepted && (DwmGetWindowAttribute(local.Handle, 1, out var nonClientEnabled, sizeof(int)) < 0 || nonClientEnabled == 0))
                throw new InvalidOperationException("Accepted native backdrop lost non-client rendering after show.");
            if (local.AnimationsEnabled) animatedCases++;
            else { phase = 6; idleFrames = local.FrameCount; idleStarted = Stopwatch.GetTimestamp(); }
            if (shown != 1 || maxResources < 4 || WindowEventListener.ForegroundWindow != before)
                throw new InvalidOperationException("Native presenter missed source/workspace previews or changed foreground.");
        }
        driver.Tick += (_, _) =>
        {
            try
            {
                if (current is null) { StartCase(); return; }
                if (phase is not (3 or 6) || idleStarted == 0 || Stopwatch.GetElapsedTime(idleStarted).TotalMilliseconds < 140) return;
                var local = current;
                if (!local.FramesStopped || local.FrameCount != idleFrames) throw new InvalidOperationException("Native endpoint kept rendering callbacks alive.");
                if (phase == 3)
                {
                    phase = 4; local.HandleCommand(Keys.Tab); return;
                }
                EnableWindow(local.Handle, false);
                try { disabledPassed = !local.HandleCommand(Keys.Tab); }
                finally { EnableWindow(local.Handle, true); }
                if (!disabledPassed) throw new InvalidOperationException("Disabled WPF owner consumed modal Tab.");
                var previousResources = local.LivePreviewCount;
                local.SetFixtureQuery("no-synthetic-match"); local.HandleCommand(Keys.Enter);
                searchPassed = !local.IsDisposed && local.LivePreviewCount < previousResources;
                if (!searchPassed) throw new InvalidOperationException("Native search or empty confirmation failed.");
                local.SetFixtureQuery(""); local.HandleCommand(Keys.Control | Keys.Tab); local.HandleCommand(Keys.Enter);
                selectionPassed = local.ConfirmedTarget is { } target && target.Id != ids[0];
                if (!selectionPassed || closed != 1 || !local.IsDisposed || local.LivePreviewCount != 0 || WindowEventListener.ForegroundWindow != before)
                    throw new InvalidOperationException("Native selection, release, completion or foreground failed.");
                results.Add(new { Case = index, activeSize.Width, activeSize.Height,
                    SystemTransitionsDisabled = transitionsDisabled, SystemTransitionsHResult = transitionsHResult, ShowStages = local.ShowStages,
                    ForceSolid = OverviewRuntimeOptions.ForceSolid, AnimationEnabled = local.AnimationsEnabled, MaxResources = maxResources, Shown = shown, Closed = closed,
                    DisabledTabPassed = disabledPassed, SearchPassed = searchPassed, SelectionPassed = selectionPassed, Frames = local.FrameCount,
                    ForegroundUnchanged = true });
                local.Dispose(); local.Dispose(); current = null;
            }
            catch (Exception error) { Fail(error); }
        };
        watchdog.Tick += (_, _) => Fail(new TimeoutException("Native presenter test did not finish."));
        try
        {
            foreach (var form in sourceForms) form.Show();
            CheckDetachedDpi();
            CheckHiddenInteractive();
            CheckCancelled();
            CheckRefreshChurn();
            driver.Start(); watchdog.Start(); System.Windows.Forms.Application.Run(context);
            if (failure is not null) throw new InvalidOperationException("Native presenter check failed.", failure);
            if (results.Count != caseCount + 2 || (animatedCases > 0 && totalFrames < 12)) throw new InvalidOperationException("Native presenter cases were incomplete.");
            var metrics = File.ReadAllLines(metricPath).Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .Where(row => row.GetProperty("RequestToFirstCallbackMs").ValueKind == JsonValueKind.Number).ToArray();
            if (metrics.Length != animatedCases || metrics.Any(row => row.GetProperty("RequestToFirstCallbackMs").GetDouble() <= 0
                || row.GetProperty("LastToggleToCallbackMs").ValueKind != JsonValueKind.Number))
                throw new InvalidOperationException("Retargeting overwrote the initial request timing.");
            if (metrics.Any(row => row.GetProperty("WindowShowCallMs").ValueKind != JsonValueKind.Number
                || row.GetProperty("PostShowLayoutMs").ValueKind != JsonValueKind.Number
                || row.GetProperty("ShownNotificationMs").ValueKind != JsonValueKind.Number
                || row.GetProperty("ActivateCallMs").ValueKind != JsonValueKind.Null))
                throw new InvalidOperationException("Native show timings lost completed stages or invented an activation stage.");
        }
        finally
        {
            driver.Stop(); watchdog.Stop(); current?.Dispose();
            foreach (var form in sourceForms) form.Dispose(); OverviewRuntimeOptions.ForceSolid = forceSolid;
            OverviewRuntimeOptions.MetricsPath = previousMetrics;
            File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("PASS Native full-scene presenter retains space/workspace previews, reverses after idle, passes disabled-host Tab, searches, selects and releases on a WinForms loop");
        Console.WriteLine("PASS Native initial-request and later-retarget timings remain distinct");
        Console.WriteLine("PASS Native preview refreshes coalesce unchanged geometry and retain explicit validation");
        if (compareSystemTransitions) Console.WriteLine("Native show comparison: 24 owned presenter samples in enabled/disabled/disabled/enabled order; no desktop capture or activation requested.");
        return 0;

        void CheckRefreshChurn()
        {
            RefreshPort? port = null;
            using var local = new NativeOverviewPresenter(models, camera, new(work.Left, work.Top, work.Left + 1000, work.Top + 700), labels,
                [camera.Bounds], animate: false, showWithoutActivation: true,
                fixturePreviewPort: handle => port = new RefreshPort(new DwmOverviewPreviewPort(handle)));
            local.SetPreviewSources(Sources()); local.PrepareHiddenHost(); local.PreparePresentation();
            if (local.PreviewSynchronizationCount != 1)
                throw new InvalidOperationException($"Initial native preparation repeated preview synchronization {local.PreviewSynchronizationCount} times.");
            local.Show(false); System.Windows.Forms.Application.DoEvents();
            var stable = local.PreviewSynchronizationCount;
            for (var iteration = 0; iteration < 20; iteration++)
            { local.UpdateWindows(models, true, ""); local.SetPreviewSources(Sources()); }
            if (local.PreviewSynchronizationCount != stable)
                throw new InvalidOperationException("Unchanged native snapshots repeated preview synchronization.");
            var renamed = models.Select((window, position) => position == 0 ? window with { Title = "Changed synthetic title" } : window).ToArray();
            local.UpdateWindows(renamed, true, "");
            if (local.PreviewSynchronizationCount != ++stable)
                throw new InvalidOperationException("A changed source snapshot did not validate exactly once.");
            local.SetFixtureQuery("no-synthetic-match");
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount >= 8)
                throw new InvalidOperationException("Native search did not update preview eligibility.");
            local.SetFixtureQuery("");
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount < 4)
                throw new InvalidOperationException("Native search did not restore previews.");
            local.HandleCommand(Keys.F5);
            if (local.PreviewSynchronizationCount != ++stable)
                throw new InvalidOperationException("Explicit native retry failed to revalidate unchanged geometry.");
            local.SetFixturePreviews(false);
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount != 0)
                throw new InvalidOperationException("Disabling previews retained resources with unchanged geometry.");
            local.SetFixturePreviews(true);
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount < 4)
                throw new InvalidOperationException("Enabling previews failed to acquire unchanged geometry.");
            local.UpdateWindows(renamed, false, "Fixture pause");
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount != 0)
                throw new InvalidOperationException("Native pause retained preview resources.");
            local.UpdateWindows(renamed, true, "");
            if (local.PreviewSynchronizationCount != ++stable || local.LivePreviewCount < 4)
                throw new InvalidOperationException("Native resume failed to restore preview resources.");
            var nestedModels = renamed.Select((window, position) => position == 0 ? window with { Title = "Nested synthetic change" } : window).ToArray();
            port!.OnBatch = () => local.UpdateWindows(nestedModels, true, "");
            var beforeNested = port.Batches;
            local.SetPreviewSources(Sources().Select(source => source with { FullyOnscreen = true }));
            if (port.Batches != beforeNested + 1 || port.MaxDepth != 1)
                throw new InvalidOperationException("Native refresh recursively entered a preview batch.");
            System.Windows.Forms.Application.DoEvents();
            if (port.Batches != beforeNested + 2 || port.MaxDepth != 1)
                throw new InvalidOperationException("A semantic change during native validation was lost or repeated.");
            var unchanged = port.Batches;
            local.UpdateWindows(nestedModels, true, ""); System.Windows.Forms.Application.DoEvents();
            if (port.Batches != unchanged) throw new InvalidOperationException("Deferred native refresh kept retrying unchanged input.");
            port.OnBatch = () => local.UpdateWindows(renamed, true, "");
            local.SetPreviewSources(Sources());
            var beforeClose = port.Batches;
            local.CancelOverview();
            System.Windows.Forms.Application.DoEvents();
            if (port.Batches != beforeClose) throw new InvalidOperationException("A queued native refresh ran after closure.");
            if (local.LivePreviewCount != 0 || WindowEventListener.ForegroundWindow != before)
                throw new InvalidOperationException("Native refresh checks leaked previews or changed foreground.");
        }

        void CheckHiddenInteractive()
        {
            foreach (var size in sizes)
            {
                using var hidden = new NativeOverviewPresenter(models, camera, new(work.Left, work.Top, work.Left + size.Width, work.Top + size.Height), labels,
                    [camera.Bounds], showWithoutActivation: false);
                hidden.PrepareHiddenHost();
                if (hidden.Visible || hidden.LivePreviewCount != 0 || hidden.ShowStages != default || WindowEventListener.ForegroundWindow != before)
                    throw new InvalidOperationException("Interactive WPF hidden host changed foreground.");
            }
        }
        void CheckDetachedDpi()
        {
            foreach (var scale in new[] { 1d, 1.25, 2 })
            {
                var model = new SpatialOverviewSession(models, camera, new(1600, 1200));
                using var view = new SpatialOverviewView(model, camera.Position, camera.ViewportSize, [camera.Bounds], ChromeTheme.Candy, synthetic: true);
                view.SetWorkspaces([new(null, "原点", PointD.Zero)]); view.SetViewport(1600, 1200, scale);
                var layout = view.PreviewLayout();
                if (layout.Count <= models.Length || layout.Any(card => card.Bounds is { } rect
                    && (!rect.IsValid || rect.Left < 0 || rect.Top < 0 || rect.Right > 1600 || rect.Bottom > 1200)))
                    throw new InvalidOperationException("Detached view lost its physical preview coordinates at a synthetic DPI.");
                using var stream = new MemoryStream(view.RenderSyntheticPng()); using var image = Image.FromStream(stream);
                if (image.Width != 1600 || image.Height != 1200) throw new InvalidOperationException("Detached view export changed physical dimensions.");
            }
            Console.WriteLine("PASS Shared detached view preserves physical preview bounds and synthetic rendering at 100/125/200 percent");
        }
        void CheckModalInput()
        {
            using var owner = new NativeOverviewPresenter(models, camera, new(0, 0, 800, 600), labels, [camera.Bounds], animate: false, showWithoutActivation: true);
            owner.SetPreviewSources(Sources()); owner.PreparePresentation(); owner.Show(false);
            using var dialog = new Form { Text = "BFW synthetic owned dialog", ClientSize = new(300, 140), ShowInTaskbar = false, TopMost = true };
            var field = new TextBox { Text = "Synthetic", Location = new(12, 12), Width = 250, TabIndex = 0 };
            var cancel = new ChromeButton { Text = "Cancel", Location = new(12, 60), TabIndex = 1, DialogResult = DialogResult.Cancel };
            dialog.Controls.AddRange([field, cancel]); dialog.CancelButton = cancel;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            var step = 0; var ticks = 0; var routed = false; var disabled = false; var posted = false;
            dialog.Shown += (_, _) => { field.Focus(); timer.Start(); };
            timer.Tick += (_, _) =>
            {
                if (++ticks > 20) { timer.Stop(); dialog.DialogResult = DialogResult.Abort; dialog.Close(); return; }
                if (step == 0)
                {
                    disabled = !owner.HandleCommand(Keys.Tab) && !IsWindowEnabled(owner.Handle);
                    // Messages target only child HWNDs created by this fixture, inside the real modal pump.
                    posted = PostMessage(field.Handle, 0x100, 9, 0); step = 1;
                }
                else if (step == 1)
                { routed = ReferenceEquals(dialog.ActiveControl, cancel); posted &= PostMessage(cancel.Handle, 0x100, 27, 0); step = 2; }
            };
            var result = owner.ShowOwnedDialog(dialog); timer.Stop();
            var reenabled = IsWindowEnabled(owner.Handle);
            if (!posted || !disabled || !routed || result != DialogResult.Cancel || !reenabled || owner.IsDisposed)
                throw new InvalidOperationException($"Owned dialog input: posted={posted}, disabled={disabled}, routed={routed}, cancelled={result == DialogResult.Cancel}, enabled={reenabled}.");
            owner.Dispose();
            results.Add(new { ModalTabRouted = routed, ModalEscapeCancelled = true, OwnerDisabled = disabled, OwnerReenabled = reenabled });
            Console.WriteLine("PASS Owned WinForms modal pump routes fixture Tab and Escape without returning the native overview");
        }
        void CheckNoAnimation()
        {
            using var direct = new NativeOverviewPresenter(models, camera, new(0, 0, 1000, 700), labels, [camera.Bounds], animate: false, showWithoutActivation: true);
            direct.SetPreviewSources(Sources()); var closes = 0; direct.Closed += (_, _) => closes++;
            direct.PreparePresentation(); direct.Show(false);
            if (direct.Position != 1 || direct.IsTransitioning || direct.LivePreviewCount < 4 || direct.FrameCount != 0)
                throw new InvalidOperationException("Disabled animation failed to show the complete native scene directly.");
            direct.ToggleOverview();
            if (!direct.IsDisposed || direct.LivePreviewCount != 0 || closes != 1 || WindowEventListener.ForegroundWindow != before)
                throw new InvalidOperationException("Disabled animation failed to close and release immediately.");
            results.Add(new { DisabledAnimationDirectShow = true, DisabledAnimationImmediateClose = true });
        }
        void CheckCancelled()
        {
            var state = Path.Combine(directory, "cancelled"); Directory.CreateDirectory(state);
            using var manager = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true,
                journalPath: Path.Combine(state, "recovery.json"), bookmarksPath: Path.Combine(state, "bookmarks.json"));
            using var cancelled = new NativeOverviewPresenter(models, camera, new(0, 0, 800, 600), labels, [camera.Bounds], showWithoutActivation: true);
            var completion = 0; var displays = 0;
            cancelled.Shown += (_, _) => displays++;
            cancelled.ToggleOverview();
            manager.PresentOverview(cancelled, () => { completion++; cancelled.Dispose(); });
            if (displays != 0 || completion != 1 || !cancelled.IsDisposed || manager.Visible || WindowEventListener.ForegroundWindow != before)
                throw new InvalidOperationException("Cancelled native presenter was shown or completed twice.");
        }
    }
    private sealed class RefreshPort(IOverviewPreviewPort inner) : IOverviewPreviewPort
    {
        internal Action? OnBatch;
        internal int Batches, MaxDepth;
        private int depth;
        public IOverviewPreview? Create(NativeWindowIdentity identity) => inner.Create(identity);
        public IDisposable BeginBatch()
        {
            var scope = inner.BeginBatch(); Batches++; MaxDepth = Math.Max(MaxDepth, ++depth);
            try { var callback = OnBatch; OnBatch = null; callback?.Invoke(); return new Scope(this, scope); }
            catch { scope?.Dispose(); depth--; throw; }
        }
        private sealed class Scope(RefreshPort owner, IDisposable? inner) : IDisposable
        {
            private bool disposed;
            public void Dispose() { if (disposed) return; disposed = true; try { inner?.Dispose(); } finally { owner.depth--; } }
        }
    }
    private sealed class SourceWindow(int index) : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000088; return value; } }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(index switch { 0 => Color.MediumPurple, 1 => Color.Coral, _ => Color.Turquoise }); base.OnPaint(e); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);
}
