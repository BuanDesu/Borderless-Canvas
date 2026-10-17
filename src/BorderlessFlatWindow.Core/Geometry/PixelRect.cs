namespace BorderlessFlatWindow.Core.Geometry;

/// <summary>Physical screen pixels with exclusive right/bottom edges; safe for Win32 int sizes.</summary>
public readonly record struct PixelRect
{
    public PixelRect(int left, int top, int right, int bottom)
    {
        var width = (long)right - left;
        var height = (long)bottom - top;
        if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(right), "Pixel bounds need positive Int32 dimensions.");
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
    public bool IsValid => Right > Left && Bottom > Top
        && (long)Right - Left <= int.MaxValue && (long)Bottom - Top <= int.MaxValue;
    public PointD Position => new(Left, Top);
    public SizeD Size => new(Width, Height);
    public RectD ToRectD() => new(Position, Size);
    public bool Contains(PixelRect other) => IsValid && other.IsValid && other.Left >= Left
        && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
    public override string ToString() => $"({Left}, {Top})–({Right}, {Bottom}) · {Width} × {Height}";
}
