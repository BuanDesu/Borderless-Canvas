using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Presentation;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    internal static string? MotionMetricsPath { get; set; }
    private readonly List<double> motionSamples = [], motionTimes = [];
    private double prepareMilliseconds, captureMilliseconds, sceneSnapshotMilliseconds, uploadMilliseconds;

    private void WriteMotionMetrics()
    {
        if (MotionMetricsPath is not { } path || motionTimes.Count == 0) return;
        // Opt-in timing numbers only: no titles, identities, paths, wallpaper or desktop pixels.
        var gaps = motionTimes.Zip(motionTimes.Skip(1), (a, b) => b - a).Order().ToArray();
        var work = motionSamples.Order().ToArray();
        var record = new
        {
            Width, Height, Dpi = DeviceDpi, Tier = CompositionSurface.RenderingTier,
            PreviewCount = animationPreviewCount, Frames = motionTimes.Count,
            CaptureMs = captureMilliseconds, PrepareMs = prepareMilliseconds,
            SceneSnapshotMs = sceneSnapshotMilliseconds, UploadMs = uploadMilliseconds,
            FirstFrameMs = motionTimes[0], FrameGapP95Ms = Percentile(gaps, .95),
            FrameGapMaxMs = gaps.LastOrDefault(), CallbackP95Ms = Percentile(work, .95),
            CallbackMaxMs = work.LastOrDefault()
        };
        try { File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private int animationPreviewCount;
    private static double Percentile(double[] values, double percentile) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
}
