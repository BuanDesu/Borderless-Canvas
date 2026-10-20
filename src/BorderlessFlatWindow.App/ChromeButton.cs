namespace BorderlessFlatWindow.App;

/// <summary>Retains Button accessibility, mnemonics and dialog semantics, owns every painted pixel.</summary>
internal sealed class ChromeButton : Button
{
    internal Action<Graphics>? Artwork;
    internal bool GameShape, SquareShape;
    internal ChromeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ChromeTheme.Ink);
        if (Artwork is { } paint) paint(e.Graphics); else ChromeTheme.PaintButton(this, e.Graphics);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Initialize every buffer pixel. The native region exposes the actual parent outside the keycap.
        e.Graphics.Clear(ChromeTheme.Ink);
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateShape(); }
    internal void UpdateShape()
    {
        if (Width < 8 || Height < 8) return;
        var width = GameShape && !SquareShape ? Math.Min(Width, Height) - 8 : Width - 8;
        var height = GameShape ? Math.Min(Width, Height) - 8 : Height - 9;
        var rect = new RectangleF((Width - width) / 2f, GameShape ? 4 : 2, width, height);
        using var path = ChromeTheme.Round(rect, GameShape ? SquareShape ? height * .25f : height / 2 : 19);
        var old = Region; Region = Artwork is null ? new Region(path) : new Region(ClientRectangle); old?.Dispose();
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
