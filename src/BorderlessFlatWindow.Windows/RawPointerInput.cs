using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

public readonly record struct RawPointerSample(PointD Value, bool Absolute = false, bool VirtualDesktop = false, nint Device = 0, ushort Buttons = 0);

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
        if (!ReadSample(packet, out var sample)) return false;
        absolute = sample.Absolute; if (absolute) return false;
        delta = sample.Value; return true;
    }
    public bool ReadSample(nint packet, out RawPointerSample sample)
    {
        sample = default;
        if (disposed || Environment.CurrentManagedThreadId != thread) return false;
        uint length = 0; var header = (uint)(8 + IntPtr.Size * 2);
        if (GetRawInputDataSize(packet, 0x10000003, 0, ref length, header) != 0 || length < header + 24 || length > buffer.Length) return false;
        if (GetRawInputData(packet, 0x10000003, buffer, ref length, header) != length) return false;
        return DecodeSample(buffer.AsSpan(0, (int)length), IntPtr.Size, out sample);
    }
    public static bool Decode(ReadOnlySpan<byte> bytes, int pointerSize, out PointD delta, out bool absolute)
    {
        delta = PointD.Zero; absolute = false;
        if (!DecodeSample(bytes, pointerSize, out var sample)) return false;
        absolute = sample.Absolute; if (absolute) return false;
        delta = sample.Value; return true;
    }
    public static bool DecodeSample(ReadOnlySpan<byte> bytes, int pointerSize, out RawPointerSample sample)
    {
        sample = default;
        if (pointerSize is not (4 or 8)) return false;
        var header = 8 + pointerSize * 2;
        if (bytes.Length < header + 24 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length) return false;
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes[header..]);
        var absolute = (flags & 1) != 0;
        var value = new PointD(BinaryPrimitives.ReadInt32LittleEndian(bytes[(header + 12)..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[(header + 16)..]));
        if (absolute && (value.X is < 0 or > 65535 || value.Y is < 0 or > 65535)) return false;
        var device = pointerSize == 8 ? unchecked((nint)BinaryPrimitives.ReadInt64LittleEndian(bytes[8..])) : unchecked((nint)BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]));
        sample = new(value, absolute, (flags & 2) != 0, device, BinaryPrimitives.ReadUInt16LittleEndian(bytes[(header + 4)..]));
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
