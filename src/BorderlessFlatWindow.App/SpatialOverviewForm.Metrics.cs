using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    internal static string? MotionMetricsPath { get; set; }
    private readonly List<double> motionSamples = [], motionTimes = [];
    private double prepareMilliseconds, captureMilliseconds, sceneSnapshotMilliseconds, uploadMilliseconds;
    private double rawCaptureMilliseconds, focusEligibilityMilliseconds, focusCloneMilliseconds, focusRecheckMilliseconds;
    private bool backdropCaptured;
    private string readbackBackend = "None";
    private bool focusWasForeground;
    private int cropOcclusionKinds;
    private double reconcileMilliseconds, constructionMilliseconds;
    private double dispatchMilliseconds, queueMilliseconds, hostHideMilliseconds, hostShowMilliseconds;
    private double sceneLayoutMilliseconds, previewLayoutMilliseconds, previewSyncMilliseconds;
    private int previewSyncCalls;
    private long requestTimestamp, firstCallbackTimestamp;
    internal void SetOpenTimings(long requested, double reconcile, double construction, double dispatch = 0, double queue = 0)
    { requestTimestamp = requested; reconcileMilliseconds = reconcile; constructionMilliseconds = construction; dispatchMilliseconds = dispatch; queueMilliseconds = queue; }
    internal void SetHostTimings(double hide, double show) { hostHideMilliseconds = hide; hostShowMilliseconds = show; }

    private void WriteMotionMetrics()
    {
        if (MotionMetricsPath is not { } path || motionTimes.Count == 0) return;
        // Opt-in timing numbers only: no titles, identities, paths, wallpaper or desktop pixels.
        var gaps = motionTimes.Zip(motionTimes.Skip(1), (a, b) => b - a).Order().ToArray();
        var work = motionSamples.Order().ToArray();
        var record = new
        {
            Width, Height, Dpi = DeviceDpi, Tier = CompositionSurface.RenderingTier,
            Renderer = scene is null ? "Legacy" : "RetainedScene",
            PreviewCount = animationPreviewCount, Frames = motionTimes.Count,
            SceneReused = reusedScene,
            HostPrepared = IdlePreparationMilliseconds > 0,
            IdlePreparationMs = IdlePreparationMilliseconds,
            CompositorFocus = compositorFocus,
            FocusCropStatus = focusCropStatus,
            CaptureMs = captureMilliseconds, PrepareMs = prepareMilliseconds,
            RawCaptureMs = rawCaptureMilliseconds, FocusEligibilityMs = focusEligibilityMilliseconds,
            BackdropCaptured = backdropCaptured,
            ReadbackBackend = readbackBackend, FocusWasForeground = focusWasForeground,
            CropOcclusionKinds = cropOcclusionKinds,
            FocusCloneMs = focusCloneMilliseconds, FocusRecheckMs = focusRecheckMilliseconds,
            ReconcileMs = reconcileMilliseconds, ConstructionMs = constructionMilliseconds,
            InputDispatchMs = dispatchMilliseconds, UiQueueMs = queueMilliseconds,
            HostHideMs = hostHideMilliseconds, HostShowMs = hostShowMilliseconds,
            SceneLayoutMs = sceneLayoutMilliseconds, PreviewLayoutMs = previewLayoutMilliseconds,
            PreviewSyncMs = previewSyncMilliseconds, PreviewSyncCalls = previewSyncCalls,
            RequestToFirstCallbackMs = requestTimestamp > 0 && firstCallbackTimestamp >= requestTimestamp
                ? Stopwatch.GetElapsedTime(requestTimestamp, firstCallbackTimestamp).TotalMilliseconds : 0,
            SceneSnapshotMs = sceneSnapshotMilliseconds, UploadMs = uploadMilliseconds,
            FirstFrameMs = motionTimes[0], FrameGapP95Ms = Percentile(gaps, .95),
            FrameGapMaxMs = gaps.LastOrDefault(), CallbackP95Ms = Percentile(work, .95),
            CallbackMaxMs = work.LastOrDefault()
        };
        try { File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private int animationPreviewCount;
    private bool compositorFocus;
    private int focusCropStatus = -1;
    private static double Percentile(double[] values, double percentile) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
}
