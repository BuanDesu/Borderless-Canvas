using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private readonly INavigationInputPlatform platform;
    private readonly bool nativeInput;
    private long InputTime => platform.TickCount;
    private nint ForegroundWindow => platform.Foreground;
    private bool Down(int key) => platform.Down(key);
    private bool MenuActive() => platform.MenuActive;
    private bool IsDesktop(Point point) => nativeInput && touchEnabled ? NativeIsDesktopRoot(WindowUnderTouchReceiver(point)) : platform.IsDesktop(new(point.X, point.Y));
    private bool GetPointer(out Point point)
    {
        var valid = platform.GetPointer(out var position);
        point = new() { X = (int)position.X, Y = (int)position.Y }; return valid;
    }
    private bool ReadPointerClip(out CursorRect rectangle)
    {
        var valid = platform.ReadClip(out var value);
        rectangle = new() { Left = value.Left, Top = value.Top, Right = value.Right, Bottom = value.Bottom }; return valid;
    }
    private bool WritePointerClip(CursorRect? rectangle) => platform.WriteClip(rectangle is { } value ? new(value.Left, value.Top, value.Right, value.Bottom) : null);
    private bool SetPointer(Point point) => platform.SetPointer(new(point.X, point.Y));
    private bool RegisterKey(int id, uint modifiers, uint key) => !nativeInput || RegisterHotKey(0, id, modifiers, key);
    private void ReleaseKey(int id) { if (nativeInput) UnregisterHotKey(0, id); }

    private sealed class WindowsNavigationPlatform : INavigationInputPlatform
    {
        public long TickCount => Environment.TickCount64;
        public nint Foreground => WindowEventListener.ForegroundWindow;
        public bool MenuActive => NativeMenuActive();
        public bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        public bool IsDesktop(PointD point) => NativeIsDesktop(new() { X = (int)point.X, Y = (int)point.Y });
        public bool GetPointer(out PointD point)
        { var result = NativeGetCursorPos(out var value); point = new(value.X, value.Y); return result; }
        public PixelRect VirtualScreen
        {
            get
            {
                var x = GetSystemMetrics(76); var y = GetSystemMetrics(77); var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
                if (width <= 0 || height <= 0 || (long)x + width > int.MaxValue || (long)y + height > int.MaxValue) return default;
                return new(x, y, x + width, y + height);
            }
        }
        public PixelRect PrimaryScreen
        {
            get { var width = GetSystemMetrics(0); var height = GetSystemMetrics(1); return width > 0 && height > 0 ? new(0, 0, width, height) : default; }
        }
        public bool ReadClip(out PixelRect rectangle)
        {
            rectangle = default;
            if (!GetClipCursor(out var value) || value.Right <= value.Left || value.Bottom <= value.Top) return false;
            rectangle = new(value.Left, value.Top, value.Right, value.Bottom); return true;
        }
        public bool WriteClip(PixelRect? rectangle)
        {
            if (rectangle is not { } value) return ClipCursorNone(0);
            var native = new CursorRect { Left = value.Left, Top = value.Top, Right = value.Right, Bottom = value.Bottom };
            return ClipCursor(ref native);
        }
        public bool SetPointer(PointD point) => SetCursorPos((int)point.X, (int)point.Y);
    }
}
