using System.Runtime.InteropServices;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>Only fixture-owned HWNDs receive messages; no desktop input injection or capture.</summary>
internal static class ArrangementDialogCheck
{
    internal static void Run()
    {
        var foreground = WindowEventListener.ForegroundWindow;
        using (var owner = new Owner { Text = "BFW synthetic arrangement owner", TopMost = true, Opacity = 0,
            ShowInTaskbar = false, Bounds = new(100, 100, 500, 400) })
        {
            using var anchor = new Panel { Dock = DockStyle.Fill }; owner.Controls.Add(anchor); owner.Show();
            var region = new RectD(PointD.Zero, new(1200, 800));
            CanvasWindow[] windows = [new(Guid.NewGuid(), "Synthetic A", new(new(50, 60), new(430, 310))),
                new(Guid.NewGuid(), "Synthetic B", new(new(100, 90), new(560, 360)))];
            foreach (var mode in new[] { "cancel", "confirm", "empty" })
            {
                using var dialog = new CanvasArrangementDialog(windows, region, [region], showWithoutActivation: true);
                using var timer = new System.Windows.Forms.Timer { Interval = 40 };
                var ticks = 0; var stage = 0; var owned = false; var disabled = false; var topmost = false; var posted = false; var emptyIgnored = false;
                Exception? failure = null;
                dialog.Shown += (_, _) => timer.Start();
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        if (++ticks > 30) throw new InvalidOperationException("Arrangement modal fixture timed out.");
                        if (stage == 0)
                        {
                            owned = GetWindow(dialog.Handle, 4) == owner.Handle;
                            disabled = !IsWindowEnabled(owner.Handle) && IsWindowEnabled(dialog.Handle);
                            topmost = (GetWindowLongPtr(dialog.Handle, -20).ToInt64() & 8) != 0;
                            if (!owned || !disabled || !topmost) throw new InvalidOperationException("Arrangement modal owner/enable/topmost state failed.");
                            if (mode == "empty")
                            {
                                var list = Descendants(dialog).OfType<CheckedListBox>().Single();
                                for (var i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false);
                                stage = 1; return;
                            }
                            posted = PostMessage(dialog.Handle, 0x100, mode == "confirm" ? (nuint)13 : 27, 0); stage = 3;
                        }
                        else if (stage == 1)
                        {
                            if (dialog.CanApply || dialog.SelectedIds.Length != 0) throw new InvalidOperationException("Empty preview remained actionable.");
                            posted = PostMessage(dialog.Handle, 0x100, 13, 0); stage = 2;
                        }
                        else if (stage == 2)
                        { emptyIgnored = dialog.Visible; posted &= PostMessage(dialog.Handle, 0x100, 27, 0); stage = 3; }
                    }
                    catch (Exception error) { failure = error; timer.Stop(); dialog.DialogResult = DialogResult.Abort; dialog.Close(); }
                };
                var result = dialog.ShowFor(anchor); timer.Stop();
                if (failure is not null) throw failure;
                if (!posted || result != (mode == "confirm" ? DialogResult.OK : DialogResult.Cancel)
                    || !IsWindowEnabled(owner.Handle) || mode == "empty" && !emptyIgnored)
                    throw new InvalidOperationException($"Arrangement modal {mode} failed.");
            }
            owner.Close();
        }
        if (WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Arrangement modal check changed foreground.");
        Console.WriteLine("PASS Arrangement preview uses the real topmost owner, disables/restores it, routes Enter/Escape, and rejects empty confirmation");
    }
    private static IEnumerable<Control> Descendants(Control owner) => owner.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private sealed class Owner : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; } }
    }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
