using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Discovery;

/// <summary>Connects observed physical bounds to persistent world positions at native scale.</summary>
public sealed class DesktopSpatialModel(CanvasWorkspace workspace, PixelRect workArea)
{
    private readonly Dictionary<Guid, ExpectedMove> expected = [];
    private sealed record ExpectedMove(RectD Bounds, long Revision, DateTimeOffset ExpiresAt);
    private PixelRect workArea = workArea.IsValid ? workArea : throw new ArgumentOutOfRangeException(nameof(workArea));
    public PixelRect WorkArea
    {
        get => workArea;
        set => workArea = value.IsValid ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    // Reading/panning an overview does not move the desktop's physical camera.
    public PointD PhysicalCameraPosition { get; set; }
    public Guid? ActiveWindowId { get; private set; }
    public int OwnMoveEchoes { get; private set; }

    public void ExpectMove(Guid id, RectD physicalBounds, long revision, DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || !physicalBounds.Size.IsPositive || revision <= 0)
            throw new ArgumentException("Expected movement needs a window, bounds and positive revision.");
        if (expected.TryGetValue(id, out var prior) && revision <= prior.Revision)
            throw new ArgumentException("Movement revisions must increase.");
        expected[id] = new(physicalBounds, revision, expiresAt);
    }

    public void CancelExpectedMove(Guid id) => expected.Remove(id);

    public void Synchronize(WindowCatalogSnapshot snapshot, nint? foreground, DateTimeOffset now)
    {
        var admitted = snapshot.AdmittedWindows;
        var ids = admitted.Select(window => window.Id).ToHashSet();
        foreach (var window in workspace.Windows.Where(window => !ids.Contains(window.Id))) workspace.RemoveWindow(window.Id);
        foreach (var id in expected.Keys.Where(id => !ids.Contains(id)).ToArray()) expected.Remove(id);
        var mapper = new DesktopCoordinateMapper(PhysicalCameraPosition, WorkArea);
        var previous = workspace.Windows.ToDictionary(window => window.Id);
        foreach (var window in admitted)
        {
            var ownEcho = expected.TryGetValue(window.Id, out var move) && move.ExpiresAt >= now
                && move.Bounds == window.Bounds && previous.ContainsKey(window.Id);
            if (ownEcho)
            {
                workspace.UpsertWindow(previous[window.Id] with { Title = window.Title });
                OwnMoveEchoes++;
                // Keep matching echoes until expiry; DWM may send repeated location events.
            }
            else
            {
                expected.Remove(window.Id);
                workspace.UpsertWindow(new(window.Id, window.Title,
                    new(mapper.PhysicalToWorld(window.Bounds.Position), window.Bounds.Size)));
            }
        }
        ActiveWindowId = admitted.FirstOrDefault(window => window.Handle == foreground)?.Id;
    }
}
