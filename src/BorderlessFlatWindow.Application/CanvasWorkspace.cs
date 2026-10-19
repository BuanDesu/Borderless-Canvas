using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.Application;

/// <summary>In-memory navigation model. It never moves or activates a desktop window.</summary>
public sealed class CanvasWorkspace
{
    private readonly Dictionary<Guid, CanvasWindow> windows = [];
    private readonly BookmarkCollection bookmarks;
    private readonly List<PointD> history = [];
    private const int HistoryLimit = 64;

    public CanvasWorkspace(SizeD viewportSize, BookmarkCollection? bookmarks = null)
    { Camera = new(PointD.Zero, viewportSize); this.bookmarks = bookmarks ?? new(); }

    public Camera2D Camera { get; private set; }
    public IReadOnlyList<CanvasWindow> Windows => windows.Values.ToArray();
    public IReadOnlyList<LocationBookmark> Bookmarks => bookmarks.Items;

    public void UpsertWindow(CanvasWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Id == Guid.Empty || !window.Bounds.Size.IsPositive)
            throw new ArgumentException("A window needs a valid identity and bounds.", nameof(window));
        windows[window.Id] = window;
    }

    public bool RemoveWindow(Guid id) => windows.Remove(id);

    public void SetViewportSize(SizeD size) => Camera = new(Camera.Position, size);

    public void ResetWindowsAndNavigation()
    {
        windows.Clear(); history.Clear(); Camera = Camera.At(PointD.Zero);
    }

    public void SetCameraPosition(PointD position) => Camera = Camera.At(position);

    public void RecordNavigationOrigin(PointD origin)
    {
        if (origin == Camera.Position) return;
        history.Add(origin);
        if (history.Count > HistoryLimit) history.RemoveAt(0);
    }

    public PointD? PreviousView => history.Count == 0 ? null : history[^1];

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
        return bookmarks.Add(name, Camera.Position);
    }

    public bool RemoveBookmark(Guid id) => bookmarks.Remove(id);

    public bool GoToBookmark(Guid id)
    {
        var bookmark = bookmarks.Items.FirstOrDefault(entry => entry.Id == id);
        if (bookmark is null)
            return false;
        NavigateTo(bookmark.CameraPosition);
        return true;
    }
}
