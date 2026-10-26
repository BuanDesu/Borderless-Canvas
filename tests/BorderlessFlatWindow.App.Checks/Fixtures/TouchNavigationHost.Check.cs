using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class TouchNavigationHost
{
    internal static void CheckOwnedInput(string output)
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using var host = new TouchNavigationHost();
        var inputs = new List<NavigationInput>(); var exits = 0;
        host.Input += inputs.Add; host.ExitRequested += () => exits++;
        using (var surface = new InputWindow(host, passive: true) { Bounds = new(100, 100, 320, 240), Opacity = 0 })
        {
            SendOwnMessage(surface.Handle, 0x20A, 1 << 16, 0);
            SendOwnMessage(surface.Handle, 0x20E, unchecked(-40 << 16), 0);
            if (inputs.Count != 2 || inputs[0].WheelDelta != 1 || inputs[1].Kind != NavigationInputKind.HorizontalWheel || inputs[1].WheelDelta != -40)
                throw new InvalidOperationException("Owned wheel messages lost precision or the horizontal axis.");
            inputs.Clear();
            SendOwnMessage(surface.Handle, 0x20A, (40 << 16) | 8, 0);
            SendOwnMessage(surface.Handle, 0x20A, (80 << 16) | 8, 0);
            if (inputs.Count != 1 || inputs[0].Kind != NavigationInputKind.CycleWindow || inputs[0].WheelDelta != 120)
                throw new InvalidOperationException("Ctrl modifier from the window message was ignored.");
            if (surface.Visible || surface.TopMost || surface.ShowInTaskbar || surface.Opacity > .04)
                throw new InvalidOperationException("Touch fixture unexpectedly became a visible or topmost surface.");
        }
        inputs.Clear();
        for (var i = 0; i < 120; i++) host.AcceptWheel(1, false, false, true);
        if (inputs.Count != 1 || inputs[0].Kind != NavigationInputKind.CycleWindow || inputs[0].WheelDelta != 120)
            throw new InvalidOperationException("Touchpad receiver expanded precision Ctrl-wheel.");
        inputs.Clear();
        foreach (var width in new[] { 320, 440, 860 })
        {
            using var tools = host.CreateTools(new(-800, 100, width, 600), passive: true); tools.Opacity = 0; tools.Show();
            var buttons = tools.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
            if (buttons.Length != 2 || buttons.Any(button => button.Bounds.Right > button.Parent!.ClientSize.Width) || tools.TopMost
                || tools.FormBorderStyle != FormBorderStyle.None || tools.BackColor != Color.FromArgb(25, 42, 52) || tools.ForeColor != Color.FromArgb(140, 250, 212))
                throw new InvalidOperationException("Touch controls do not fit a compact work area.");
            foreach (var button in buttons)
                if (TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > button.ClientSize.Width - 12)
                    throw new InvalidOperationException("Touch action text would wrap or be clipped.");
            using var image = new Bitmap(tools.Width, tools.Height);
            tools.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, $"touch-tools-{width}.png"));
            foreach (var button in buttons) button.PerformClick();
            tools.Close(); System.Windows.Forms.Application.DoEvents();
        }
        if (exits != 6 || inputs.Count != 3 || WindowEventListener.ForegroundWindow != foreground)
            throw new InvalidOperationException("Touch toolbar lost commands or activated during an invisible fixture.");
        Console.WriteLine("PASS Touch feedback shares dark translucent navigation chrome, keeps only overview/exit and handles precision wheel without activation");
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendOwnMessage(nint window, uint message, nint wParam, nint lParam);
}
