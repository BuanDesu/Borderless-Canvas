namespace BorderlessFlatWindow.App;

/// <summary>Shared dark, translucent navigation chrome for mouse and touch feedback.</summary>
internal class NavigationOverlay : Form
{
    private readonly bool mouseTransparent;
    internal NavigationOverlay(bool mouseTransparent)
    {
        this.mouseTransparent = mouseTransparent;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(25, 42, 52); ForeColor = Color.FromArgb(140, 250, 212);
        Font = PixelFont.Create(12); Opacity = .88;
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); if (Width <= 0 || Height <= 0) return;
        using var round = ChromeTheme.Round(new Rectangle(0, 0, Width, Height), 18); var previous = Region; Region = new(round); previous?.Dispose();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    { get { var value = base.CreateParams; value.ExStyle |= 0x08000080 | (mouseTransparent ? 0x20 : 0); return value; } }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x21) { message.Result = 3; return; }
        if (mouseTransparent && message.Msg == 0x84) { message.Result = -1; return; }
        base.WndProc(ref message);
    }
}
