using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Overview;

public enum OverviewDirection { Left, Right, Up, Down }
public enum OverviewStatus { Open, Confirmed, Cancelled }

/// <summary>Selection and search never own or change the working camera.</summary>
public sealed class SpatialOverviewSession
{
    private Dictionary<Guid, CanvasWindow> windows;
    private string query = "";
    public SpatialOverviewSession(IEnumerable<CanvasWindow> windows, Camera2D camera, SizeD mapSize, Guid? selected = null)
    {
        this.windows = Validate(windows); StartCamera = camera; MapSize = mapSize;
        Projection = Fit(mapSize); SelectedId = selected is { } id && this.windows.ContainsKey(id) ? id : NearestToViewport();
    }
    public Camera2D StartCamera { get; }
    public SizeD MapSize { get; private set; }
    public OverviewProjection Projection { get; private set; }
    public OverviewStatus Status { get; private set; }
    public Guid? SelectedId { get; private set; }
    public string Query => query;
    public IReadOnlyList<CanvasWindow> Windows => windows.Values.ToArray();
    public IReadOnlyList<CanvasWindow> Matches => windows.Values.Where(MatchesQuery).ToArray();
    public bool IsMatch(Guid id) => windows.TryGetValue(id, out var window) && MatchesQuery(window);
    private bool MatchesQuery(CanvasWindow window) => window.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static Dictionary<Guid, CanvasWindow> Validate(IEnumerable<CanvasWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var copy = windows.ToArray();
        if (copy.Any(window => window.Id == Guid.Empty || !window.Bounds.Size.IsPositive)
            || copy.Select(window => window.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("Overview windows need unique identities and valid bounds.", nameof(windows));
        return copy.ToDictionary(window => window.Id);
    }
    private OverviewProjection Fit(SizeD size) => OverviewProjection.Fit(windows.Values.Select(window => window.Bounds)
        .Append(StartCamera.Bounds).Append(new RectD(PointD.Zero, new SizeD(1, 1))), size, 32);
    public void Refit(SizeD size)
    {
        if (Status != OverviewStatus.Open) return;
        var next = Fit(size); MapSize = size; Projection = next;
    }
    public void Synchronize(IEnumerable<CanvasWindow> current)
    {
        if (Status != OverviewStatus.Open) return;
        var next = Validate(current); windows = next;
        if (SelectedId is not { } id || !IsMatch(id)) SelectedId = NearestToViewport();
        // Keep the projection stable when titles change, targets close, or new windows appear.
    }
    public void Search(string text)
    {
        if (Status != OverviewStatus.Open) return;
        query = (text ?? "").Trim();
        if (SelectedId is not { } id || !IsMatch(id)) SelectedId = NearestToViewport();
    }
    public bool Select(Guid id)
    {
        if (Status != OverviewStatus.Open || !IsMatch(id)) return false;
        SelectedId = id; return true;
    }
    public Guid? HitTest(PointD mapPosition)
    {
        if (Status != OverviewStatus.Open) return null;
        var position = Projection.ToWorld(mapPosition);
        return windows.Values.OrderBy(window => window.Id == SelectedId).LastOrDefault(window => MatchesQuery(window)
            && window.Bounds.Contains(position))?.Id;
    }
    public bool MoveSelection(OverviewDirection direction)
    {
        if (Status != OverviewStatus.Open) return false;
        var origin = SelectedId is { } id && windows.TryGetValue(id, out var selected) ? selected.Bounds.Center : StartCamera.Bounds.Center;
        var candidate = windows.Values.Where(window => MatchesQuery(window) && window.Id != SelectedId)
            .Select(window => (Window: window, Score: DirectionScore(window.Bounds.Center - origin, direction)))
            .Where(item => item.Score is not null).OrderBy(item => item.Score).ThenBy(item => item.Window.Id).FirstOrDefault();
        return candidate.Window is not null && Select(candidate.Window.Id);
    }
    private static double? DirectionScore(PointD delta, OverviewDirection direction)
    {
        var along = direction switch { OverviewDirection.Left => -delta.X, OverviewDirection.Right => delta.X,
            OverviewDirection.Up => -delta.Y, _ => delta.Y };
        if (along <= 0) return null;
        var across = Math.Abs(direction is OverviewDirection.Left or OverviewDirection.Right ? delta.Y : delta.X);
        var scale = Math.Max(along, across);
        var a = along / scale; var b = across / scale;
        // Log distance avoids overflowing squared far-world distances; angle breaks poor diagonal choices.
        return Math.Log(scale) + 0.5 * Math.Log(a * a + b * b) + 2 * b / (a + b);
    }
    public bool Cycle(int direction)
    {
        if (Status != OverviewStatus.Open || direction == 0) return false;
        var matches = windows.Values.Where(MatchesQuery).OrderBy(window => window.Bounds.Position.Y)
            .ThenBy(window => window.Bounds.Position.X).ThenBy(window => window.Id).ToArray();
        if (matches.Length == 0) return false;
        var index = Array.FindIndex(matches, window => window.Id == SelectedId);
        return Select(matches[(index + (direction > 0 ? 1 : -1) + matches.Length) % matches.Length].Id);
    }
    public CanvasWindow? Confirm()
    {
        if (Status != OverviewStatus.Open || SelectedId is not { } id || !IsMatch(id)) return null;
        Status = OverviewStatus.Confirmed; return windows[id];
    }
    public Camera2D Cancel()
    {
        if (Status == OverviewStatus.Open) Status = OverviewStatus.Cancelled;
        return StartCamera;
    }
    private Guid? NearestToViewport() => windows.Values.Where(MatchesQuery)
        .OrderBy(window => LogDistance(window.Bounds.Center - StartCamera.Bounds.Center)).ThenBy(window => window.Id)
        .Select(window => (Guid?)window.Id).FirstOrDefault();
    private static double LogDistance(PointD delta)
    {
        var scale = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
        return scale == 0 ? double.NegativeInfinity : Math.Log(scale)
            + 0.5 * Math.Log(Math.Pow(delta.X / scale, 2) + Math.Pow(delta.Y / scale, 2));
    }
}
