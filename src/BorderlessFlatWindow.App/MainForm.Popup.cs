using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Windows;
using Microsoft.Win32;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer dismissPopup = new() { Interval = 160 };
    private bool followWallpaper = true, samplingWallpaper, popupServices, resamplePending;
    private CandyPalette? deferredPalette;
    private void ConfigurePopup(Control header)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = reviewPanel; TopMost = true;
        StartPosition = FormStartPosition.Manual; DoubleBuffered = true; Padding = new(6);
        var collapse = new Button { Text = "×", AccessibleName = "收起面板", Size = new(38, 36), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Controls.Add(collapse); collapse.Location = new(header.Width - 48, 17);
        header.SizeChanged += (_, _) => collapse.Location = new(header.Width - collapse.Width - 12, Math.Max(8, (header.Height - collapse.Height) / 2 - 3));
        collapse.Click += (_, _) => Hide(); ChromeTheme.Apply(collapse);
        Deactivate += (_, _) => { if (!IsDisposed) dismissPopup.Start(); };
        dismissPopup.Tick += (_, _) =>
        {
            dismissPopup.Stop();
            if (!reviewPanel && Visible && !management.HasOverview && !OwnedForms.Any(f => f.Visible) && !ForegroundWindowBookmark.IsApplicationForeground) Hide();
        };
        Resize += (_, _) => UpdatePopupRegion();
        DockPopup(Screen.PrimaryScreen ?? Screen.FromControl(this)); UpdatePopupRegion();
    }
    private void UpdatePopupRegion()
    {
        if (showWithoutActivation || Width < 1 || Height < 1) return;
        using var path = ChromeTheme.Round(ClientRectangle, 28 * DeviceDpi / 96f);
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    private void DockPopup(Screen monitor)
    {
        if (showWithoutActivation) return;
        var scale = DeviceDpi / 96f; var area = monitor.WorkingArea; var gap = Math.Max(8, (int)(12 * scale));
        var width = Math.Min((int)(432 * scale), area.Width - gap * 2);
        var height = Math.Min((int)(700 * scale), area.Height - gap * 2);
        MinimumSize = new(Math.Min((int)(360 * scale), width), Math.Min((int)(540 * scale), height));
        Bounds = new(area.Right - width - gap, area.Bottom - height - gap, width, height);
    }
    private void StartPopupServices()
    {
        if (popupServices) return; popupServices = true;
        SystemEvents.UserPreferenceChanged += WallpaperPreferenceChanged;
        DockPopup(Screen.FromControl(this)); _ = RefreshPaletteAsync();
    }
    private void StopPopupServices()
    {
        dismissPopup.Dispose();
        if (popupServices) SystemEvents.UserPreferenceChanged -= WallpaperPreferenceChanged;
    }
    private void WallpaperPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.Color or UserPreferenceCategory.General)
            try { BeginInvoke(() => _ = RefreshPaletteAsync()); } catch (InvalidOperationException) { }
    }
    private async Task RefreshPaletteAsync()
    {
        if (showWithoutActivation || IsDisposed || Disposing) return;
        if (samplingWallpaper) { resamplePending = true; return; }
        samplingWallpaper = true;
        try
        {
            var next = followWallpaper ? await WallpaperPaletteReader.ReadAsync(Screen.FromControl(this).Bounds) : CandyPalette.Default;
            if (IsDisposed || Disposing) return;
            if (!followWallpaper) next = CandyPalette.Default;
            if (management.HasOverview) deferredPalette = next; else ApplyPalette(next);
        }
        finally
        {
            samplingWallpaper = false;
            if (resamplePending && !IsDisposed && !Disposing) { resamplePending = false; _ = RefreshPaletteAsync(); }
        }
    }
    private void ApplyPalette(CandyPalette next)
    {
        ChromeTheme.SetPalette(next); ChromeTheme.Apply(this); management.RefreshMenus();
        if (trayMenu is not null) ChromeTheme.StyleMenu(trayMenu);
        Invalidate(true);
    }
    internal void ApplyDeferredPalette()
    {
        if (deferredPalette is not { } next || IsDisposed || Disposing) return;
        deferredPalette = null; ApplyPalette(next);
    }
    internal void PreparePopupSmoke()
    {
        var scale = DeviceDpi / 96f;
        ClientSize = new((int)(432 * scale), (int)(700 * scale)); MinimumSize = new((int)(360 * scale), (int)(540 * scale));
        FormBorderStyle = FormBorderStyle.None; Padding = new(6);
        UpdatePopupRegion(); PerformLayout();
    }
}
