using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class NavigationSettingsDialog
{
    internal void CheckKeyBindings(string output)
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Synthetic settings checks only.");
        var tabs = Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
        var save = (ChromeButton)AcceptButton!;
        var original = bindingFields[2].Key.SelectedItem;
        bindingFields[2].Key.SelectedItem = bindingFields[2].Key.Items.Cast<KeyOption>().Single(value => value.Key == Bindings.Crosshair.Key);
        save.PerformClick();
        if (DialogResult == DialogResult.OK || string.IsNullOrEmpty(error.Text) || !Visible) throw new InvalidOperationException("Duplicate key settings closed or silently accepted.");
        bindingFields[2].Key.SelectedItem = original; error.Text = ""; error.Visible = false; System.Windows.Forms.Application.DoEvents();
        if (bindingFields.Any(pair => pair.Key.Right > pair.Key.Parent!.ClientSize.Width || pair.Modifier.Bottom > pair.Modifier.Parent!.ClientSize.Height)) throw new InvalidOperationException("Key controls are clipped.");
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(output, "navigation-keys-smoke.png"));
        Console.WriteLine("PASS Key settings retain duplicate errors without closing and show all six commands in a synthetic nonactivating fixture");
    }
}
