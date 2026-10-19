using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

internal static class AppearanceChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Wallpaper palette follows dominant warm and cool hue families", Dominant);
        yield return ("Neutral, empty and near-black samples keep the readable default palette", Neutral);
        yield return ("Adaptive accents stay colorful and permit a legible black or white foreground", Contrast);
        yield return ("Wallpaper palette is independent of sample enumeration order", Order);
        yield return ("Fill and fit samples account for cropping and letterboxes", Crop);
        yield return ("Wallpaper span, tile and stretch support negative monitor origins", MultiMonitor);
        yield return ("Overview motion reaches exact endpoints and clamps elapsed progress", Endpoints);
        yield return ("Overview motion shrinks monotonically without moving the world model", Motion);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Dominant()
    {
        var warm = CandyPalette.FromSamples([new(220, 80, 30), new(200, 70, 20), new(20, 120, 200)]);
        var cool = CandyPalette.FromSamples([new(20, 90, 220), new(30, 100, 210), new(210, 80, 20)]);
        Require(warm.Primary.Hue is >= 0 and < 45 && cool.Primary.Hue is > 190 and < 250
            && CandyPalette.FromSamples([new(255, 0, 0)]).Primary.Hue < 10, "Dominant hue family was lost.");
    }
    private static void Neutral() => Require(CandyPalette.FromSamples([]) == CandyPalette.Default
        && CandyPalette.FromSamples([new(0, 0, 0), new(255, 255, 255), new(90, 90, 90), new(8, 3, 2)]) == CandyPalette.Default, "Neutral fallback was unstable.");
    private static void Contrast()
    {
        for (var hue = 0; hue < 360; hue += 5)
        {
            var p = CandyPalette.FromSamples([Rgb.Hsv(hue, .8, .8)]);
            foreach (var c in new[] { p.Primary, p.Secondary, p.Tertiary, p.Quaternary })
                Require(c.Saturation >= .6 && Math.Max(c.Contrast(new(0, 0, 0)), c.Contrast(new(255, 255, 255))) >= 4.5, "Accent cannot support readable text.");
        }
    }
    private static void Order()
    {
        Rgb[] samples = [new(240, 80, 20), new(20, 80, 230), new(220, 70, 20), new(30, 110, 210)];
        Require(CandyPalette.FromSamples(samples) == CandyPalette.FromSamples(samples.Reverse()), "Order changed the palette.");
    }
    private static void Crop()
    {
        var monitor = new RectD(new(0, 0), new(200, 100));
        Require(WallpaperSampling.ToImage(new(0, 50), new(100, 100), monitor, monitor, WallpaperPlacement.Fill) == new PointD(0, 50), "Fill crop incorrect.");
        Require(WallpaperSampling.ToImage(new(10, 50), new(100, 100), monitor, monitor, WallpaperPlacement.Fit) is null
            && WallpaperSampling.ToImage(new(100, 50), new(100, 100), monitor, monitor, WallpaperPlacement.Fit) == new PointD(50, 50), "Fit letterbox incorrect.");
    }
    private static void MultiMonitor()
    {
        var desktop = new RectD(new(-200, 0), new(400, 100)); var monitor = new RectD(new(-200, 0), new(200, 100));
        Require(WallpaperSampling.ToImage(new(-100, 50), new(400, 100), monitor, desktop, WallpaperPlacement.Span) == new PointD(100, 50), "Span did not use desktop bounds.");
        Require(WallpaperSampling.ToImage(new(-190, 10), new(40, 40), monitor, desktop, WallpaperPlacement.Tile) == new PointD(10, 10), "Tile origin incorrect.");
        Require(WallpaperSampling.ToImage(new(-100, 50), new(80, 40), monitor, desktop, WallpaperPlacement.Stretch) == new PointD(40, 20), "Stretch incorrect.");
    }
    private static void Endpoints()
    {
        var a = new RectD(new(-300, 20), new(800, 600)); var b = new RectD(new(150, 90), new(200, 150));
        Require(OverviewMotion.Between(a, b, -1) == a && OverviewMotion.Between(a, b, 1) == b && OverviewMotion.Between(a, b, 2) == b, "Motion missed endpoints.");
        try { OverviewMotion.Ease(double.NaN); throw new InvalidOperationException("Invalid progress accepted."); } catch (ArgumentOutOfRangeException) { }
    }
    private static void Motion()
    {
        var a = new RectD(new(-300, 20), new(800, 600)); var b = new RectD(new(150, 90), new(200, 150)); var width = a.Size.Width;
        for (var i = 0; i <= 100; i++) { var r = OverviewMotion.Between(a, b, i / 100d); Require(r.Size.Width <= width && r.Size.IsPositive, "Motion overshot."); width = r.Size.Width; }
    }
}
