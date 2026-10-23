using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

// Separates desktop observation/cursor ownership from routing, so the complete input
// worker can be exercised without installing hooks or touching the user's pointer.
internal interface INavigationInputPlatform
{
    long TickCount { get; }
    nint Foreground { get; }
    bool MenuActive { get; }
    bool Down(int key);
    bool IsDesktop(PointD point);
    bool GetPointer(out PointD point);
    PixelRect VirtualScreen { get; }
    bool ReadClip(out PixelRect rectangle);
    bool WriteClip(PixelRect? rectangle);
    bool SetPointer(PointD point);
}
