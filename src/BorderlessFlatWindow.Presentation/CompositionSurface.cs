using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using Image = System.Windows.Controls.Image;

namespace BorderlessFlatWindow.Presentation;

/// <summary>Uploads two immutable scenes once, then blends them on WPF's composition thread.</summary>
public sealed class CompositionSurface : ElementHost
{
    private readonly Grid scene = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Image foreground;
    private EventHandler? frameHandler;
    private long started;
    private double duration;
    private bool released;
    public event Action<double>? Frame;
    public static int RenderingTier => RenderCapability.Tier >> 16;
    private static bool warmed;
    public static void WarmUp()
    {
        if (warmed) return;
        warmed = true;
        using var pixel = new Bitmap(2, 2);
        using var host = new CompositionSurface(pixel, pixel);
        _ = host.Handle;
    }

    public CompositionSurface(Bitmap background, Bitmap target)
    {
        TabStop = false;
        scene.Children.Add(new Image { Source = Upload(background), Stretch = Stretch.Fill });
        foreground = new() { Source = Upload(target), Stretch = Stretch.Fill, Opacity = 0 };
        scene.Children.Add(foreground);
        Child = scene;
    }

    public void Start(double milliseconds, bool reverse = false)
    {
        ObjectDisposedException.ThrowIf(released, this);
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Stop(); duration = milliseconds;
        var animation = new DoubleAnimation(reverse ? 1 : 0, reverse ? 0 : 1, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
        // Present both textures before starting the clock. First-use GPU upload must not
        // consume the first moving frames (zero-opacity images can defer that upload).
        foreground.Opacity = reverse ? 1 : .001;
        var primed = false;
        TimeSpan last = TimeSpan.MinValue;
        frameHandler = (_, args) =>
        {
            if (args is RenderingEventArgs rendering)
            {
                if (rendering.RenderingTime == last) return;
                last = rendering.RenderingTime;
            }
            if (!primed)
            {
                primed = true; return;
            }
            if (started == 0)
            {
                started = Stopwatch.GetTimestamp(); foreground.BeginAnimation(UIElement.OpacityProperty, animation);
            }
            Frame?.Invoke(Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / duration, 0, 1));
        };
        CompositionTarget.Rendering += frameHandler;
    }

    public void Seek(double progress)
    {
        ObjectDisposedException.ThrowIf(released, this);
        foreground.BeginAnimation(UIElement.OpacityProperty, null);
        foreground.Opacity = 1 - Math.Pow(1 - Math.Clamp(progress, 0, 1), 3);
    }

    public void Stop()
    {
        if (frameHandler is not null) CompositionTarget.Rendering -= frameHandler;
        frameHandler = null;
        started = 0;
        var opacity = foreground.Opacity;
        foreground.BeginAnimation(UIElement.OpacityProperty, null);
        foreground.Opacity = opacity;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !released) { released = true; Stop(); Frame = null; Child = null; scene.Children.Clear(); }
        base.Dispose(disposing);
    }

    private static BitmapSource Upload(Bitmap source)
    {
        // A scoped lock and one managed copy avoid leaking HBITMAP handles. No files or PNG encode/decode.
        using var converted = source.PixelFormat == DrawingPixelFormat.Format32bppPArgb ? null : source.Clone(new Rectangle(System.Drawing.Point.Empty, source.Size), DrawingPixelFormat.Format32bppPArgb);
        var bitmap = converted ?? source;
        var data = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
        try
        {
            var result = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Pbgra32, null,
                data.Scan0, data.Stride * data.Height, data.Stride);
            result.Freeze(); return result;
        }
        finally { bitmap.UnlockBits(data); }
    }
}
