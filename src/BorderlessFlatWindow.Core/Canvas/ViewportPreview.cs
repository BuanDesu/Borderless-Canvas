using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>A window slice inside a camera viewport, preserving the original relative position and aspect.</summary>
public sealed record ViewportPreview(PixelRect Destination, RectD SourceFraction)
{
    public static ViewportPreview? Project(RectD window, RectD viewport, PixelRect frame, RectD? visibleArea = null)
    {
        if (!window.Size.IsPositive || !viewport.Size.IsPositive || !frame.IsValid) throw new ArgumentException("Positive geometry required.");
        var left = Math.Max(window.Position.X, viewport.Position.X); var top = Math.Max(window.Position.Y, viewport.Position.Y);
        var right = Math.Min(window.Right, viewport.Right); var bottom = Math.Min(window.Bottom, viewport.Bottom);
        if (visibleArea is { } area)
        {
            left = Math.Max(left, area.Position.X); top = Math.Max(top, area.Position.Y);
            right = Math.Min(right, area.Right); bottom = Math.Min(bottom, area.Bottom);
        }
        if (right <= left || bottom <= top) return null;
        var fit = BorderlessFit(viewport.Size, frame);
        var x = (double)fit.Width / viewport.Size.Width; var y = (double)fit.Height / viewport.Size.Height;
        var l = fit.Left + (int)Math.Round((left - viewport.Position.X) * x);
        var t = fit.Top + (int)Math.Round((top - viewport.Position.Y) * y);
        var r = fit.Left + (int)Math.Round((right - viewport.Position.X) * x);
        var b = fit.Top + (int)Math.Round((bottom - viewport.Position.Y) * y);
        if (r <= l || b <= t) return null;
        return new(new(l, t, r, b), new(new((left - window.Position.X) / window.Size.Width, (top - window.Position.Y) / window.Size.Height),
            new((right - left) / window.Size.Width, (bottom - top) / window.Size.Height)));
    }
    public static PixelRect BorderlessFit(SizeD size, PixelRect frame)
    {
        if (!size.IsPositive || !frame.IsValid) throw new ArgumentException("Positive geometry required.");
        var scale = Math.Min(frame.Width / size.Width, frame.Height / size.Height);
        var w = Math.Clamp((int)Math.Round(size.Width * scale), 1, frame.Width);
        var h = Math.Clamp((int)Math.Round(size.Height * scale), 1, frame.Height);
        var l = frame.Left + (frame.Width - w) / 2; var t = frame.Top + (frame.Height - h) / 2;
        return new(l, t, l + w, t + h);
    }
}
