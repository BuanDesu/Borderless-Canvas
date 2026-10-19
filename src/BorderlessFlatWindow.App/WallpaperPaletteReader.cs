using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal static class WallpaperPaletteReader
{
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
