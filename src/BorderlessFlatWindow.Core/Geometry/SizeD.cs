namespace BorderlessFlatWindow.Core.Geometry;

public readonly record struct SizeD
{
    public SizeD(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive and finite.");
        Width = width;
        Height = height;
    }

    public double Width { get; }
    public double Height { get; }
    public bool IsPositive => Width > 0 && Height > 0;
}
