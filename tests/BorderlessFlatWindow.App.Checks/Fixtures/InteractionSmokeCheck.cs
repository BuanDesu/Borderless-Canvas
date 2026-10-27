using System.Collections.Concurrent;

namespace BorderlessFlatWindow.App;

internal static class InteractionSmokeCheck
{
    internal static void Run(string directory)
    {
        var received = new ConcurrentQueue<string>(); var name = "BFW.Check." + Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, "Synthetic shortcut.lnk");
        using (var channel = new ShortcutCommandChannel(received.Enqueue, name))
        {
            var delivery = ShortcutCommandChannel.Send(path, name).GetAwaiter().GetResult();
            if (delivery != ShortcutDelivery.Delivered || !received.TryDequeue(out var first) || first != path)
            {
                File.WriteAllText(Path.Combine(directory, "shortcut-channel-failure.txt"), $"{delivery}; {channel.Diagnostics}; received={received.Count}");
                throw new InvalidOperationException($"Own authenticated command channel lost its literal request ({delivery}; diagnostic saved locally).");
            }
            try { using var collision = new ShortcutCommandChannel(_ => { }, name); throw new Exception("Duplicate first-pipe owner accepted."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            if (ShortcutCommandChannel.Send(path + ".txt", name).GetAwaiter().GetResult() != ShortcutDelivery.Unavailable || !received.IsEmpty)
                throw new InvalidOperationException("Invalid command reached a launch callback.");
            var resumed = ShortcutCommandChannel.Send(path, name).GetAwaiter().GetResult();
            if (resumed != ShortcutDelivery.Delivered) throw new InvalidOperationException($"Rejected command stopped the pipe service: {resumed}; {channel.Diagnostics}");
        }
        if (ShortcutCommandChannel.Send(path, name).GetAwaiter().GetResult() != ShortcutDelivery.NotRunning) throw new InvalidOperationException("Disposed command server remained available.");
        Console.WriteLine("PASS Current-user command pipe preserves one literal shortcut path, rejects invalid and duplicate ownership, recovers and releases without launching or registering shell verbs");
        using var header = new CanvasHeader { Size = new(432, 78) };
        header.CreateControl(); var scale = header.DeviceDpi / 96f; header.Size = new((int)(432 * scale), (int)(78 * scale)); header.PerformLayout();
        if (header.CollapseButton.Size != new Size((int)(48 * scale), (int)(48 * scale)) || header.CollapseButton.Top != (int)(12 * scale) || header.CollapseButton.Right != header.Width - (int)(8 * scale))
            throw new InvalidOperationException("Pixel header accessories lost shared size or baseline.");
        using var image = new Bitmap(header.Width, header.Height); header.DrawToBitmap(image, header.ClientRectangle);
        image.Save(Path.Combine(directory, "header-accessories.png"));
        Console.WriteLine("PASS Embedded pixel heart and button SVGs share equal accessory slots and a real accessible collapse control");
        var foreground = BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow;
        using var crosshair = new CrosshairOverlay();
        crosshair.Present(new(100, 100)); System.Windows.Forms.Application.DoEvents();
        if (BorderlessFlatWindow.Windows.WindowEventListener.ForegroundWindow != foreground)
            throw new InvalidOperationException("Showing the crosshair overlay changed foreground and would end its input mode.");
        crosshair.Hide();
        Console.WriteLine("PASS The crosshair overlay presents without taking foreground");
    }
}
