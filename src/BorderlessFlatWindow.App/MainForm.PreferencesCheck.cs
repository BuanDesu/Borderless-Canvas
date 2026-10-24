using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal static void CheckPreferenceRestart(string directory)
    {
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "settings.json"); var journal = Path.Combine(directory, "recovery.json"); var labels = Path.Combine(directory, "bookmarks.json");
        var foreground = WindowEventListener.ForegroundWindow;
        var expected = new UserPreferences(new NavigationInputOptions(false, true, false, 330), ManualNavigationShortcut.Default, false, false, false)
            { Bindings = NavigationKeyBindings.Default with { Overview = new(3, 0x4E) } };
        using (var first = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true, journalPath: journal, bookmarksPath: labels, settingsPath: settings))
        {
            if (first.management.CurrentPreferencesForCheck != UserPreferences.Default || File.Exists(settings)) throw new InvalidOperationException("Fresh UI modified preferences.");
            first.management.CheckPreferenceBindings();
            if (first.management.CurrentPreferencesForCheck != expected || first.management.Diagnostics().Running) throw new InvalidOperationException("Preference binding changed management state.");
        }
        using (var second = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true, journalPath: journal, bookmarksPath: labels, settingsPath: settings))
            if (second.management.CurrentPreferencesForCheck != expected || second.followWallpaper || second.management.Diagnostics().Running
                || second.management.Diagnostics().Count != 0) throw new InvalidOperationException("Reopened UI lost preferences or automatically managed windows.");
        File.WriteAllText(settings, "{synthetic invalid settings"); var invalid = File.ReadAllBytes(settings);
        using (var broken = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true, journalPath: journal, bookmarksPath: labels, settingsPath: settings))
        {
            if (broken.management.CurrentPreferencesForCheck != (UserPreferences.Default with { AutomaticAdmission = false }) || broken.preferences.Warning is null) throw new InvalidOperationException("Corrupt settings did not degrade safely.");
            broken.management.CheckPreferenceBindings();
            if (!File.ReadAllBytes(settings).SequenceEqual(invalid) || broken.preferences.Current != expected || broken.preferences.CanSave)
                throw new InvalidOperationException("Degraded UI discarded current choices or overwrote invalid settings.");
            broken.Opacity = 0; broken.Show(); broken.PreparePopupSmoke(); System.Windows.Forms.Application.DoEvents();
            using var image = new Bitmap(broken.Width, broken.Height);
            broken.DrawToBitmap(image, new Rectangle(Point.Empty, broken.Size));
            image.Save(Path.Combine(directory, "settings-invalid-smoke.png")); broken.Hide();
        }
        if (WindowEventListener.ForegroundWindow != foreground || File.Exists(journal)) throw new InvalidOperationException("Preference restart fixture touched foreground or recovery ownership.");
        Console.WriteLine("PASS Preference controls persist and reopen their real choices, stay paused, and preserve invalid files without desktop input or capture");
    }
}
