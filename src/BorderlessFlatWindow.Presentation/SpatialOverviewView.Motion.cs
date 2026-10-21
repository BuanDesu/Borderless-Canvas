using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using Image = System.Windows.Controls.Image;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace BorderlessFlatWindow.Presentation;

public sealed partial class SpatialOverviewView
{
    private readonly ReversibleTransition motion = new();
    private EventHandler? frameHandler;
    private bool presented;
    private int motionGeneration;
    private Image? focusImage;
    private ScaleTransform? focusScale;
    private TranslateTransform? focusOffset;
    private RectD focusFrom, focusTo;
    public event Action<double>? Frame;

    public void SetBackdrop(Bitmap bitmap)
    {
        var texture = Upload(bitmap); sharp.Source = blurred.Source = texture;
        if (CompositionSurface.RenderingTier > 0)
        {
            var effect = new BlurEffect { Radius = 22, RenderingBias = RenderingBias.Performance }; effect.Freeze(); blurred.Effect = effect; blurred.CacheMode = new BitmapCache(.5);
        }
    }
    public void SetFocusImage(Bitmap bitmap, RectD from, PixelRect to)
    {
        if (!from.Size.IsPositive || !to.IsValid) return;
        focusFrom = from; focusTo = new(to.Position, to.Size); focusScale = new(); focusOffset = new();
        var transform = new TransformGroup(); transform.Children.Add(focusScale); transform.Children.Add(focusOffset);
        focusImage = new() { Source = Upload(bitmap), Width = from.Size.Width / scale, Height = from.Size.Height / scale, Stretch = Stretch.Fill, IsHitTestVisible = false, RenderTransform = transform };
        var overlay = new Canvas { IsHitTestVisible = false, ClipToBounds = true }; overlay.Children.Add(focusImage); root.Children.Add(overlay); SetPosition(Position);
    }
    public void ReplaceFocusImage(Bitmap bitmap) { if (focusImage is not null) focusImage.Source = Upload(bitmap); }
    public void ShowFocusImage(bool value) { if (focusImage is not null) focusImage.Visibility = value ? Visibility.Visible : Visibility.Hidden; }
    public void Retarget(bool open, double milliseconds)
    {
        ObjectDisposedException.ThrowIf(released, this);
        var current = Position; StopMotion(); motion.Reset(current); var generation = ++motionGeneration;
        void Start()
        {
            // The core transition only plans the remaining distance/duration.
            // WPF owns runtime progress; there is no second elapsed-time clock.
            var plan = motion.Retarget(open, 0, milliseconds);
            Animate(content, UIElement.OpacityProperty, plan.From, plan.To, plan.Duration);
            Animate(blurred, UIElement.OpacityProperty, plan.From, plan.To, plan.Duration);
            Animate(sharp, UIElement.OpacityProperty, 1 - plan.From, 1 - plan.To, plan.Duration);
            if (focusScale is not null && focusOffset is not null)
            {
                Animate(focusScale, ScaleTransform.ScaleXProperty, ZoomX(plan.From), ZoomX(plan.To), plan.Duration);
                Animate(focusScale, ScaleTransform.ScaleYProperty, ZoomY(plan.From), ZoomY(plan.To), plan.Duration);
                Animate(focusOffset, TranslateTransform.XProperty, X(plan.From), X(plan.To), plan.Duration);
                Animate(focusOffset, TranslateTransform.YProperty, Y(plan.From), Y(plan.To), plan.Duration);
            }
        }
        var waiting = !presented;
        if (!waiting) Start();
        TimeSpan previous = TimeSpan.MinValue;
        frameHandler = (_, args) =>
        {
            if (released || generation != motionGeneration) return;
            if (args is RenderingEventArgs rendering) { if (previous == rendering.RenderingTime) return; previous = rendering.RenderingTime; }
            if (waiting) { waiting = false; presented = true; Start(); return; }
            Frame?.Invoke(Position);
        };
        CompositionTarget.Rendering += frameHandler;
    }
    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to, double milliseconds)
        => target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd });
    private double ZoomX(double p) => 1 + (focusTo.Size.Width / focusFrom.Size.Width - 1) * p;
    private double ZoomY(double p) => 1 + (focusTo.Size.Height / focusFrom.Size.Height - 1) * p;
    private double X(double p) => (focusFrom.Position.X + (focusTo.Position.X - focusFrom.Position.X) * p) / scale;
    private double Y(double p) => (focusFrom.Position.Y + (focusTo.Position.Y - focusFrom.Position.Y) * p) / scale;
    public void Seek(double value) { StopMotion(); motion.Reset(value); SetPosition(value); }
    public void StopMotion()
    {
        var current = Position;
        if (frameHandler is not null) CompositionTarget.Rendering -= frameHandler;
        frameHandler = null; motionGeneration++; motion.Reset(current);
        content.BeginAnimation(UIElement.OpacityProperty, null); blurred.BeginAnimation(UIElement.OpacityProperty, null); sharp.BeginAnimation(UIElement.OpacityProperty, null);
        focusScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null); focusScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        focusOffset?.BeginAnimation(TranslateTransform.XProperty, null); focusOffset?.BeginAnimation(TranslateTransform.YProperty, null);
        SetPosition(current);
    }
    private void SetPosition(double value)
    {
        content.Opacity = blurred.Opacity = value; sharp.Opacity = 1 - value;
        if (focusScale is not null && focusOffset is not null) { focusScale.ScaleX = ZoomX(value); focusScale.ScaleY = ZoomY(value); focusOffset.X = X(value); focusOffset.Y = Y(value); }
    }
    private static BitmapSource Upload(Bitmap source)
    {
        using var converted = source.PixelFormat == DrawingPixelFormat.Format32bppPArgb ? null : source.Clone(new Rectangle(System.Drawing.Point.Empty, source.Size), DrawingPixelFormat.Format32bppPArgb);
        var bitmap = converted ?? source; var data = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
        try { var texture = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Pbgra32, null, data.Scan0, data.Stride * data.Height, data.Stride); texture.Freeze(); return texture; }
        finally { bitmap.UnlockBits(data); }
    }
}
