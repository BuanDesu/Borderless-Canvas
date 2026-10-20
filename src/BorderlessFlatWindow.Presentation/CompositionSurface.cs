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
using BorderlessFlatWindow.Core.Canvas;

namespace BorderlessFlatWindow.Presentation;

/// <summary>Uploads two immutable scenes once, then blends them on WPF's composition thread.</summary>
public sealed class CompositionSurface : ElementHost
{
    private readonly Grid scene = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Image foreground;
    private EventHandler? frameHandler;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ReversibleTransition motion = new();
    private int generation;
    private bool presented;
    private Image? movingImage;
    private ScaleTransform? movingScale;
    private TranslateTransform? movingOffset;
    private RectangleF movingFrom, movingTo;
    private double pixelScale;
    public bool HasMovingImage => movingImage is not null && movingImage.Visibility == Visibility.Visible;
    private bool released;
    public event Action<double>? Frame;
    public double Position => motion.Value(clock.Elapsed.TotalMilliseconds);
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
        motion.Reset(reverse ? 1 : 0);
        Retarget(!reverse, milliseconds);
    }

    public void ReplaceTarget(Bitmap target) => foreground.Source = Upload(target);
    public void ReplaceBackground(Bitmap background) => ((Image)scene.Children[0]).Source = Upload(background);

    public void SetMovingImage(Bitmap image, RectangleF from, RectangleF to)
    {
        pixelScale = DeviceDpi / 96.0; movingFrom = from; movingTo = to;
        movingScale = new(); movingOffset = new();
        var transform = new TransformGroup(); transform.Children.Add(movingScale); transform.Children.Add(movingOffset);
        movingImage = new Image { Source = Upload(image), Width = from.Width / pixelScale, Height = from.Height / pixelScale,
            Stretch = Stretch.Fill, RenderTransform = transform };
        var canvas = new Canvas { IsHitTestVisible = false }; canvas.Children.Add(movingImage); scene.Children.Add(canvas);
        SetMovingPosition(Position);
    }
    public void ShowMovingImage(bool show) { if (movingImage is not null) movingImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    public void ReplaceMovingImage(Bitmap image) { if (movingImage is not null) movingImage.Source = Upload(image); }
    private void SetMovingPosition(double value)
    {
        if (movingScale is null || movingOffset is null) return;
        movingScale.ScaleX = 1 + (movingTo.Width / movingFrom.Width - 1) * value;
        movingScale.ScaleY = 1 + (movingTo.Height / movingFrom.Height - 1) * value;
        movingOffset.X = (movingFrom.X + (movingTo.X - movingFrom.X) * value) / pixelScale;
        movingOffset.Y = (movingFrom.Y + (movingTo.Y - movingFrom.Y) * value) / pixelScale;
    }
    private void AnimateMovingImage(double from, double to, double milliseconds)
    {
        if (movingScale is null || movingOffset is null) return;
        void Animate(Animatable element, DependencyProperty property, double a, double b)
            => element.BeginAnimation(property, new DoubleAnimation(a, b, TimeSpan.FromMilliseconds(milliseconds))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd });
        Animate(movingScale, ScaleTransform.ScaleXProperty, 1 + (movingTo.Width / movingFrom.Width - 1) * from, 1 + (movingTo.Width / movingFrom.Width - 1) * to);
        Animate(movingScale, ScaleTransform.ScaleYProperty, 1 + (movingTo.Height / movingFrom.Height - 1) * from, 1 + (movingTo.Height / movingFrom.Height - 1) * to);
        Animate(movingOffset, TranslateTransform.XProperty, (movingFrom.X + (movingTo.X - movingFrom.X) * from) / pixelScale, (movingFrom.X + (movingTo.X - movingFrom.X) * to) / pixelScale);
        Animate(movingOffset, TranslateTransform.YProperty, (movingFrom.Y + (movingTo.Y - movingFrom.Y) * from) / pixelScale, (movingFrom.Y + (movingTo.Y - movingFrom.Y) * to) / pixelScale);
    }

    public void Retarget(bool open, double milliseconds)
    {
        ObjectDisposedException.ThrowIf(released, this);
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var current = Position; Stop(); motion.Reset(current);
        var token = ++generation;
        var waitingForTexture = !presented;
        foreground.Opacity = Math.Max(.001, current);
        void BeginMotion()
        {
            var plan = motion.Retarget(open, clock.Elapsed.TotalMilliseconds, milliseconds);
            foreground.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(plan.From, plan.To, TimeSpan.FromMilliseconds(plan.Duration))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd });
            AnimateMovingImage(plan.From, plan.To, plan.Duration);
        }
        if (!waitingForTexture) BeginMotion();
        TimeSpan last = TimeSpan.MinValue;
        frameHandler = (_, args) =>
        {
            if (token != generation || released) return;
            if (args is RenderingEventArgs rendering)
            {
                if (rendering.RenderingTime == last) return;
                last = rendering.RenderingTime;
            }
            if (waitingForTexture)
            {
                waitingForTexture = false; presented = true; BeginMotion(); return;
            }
            Frame?.Invoke(Position);
        };
        CompositionTarget.Rendering += frameHandler;
    }

    public void Seek(double progress)
    {
        ObjectDisposedException.ThrowIf(released, this);
        motion.Reset(progress);
        foreground.BeginAnimation(UIElement.OpacityProperty, null);
        foreground.Opacity = progress;
        ClearMovingAnimations(); SetMovingPosition(progress);
    }

    public void Stop()
    {
        if (frameHandler is not null) CompositionTarget.Rendering -= frameHandler;
        frameHandler = null;
        generation++;
        var opacity = Position; motion.Reset(opacity);
        foreground.BeginAnimation(UIElement.OpacityProperty, null);
        foreground.Opacity = opacity;
        ClearMovingAnimations(); SetMovingPosition(opacity);
    }
    private void ClearMovingAnimations()
    {
        movingScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null); movingScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        movingOffset?.BeginAnimation(TranslateTransform.XProperty, null); movingOffset?.BeginAnimation(TranslateTransform.YProperty, null);
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
