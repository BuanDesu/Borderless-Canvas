using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Appearance;

public enum WallpaperPlacement { Center, Tile, Stretch, Fit, Fill, Span }

public static class WallpaperSampling
{
    /// <summary>Maps a display sample to the source image; null means desktop background/letterboxing.</summary>
    public static PointD? ToImage(PointD point, SizeD image, RectD monitor, RectD desktop, WallpaperPlacement placement)
    {
        if (!image.IsPositive || !Enum.IsDefined(placement)) throw new ArgumentException("Valid image and wallpaper placement required.");
        var target = placement is WallpaperPlacement.Span or WallpaperPlacement.Tile ? desktop : monitor;
        if (placement == WallpaperPlacement.Tile)
        {
            static double Mod(double value, double range) => (value % range + range) % range;
            return new(Mod(point.X - target.Position.X, image.Width), Mod(point.Y - target.Position.Y, image.Height));
        }
        if (placement == WallpaperPlacement.Stretch)
            return new((point.X - target.Position.X) / target.Size.Width * image.Width, (point.Y - target.Position.Y) / target.Size.Height * image.Height);
        var scale = placement == WallpaperPlacement.Center ? 1 : placement == WallpaperPlacement.Fit
            ? Math.Min(target.Size.Width / image.Width, target.Size.Height / image.Height)
            : Math.Max(target.Size.Width / image.Width, target.Size.Height / image.Height);
        var x = (point.X - target.Center.X) / scale + image.Width / 2;
        var y = (point.Y - target.Center.Y) / scale + image.Height / 2;
        return x < 0 || y < 0 || x >= image.Width || y >= image.Height ? null : new(x, y);
    }
}
