using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

public sealed record CanvasWindow(Guid Id, string Title, RectD Bounds);
public sealed record LocationBookmark(Guid Id, string Name, PointD CameraPosition);
