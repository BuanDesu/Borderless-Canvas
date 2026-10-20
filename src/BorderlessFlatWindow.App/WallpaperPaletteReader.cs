using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

internal static class WallpaperPaletteReader
{
    private sealed record PreviewPixels(int Width, int Height, byte[] Pixels);
    private static readonly object previewGate = new();
    private static Task<PreviewPixels?>? cachedPreview;
    private static string? cachedLayout;
    internal static void PreloadPreview(bool invalidate = false)
    {
        var layout = string.Join(";", Screen.AllScreens.Select(screen => $"{screen.Bounds}:{screen.WorkingArea}"));
        lock (previewGate)
        {
            if (invalidate || cachedLayout != layout) { cachedPreview = null; cachedLayout = layout; }
            cachedPreview ??= ReadPixelsAsync();
        }
    }
    internal static async Task<Bitmap?> ReadPreviewAsync()
    {
        PreloadPreview(); Task<PreviewPixels?> task;
        lock (previewGate) task = cachedPreview ??= ReadPixelsAsync();
        if (await task is not { } pixels) return null;
        var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels.Pixels, 0, data.Scan0, pixels.Pixels.Length); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
    private static async Task<PreviewPixels?> ReadPixelsAsync()
    {
        using var bitmap = await ReadPreviewCoreAsync().ConfigureAwait(false);
        if (bitmap is null) return null;
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new(bitmap.Width, bitmap.Height, pixels);
        }
        finally { bitmap.UnlockBits(data); }
    }
    private static Task<Bitmap?> ReadPreviewCoreAsync()
    {
        var result = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var screens = Screen.AllScreens.Select(s => (s.Bounds, s.WorkingArea)).ToArray();
        var thread = new Thread(() =>
        {
            Bitmap? output = null;
            try
            {
                var area = screens.Select(s => s.WorkingArea).Aggregate(Rectangle.Union);
                var width = 480; var height = Math.Max(1, (int)Math.Round(width * (double)area.Height / area.Width));
                output = new Bitmap(width, height);
                using (var g = Graphics.FromImage(output)) g.Clear(ChromeTheme.Panel);
                foreach (var screen in screens)
                {
                    var r = screen.Bounds;
                    var info = DesktopWallpaperSource.Read(new(r.Left, r.Top, r.Right, r.Bottom));
                    if (info is null) continue;
                    Bitmap? sample = null; SizeD? fullSize = null;
                    try
                    {
                        if (Path.IsPathFullyQualified(info.ImagePath) && !info.ImagePath.StartsWith(@"\\", StringComparison.Ordinal))
                        {
                            using var file = new FileStream(info.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using var image = Image.FromStream(file, false, true);
                            if (image.Width > 0 && image.Height > 0 && (long)image.Width * image.Height <= 64_000_000)
                            { fullSize = new(image.Width, image.Height); sample = new Bitmap(image, new Size(480, 480)); }
                        }
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            var physical = new Point(area.Left + (int)((x + .5) / width * area.Width), area.Top + (int)((y + .5) / height * area.Height));
                            if (!screen.WorkingArea.Contains(physical)) continue;
                            var color = Color.FromArgb(info.Background.R, info.Background.G, info.Background.B);
                            if (sample is not null && fullSize is { } imageSize && WallpaperSampling.ToImage(new(physical.X, physical.Y), imageSize, info.Monitor, info.Desktop, info.Placement) is { } p)
                                color = sample.GetPixel(Math.Clamp((int)(p.X / imageSize.Width * sample.Width), 0, sample.Width - 1), Math.Clamp((int)(p.Y / imageSize.Height * sample.Height), 0, sample.Height - 1));
                            output.SetPixel(x, y, color);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { }
                    finally { sample?.Dispose(); }
                }
                result.TrySetResult(output); output = null;
            }
            catch (Exception e) when (e is not StackOverflowException) { output?.Dispose(); result.TrySetResult(null); }
        })
        { IsBackground = true, Name = "BFW workspace wallpaper" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    internal static Task<CandyPalette> ReadAsync(Rectangle monitor)
    {
        var result = new TaskCompletionSource<CandyPalette>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { result.SetResult(Read(monitor)); }
            catch (Exception e) when (e is not StackOverflowException) { result.TrySetResult(CandyPalette.Default); }
        })
        { IsBackground = true, Name = "BFW wallpaper colors" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task;
    }
    private static CandyPalette Read(Rectangle monitor)
    {
        var info = DesktopWallpaperSource.Read(new(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom));
        if (info is null) return CandyPalette.Default;
        if (string.IsNullOrWhiteSpace(info.ImagePath)) return CandyPalette.FromSamples([info.Background]);
        // Never capture application pixels for color selection. Do not fetch remote/UNC wallpaper files.
        if (!Path.IsPathFullyQualified(info.ImagePath) || info.ImagePath.StartsWith(@"\\", StringComparison.Ordinal)) return CandyPalette.Default;
        try
        {
            using var stream = new FileStream(info.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var image = Image.FromStream(stream, false, true);
            if ((long)image.Width * image.Height > 64_000_000 || image.Width < 1 || image.Height < 1) return CandyPalette.Default;
            using var thumbnail = new Bitmap(128, 128);
            using (var g = Graphics.FromImage(thumbnail)) g.DrawImage(image, new Rectangle(0, 0, 128, 128));
            var samples = new List<Rgb>();
            foreach (var y in new[] { .12, .3, .5, .7, .88 }) foreach (var x in new[] { .12, .3, .5, .7, .88 })
            {
                var point = new PointD(info.Monitor.Position.X + x * info.Monitor.Size.Width, info.Monitor.Position.Y + y * info.Monitor.Size.Height);
                var mapped = WallpaperSampling.ToImage(point, new(image.Width, image.Height), info.Monitor, info.Desktop, info.Placement);
                if (mapped is not { } p) { samples.Add(info.Background); continue; }
                var c = thumbnail.GetPixel(Math.Clamp((int)(p.X / image.Width * 128), 0, 127), Math.Clamp((int)(p.Y / image.Height * 128), 0, 127));
                samples.Add(new((byte)((c.R * c.A + info.Background.R * (255 - c.A)) / 255),
                    (byte)((c.G * c.A + info.Background.G * (255 - c.A)) / 255), (byte)((c.B * c.A + info.Background.B * (255 - c.A)) / 255)));
            }
            return CandyPalette.FromSamples(samples);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { return CandyPalette.Default; }
    }
}
