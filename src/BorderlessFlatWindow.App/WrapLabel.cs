namespace BorderlessFlatWindow.App;

/// <summary>Docked explanatory text measures its actual font instead of clipping to a fixed pixel height.</summary>
internal sealed class WrapLabel : Label
{
    private bool fitting;
    internal void FitText(int minimum = 0)
    {
        if (fitting || Width <= Padding.Horizontal) return; fitting = true;
        try
        {
            var size = TextRenderer.MeasureText(Text, Font, new(Width - Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var height = Math.Max(minimum, size.Height + Padding.Vertical + 6);
            if (Height != height) Height = height;
        }
        finally { fitting = false; }
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitText(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitText(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitText(); }
}
