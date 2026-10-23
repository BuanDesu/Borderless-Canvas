using System.Drawing.Drawing2D;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BorderlessFlatWindow.App;

/// <summary>Only our embedded rect-only SVGs; integral cells keep both header accessories equally weighted.</summary>
internal static class PixelAccessory
{
    private static readonly Dictionary<string, XElement> assets = [];
    internal static void Draw(Graphics graphics, RectangleF slot, string name, Color accent)
    {
        if (!assets.TryGetValue(name, out var root))
        {
            using var stream = typeof(PixelAccessory).Assembly.GetManifestResourceStream($"BorderlessFlatWindow.App.Assets.pixel-{name}.svg")
                ?? throw new InvalidOperationException("Header artwork unavailable.");
            using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            assets[name] = root = XElement.Load(reader);
        }
        var cell = Math.Max(1, MathF.Floor(Math.Min(slot.Width, slot.Height) / 16));
        var x = MathF.Round(slot.X + (slot.Width - 16 * cell) / 2); var y = MathF.Round(slot.Y + (slot.Height - 16 * cell) / 2);
        var state = graphics.Save(); graphics.SmoothingMode = SmoothingMode.None;
        foreach (var rect in root.Elements())
        {
            float N(string field) => float.Parse((string)rect.Attribute(field)!, CultureInfo.InvariantCulture);
            var color = (string?)rect.Attribute("data-tone") switch
            { "accent" => accent, "light" => ChromeTheme.Mix(accent, Color.White, .62f), "dark" => ChromeTheme.Mix(accent, ChromeTheme.Text, .48f), "white" => Color.White, _ => ChromeTheme.Ink };
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, x + N("x") * cell, y + N("y") * cell, N("width") * cell, N("height") * cell);
        }
        graphics.Restore(state);
    }
}
