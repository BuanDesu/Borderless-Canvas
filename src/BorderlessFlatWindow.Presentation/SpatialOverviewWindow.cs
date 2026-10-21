using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Presentation;

/// <summary>A real WPF top-level window. Its HWND is the preview and input destination.</summary>
public sealed class SpatialOverviewWindow : IDisposable
{
    private readonly Window window;
    private readonly SpatialOverviewView view;
    private readonly PixelRect bounds;
    private readonly Func<nint, bool, bool> configure;
    private bool closed, disposing, shown;
    public event EventHandler? Shown;
    public event EventHandler? Activated;
    public event EventHandler? Closed;
    public bool IsClosed => closed;
    public bool Visible => !closed && window.IsVisible;
    public bool NativeBackdropAccepted { get; private set; }
    public bool ShowInTaskbar { get => window.ShowInTaskbar; set { if (!closed && window.ShowInTaskbar != value) window.ShowInTaskbar = value; } }
    public nint Handle => closed ? 0 : new WindowInteropHelper(window).EnsureHandle();
    public int Dpi => closed ? 96 : (int)GetDpiForWindow(Handle);
    public bool Enabled => !closed && IsWindowEnabled(Handle);
    public SizeD ClientPixels => GetClientRect(Handle, out var rect) ? new(rect.Right, rect.Bottom) : bounds.Size;

    public SpatialOverviewWindow(SpatialOverviewView view, PixelRect bounds, Func<nint, bool, bool> configure, bool showInTaskbar)
    {
        this.view = view; this.bounds = bounds; this.configure = configure;
        window = new Window { Title = "BFW 空间总览", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false, Background = Brushes.Transparent, Topmost = true, ShowActivated = false,
            ShowInTaskbar = showInTaskbar, Width = bounds.Width, Height = bounds.Height, Content = view };
        window.SourceInitialized += Initialize;
        window.SizeChanged += (_, _) => EnsureLayout();
        window.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        window.Closed += (_, _) =>
        {
            if (closed) return;
            closed = true; view.Dispose(); window.Content = null;
            Closed?.Invoke(this, EventArgs.Empty);
            Shown = null; Activated = null; Closed = null;
        };
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window);
    }
    private void Initialize(object? sender, EventArgs args)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor = Colors.Transparent;
        if (!SetWindowPos(handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x214))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        NativeBackdropAccepted = configure(handle, !SystemParameters.HighContrast);
        if (!NativeBackdropAccepted) view.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(228, 232, 247));
        EnsureLayout();
    }
    public void EnsureLayout()
    {
        if (closed) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var pixels = ClientPixels;
        if (pixels.IsPositive) view.SetViewport((int)pixels.Width, (int)pixels.Height, Math.Max(1, Dpi) / 96d);
    }
    public void Show(bool activate)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        window.ShowActivated = activate; window.Show();
        if (closed) return;
        EnsureLayout();
        if (!shown) { shown = true; Shown?.Invoke(this, EventArgs.Empty); }
        if (activate && !closed) window.Activate();
    }
    public void Close() { if (!closed) window.Close(); }
    public void Post(Action action) { if (closed) action(); else window.Dispatcher.BeginInvoke(action); }
    public void Dispose()
    {
        if (closed || disposing) return;
        disposing = true;
        try { window.Close(); }
        finally { if (!closed) view.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
