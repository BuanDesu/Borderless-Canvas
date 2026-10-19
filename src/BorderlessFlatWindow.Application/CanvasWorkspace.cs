using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application;

/// <summary>In-memory navigation model. It never moves or activates a desktop window.</summary>
public sealed class CanvasWorkspace
{
    private readonly Dictionary<Guid, CanvasWindow> windows = [];
    private readonly Dictionary<Guid, LocationBookmark> bookmarks = [];
    private readonly List<PointD> history = [];
    private const int HistoryLimit = 64;

    public CanvasWorkspace(SizeD viewportSize) => Camera = new(PointD.Zero, viewportSize);

    public Camera2D Camera { get; private set; }
    public IReadOnlyList<CanvasWindow> Windows => windows.Values.ToArray();
    public IReadOnlyList<LocationBookmark> Bookmarks => bookmarks.Values.ToArray();

    public void UpsertWindow(CanvasWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Id == Guid.Empty || !window.Bounds.Size.IsPositive)
            throw new ArgumentException("A window needs a valid identity and bounds.", nameof(window));
        windows[window.Id] = window;
    }

    public bool RemoveWindow(Guid id) => windows.Remove(id);

    public void SetViewportSize(SizeD size) => Camera = new(Camera.Position, size);

    public void NavigateTo(PointD position)
    {
        if (position == Camera.Position)
            return;
        history.Add(Camera.Position);
        if (history.Count > HistoryLimit)
            history.RemoveAt(0);
        Camera = Camera.At(position);
    }

    public void PanBy(PointD delta) => NavigateTo(Camera.Position + delta);
    public void ReturnHome() => NavigateTo(PointD.Zero);

    public bool ReturnToPreviousView()
    {
        if (history.Count == 0)
            return false;
        Camera = Camera.At(history[^1]);
        history.RemoveAt(history.Count - 1);
        return true;
    }

    public bool FocusWindow(Guid id)
    {
        if (!windows.TryGetValue(id, out var window))
            return false;
        NavigateTo(Camera.CenterOn(window.Bounds).Position);
        return true;
    }

    public LocationBookmark AddBookmark(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var bookmark = new LocationBookmark(Guid.NewGuid(), name.Trim(), Camera.Position);
        bookmarks.Add(bookmark.Id, bookmark);
        return bookmark;
    }

    public bool RemoveBookmark(Guid id) => bookmarks.Remove(id);

    public bool GoToBookmark(Guid id)
    {
        if (!bookmarks.TryGetValue(id, out var bookmark))
            return false;
        NavigateTo(bookmark.CameraPosition);
        return true;
    }
}
