namespace BorderlessFlatWindow.Core.Appearance;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public double Value => Math.Max(R, Math.Max(G, B)) / 255d;
    public double Saturation => Value == 0 ? 0 : 1 - Math.Min(R, Math.Min(G, B)) / (255 * Value);
    public double Hue
    {
        get
        {
            var max = Math.Max(R, Math.Max(G, B)); var min = Math.Min(R, Math.Min(G, B)); var delta = max - min;
            if (delta == 0) return 0;
            var hue = max == R ? (G - B) / (double)delta : max == G ? (B - R) / (double)delta + 2 : (R - G) / (double)delta + 4;
            return (hue * 60 + 360) % 360;
        }
    }
    public double Luminance
    {
        get
        {
            static double Linear(byte c) => c / 255d <= .04045 ? c / 255d / 12.92 : Math.Pow((c / 255d + .055) / 1.055, 2.4);
            return .2126 * Linear(R) + .7152 * Linear(G) + .0722 * Linear(B);
        }
    }
    public double Contrast(Rgb other) => (Math.Max(Luminance, other.Luminance) + .05) / (Math.Min(Luminance, other.Luminance) + .05);
    public static Rgb Hsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        var c = value * saturation; var x = c * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - c;
        var (r, g, b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public sealed record CandyPalette(Rgb Primary, Rgb Secondary, Rgb Tertiary, Rgb Quaternary)
{
    public static CandyPalette Default { get; } = new(new(125, 58, 244), new(0, 178, 248), new(255, 55, 157), new(0, 199, 165));
    public static CandyPalette FromSamples(IEnumerable<Rgb> samples)
    {
        var colorful = samples.Where(c => c.Saturation >= .14 && c.Value >= .12).ToArray();
        if (colorful.Length == 0) return Default;
        // Select a dominant hue family rather than averaging opposite hues into a muddy neutral.
        var bin = colorful.GroupBy(c => (int)Math.Floor(c.Hue / 30)).OrderByDescending(g => g.Sum(c => .2 + c.Saturation)).ThenBy(g => g.Key).First();
        var radians = bin.Select(c => (Angle: c.Hue * Math.PI / 180, Weight: .2 + c.Saturation)).ToArray();
        var hue = (Math.Atan2(radians.Sum(c => Math.Sin(c.Angle) * c.Weight), radians.Sum(c => Math.Cos(c.Angle) * c.Weight)) * 180 / Math.PI + 360) % 360;
        var companion = colorful.Where(c => Distance(c.Hue, hue) is >= 35 and <= 115).OrderByDescending(c => c.Saturation).ThenBy(c => c.Hue).FirstOrDefault();
        var second = companion == default ? hue + 42 : companion.Hue;
        return new(Rgb.Hsv(hue, .76, .88), Rgb.Hsv(second, .66, .94), Rgb.Hsv(hue - 36, .72, .96), Rgb.Hsv(hue + 155, .65, .87));
    }
    private static double Distance(double a, double b) => Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));
}
