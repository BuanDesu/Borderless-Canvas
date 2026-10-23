using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>One process-owned mouse sink, no NOLEGACY flag and no suppression of another application's input.</summary>
public sealed class RawPointerInput : IDisposable
{
    private static int processOwner;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly byte[] buffer = new byte[256];
    private nint window;
    private bool disposed;
    public nint Handle => window;
    public RawPointerInput()
    {
        if (Interlocked.CompareExchange(ref processOwner, 1, 0) != 0) throw new InvalidOperationException("本进程已有相对鼠标接收器。");
        try
        {
            window = NativeMethods.CreateWindowExW(0x08000080, "STATIC", "", 0, 0, 0, 0, 0, -3, 0, 0, 0);
            if (window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法创建相对鼠标接收器。");
            Device[] devices = [new() { Page = 1, Usage = 2, Flags = 0x100, Target = window }];
            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Device>())) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法注册相对鼠标。");
        }
        catch { if (window != 0) NativeMethods.DestroyWindow(window); window = 0; Interlocked.Exchange(ref processOwner, 0); throw; }
    }
    public bool Read(nint packet, out PointD delta, out bool absolute)
    {
        delta = PointD.Zero; absolute = false;
        if (disposed || Environment.CurrentManagedThreadId != thread) return false;
        uint length = 0; var header = (uint)(8 + IntPtr.Size * 2);
        if (GetRawInputDataSize(packet, 0x10000003, 0, ref length, header) != 0 || length < header + 24 || length > buffer.Length) return false;
        if (GetRawInputData(packet, 0x10000003, buffer, ref length, header) != length) return false;
        return Decode(buffer.AsSpan(0, (int)length), IntPtr.Size, out delta, out absolute);
    }
    public static bool Decode(ReadOnlySpan<byte> bytes, int pointerSize, out PointD delta, out bool absolute)
    {
        delta = PointD.Zero; absolute = false;
        if (pointerSize is not (4 or 8)) return false;
        var header = 8 + pointerSize * 2;
        if (bytes.Length < header + 24 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length) return false;
        absolute = (BinaryPrimitives.ReadUInt16LittleEndian(bytes[header..]) & 1) != 0;
        if (absolute) return false; // Absolute/tablet/remote packets do not encode movement beyond a clipped edge.
        delta = new(BinaryPrimitives.ReadInt32LittleEndian(bytes[(header + 12)..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[(header + 16)..]));
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("相对鼠标接收器必须在创建线程释放。");
        disposed = true;
        Device[] devices = [new() { Page = 1, Usage = 2, Flags = 1 }];
        RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Device>());
        if (window != 0) NativeMethods.DestroyWindow(window); window = 0; Interlocked.Exchange(ref processOwner, 0);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Device { internal ushort Page, Usage; internal uint Flags; internal nint Target; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputData")] private static extern uint GetRawInputDataSize(nint input, uint command, nint data, ref uint size, uint header);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, [Out] byte[] data, ref uint size, uint header);
}
