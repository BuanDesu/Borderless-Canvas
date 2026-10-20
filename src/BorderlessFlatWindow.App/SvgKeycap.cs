using System.Drawing.Drawing2D;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BorderlessFlatWindow.App;

/// <summary>Renderer for our two embedded SVG keycaps (rect, ellipse, two-stop gradients). No external content.</summary>
internal static class SvgKeycap
{
    private static readonly Dictionary<bool, XElement> artwork = [];
    internal static void Draw(Graphics graphics, RectangleF bounds, Color accent, bool square)
    {
        if (!artwork.TryGetValue(square, out var root))
        {
            using var stream = typeof(SvgKeycap).Assembly.GetManifestResourceStream($"BorderlessFlatWindow.App.Assets.keycap-{(square ? "square" : "round")}.svg")!;
            using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            artwork[square] = root = XElement.Load(reader);
        }
        var saved = graphics.Save(); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 100, bounds.Height / 100);
        var gradients = root.Descendants().Where(e => e.Name.LocalName == "linearGradient").ToDictionary(e => (string)e.Attribute("id")!);
        Color ColorOf(string name, float opacity = 1) => Color.FromArgb((int)(255 * opacity), name switch
        { "accent" => accent, "tint" => ChromeTheme.Mix(accent, Color.White, .58f), "shadow" => ChromeTheme.Mix(accent, Color.Black, .36f), "white" => Color.White, _ => ColorTranslator.FromHtml(name) });
        foreach (var element in root.Elements().Where(e => e.Name.LocalName is "rect" or "ellipse"))
        {
            float N(string name, float fallback = 0) => float.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
            var ellipse = element.Name.LocalName == "ellipse";
            var r = ellipse ? new RectangleF(N("cx") - N("rx"), N("cy") - N("ry"), N("rx") * 2, N("ry") * 2) : new RectangleF(N("x"), N("y"), N("width"), N("height"));
            using var path = ellipse ? new GraphicsPath() : ChromeTheme.Round(r, N("rx")); if (ellipse) path.AddEllipse(r);
            var fill = (string)element.Attribute("fill")!;
            Brush brush;
            if (fill.StartsWith("url(#", StringComparison.Ordinal))
            {
                var gradient = gradients[fill[5..^1]]; var stops = gradient.Elements().ToArray();
                Color Stop(XElement stop) => ColorOf((string)stop.Attribute("stop-color")!, float.TryParse((string?)stop.Attribute("stop-opacity"), CultureInfo.InvariantCulture, out var a) ? a : 1);
                brush = new LinearGradientBrush(r, Stop(stops[0]), Stop(stops[^1]), float.Parse((string)gradient.Attribute("data-angle")!, CultureInfo.InvariantCulture));
            }
            else brush = new SolidBrush(ColorOf(fill, N("opacity", 1)));
            using (brush) graphics.FillPath(brush, path);
            if (element.Attribute("stroke") is { } stroke) { using var pen = new Pen(ColorOf(stroke.Value), N("stroke-width", 1)); graphics.DrawPath(pen, path); }
        }
        graphics.Restore(saved);
    }
}
