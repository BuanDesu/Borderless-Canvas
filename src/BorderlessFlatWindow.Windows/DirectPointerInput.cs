using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public readonly record struct DirectPointerSample(uint Id, ContactPhase Phase, PointD Position);

/// <summary>Reads touch/pen messages addressed to an owned HWND, never global pointer input.</summary>
public static class DirectPointerInput
{
    public static bool TryRead(int message, nint wParam, out DirectPointerSample sample)
    {
        sample = default;
        if (message is not (0x245 or 0x246 or 0x247 or 0x24C)) return false;
        var id = unchecked((uint)wParam) & 0xFFFF;
        if (message == 0x24C) { sample = new(id, ContactPhase.Cancel, PointD.Zero); return true; }
        if (!GetPointerInfo(id, out var info) || info.Type is not (2 or 3)) return false;
        var phase = (info.Flags & 0x8000) != 0 ? ContactPhase.Cancel
            : message == 0x246 ? ContactPhase.Down : message == 0x247 ? ContactPhase.Up : ContactPhase.Move;
        sample = new(id, phase, new(info.Pixel.X, info.Pixel.Y)); return true;
    }
    public static bool IsPromotedContactMouse =>
        (unchecked((ulong)GetMessageExtraInfo()) & 0xFFFFFF00) == 0xFF515700;

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct PointerInfo
    {
        internal uint Type, Id, Frame, Flags;
        internal nint Device, Target;
        internal NativePoint Pixel, Himetric, RawPixel, RawHimetric;
        internal uint Time, History;
        internal int InputData;
        internal uint Keys;
        internal ulong Performance;
        internal uint ButtonChange;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPointerInfo(uint id, out PointerInfo info);
    [DllImport("user32.dll")] private static extern nint GetMessageExtraInfo();
}
