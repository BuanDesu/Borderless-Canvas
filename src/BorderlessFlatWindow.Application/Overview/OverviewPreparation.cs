using BorderlessFlatWindow.Application.Displays;
using BorderlessFlatWindow.Core.Canvas;

namespace BorderlessFlatWindow.Application.Overview;

/// <summary>Immutable input identity for an unused overview. Contains no native handles or pixels.</summary>
public sealed class OverviewPreparationSnapshot
{
    private readonly CanvasWindow[] windows;
    private readonly DisplayMonitor[] monitors;
    private readonly LocationBookmark[] bookmarks;
    private readonly int[] colors;
    private readonly object owner;
    private readonly bool writable, animate, taskbar;
    private readonly string bookmarkState;
    public Camera2D Camera { get; }
    public IReadOnlyList<CanvasWindow> Windows { get; }
    public IReadOnlyList<DisplayMonitor> Monitors { get; }
    public bool Animate => animate;
    public bool ShowInTaskbar => taskbar;
    public OverviewPreparationSnapshot(object owner, Camera2D camera, IEnumerable<CanvasWindow> windows,
        IEnumerable<DisplayMonitor> monitors, IEnumerable<LocationBookmark> bookmarks, bool writable,
        string bookmarkState, IEnumerable<int> colors, bool animate, bool taskbar)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner)); Camera = camera;
        this.windows = windows.ToArray(); this.monitors = monitors.ToArray(); this.bookmarks = bookmarks.ToArray(); this.colors = colors.ToArray();
        Windows = Array.AsReadOnly(this.windows); Monitors = Array.AsReadOnly(this.monitors);
        this.writable = writable; this.bookmarkState = bookmarkState; this.animate = animate; this.taskbar = taskbar;
    }
    public bool Matches(OverviewPreparationSnapshot? other) => other is not null && ReferenceEquals(owner, other.owner)
        && Camera == other.Camera && writable == other.writable && bookmarkState == other.bookmarkState
        && animate == other.animate && taskbar == other.taskbar && windows.SequenceEqual(other.windows)
        && monitors.SequenceEqual(other.monitors) && bookmarks.SequenceEqual(other.bookmarks) && colors.SequenceEqual(other.colors);
}

/// <summary>One unused resource. A generation rejects late preparation after take, pause or disposal.</summary>
public sealed class OverviewPreparationSlot<T> : IDisposable where T : class, IDisposable
{
    private (OverviewPreparationSnapshot Key, T Resource)? entry;
    private long generation;
    private bool disposed;
    public bool Contains(OverviewPreparationSnapshot key) => !disposed && entry is { } value && value.Key.Matches(key);
    public long Begin()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var ticket = ++generation; var previous = entry; entry = null;
        previous?.Resource.Dispose(); return ticket;
    }
    // Ownership always transfers, including rejection. Callers must not dispose twice.
    public bool Publish(long ticket, OverviewPreparationSnapshot key, T resource)
    {
        if (disposed || ticket != generation || entry is not null) { resource.Dispose(); return false; }
        entry = (key, resource); return true;
    }
    public T? Take(OverviewPreparationSnapshot key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        generation++; var previous = entry; entry = null;
        if (previous is not { } value) return null;
        if (value.Key.Matches(key)) return value.Resource;
        value.Resource.Dispose(); return null;
    }
    public void Invalidate()
    {
        generation++; var previous = entry; entry = null;
        previous?.Resource.Dispose();
    }
    public void Dispose() { if (disposed) return; disposed = true; Invalidate(); }
}
