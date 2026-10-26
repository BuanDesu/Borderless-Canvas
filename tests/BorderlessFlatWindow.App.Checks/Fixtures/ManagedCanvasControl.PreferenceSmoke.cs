using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    internal void CheckTraySettingsMenu(bool appearance)
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true }) throw new InvalidOperationException("Fixture entry only.");
        var item = appearance ? appearanceMenu : localSettingsMenu;
        if (!moreMenu.Visible || !item.DropDown.Visible) throw new InvalidOperationException("Tray settings did not open the shared menu.");
        item.HideDropDown(); moreMenu.Close();
    }
    internal void SetTransparentMenusForCheck()
    {
        moreMenu.Opacity = appearanceMenu.DropDown.Opacity = localSettingsMenu.DropDown.Opacity = 0;
    }
    internal void CheckPreparingOverviewTrayDeferral(Action request, Action beforeCompletion, Action afterCompletion)
    {
        if (FindForm() is not MainForm { IsTransparentFixture: true } || HasOverviewWork) throw new InvalidOperationException("Fixture entry only.");
        openingOverview = true; overviewRequested = true;
        try
        {
            request(); System.Windows.Forms.Application.DoEvents(); beforeCompletion();
            if (overviewRequested || !openingCloseRequested) throw new InvalidOperationException("Panel request did not cancel overview preparation.");
            openingOverview = false; openingCloseRequested = false; StatusChanged?.Invoke();
            // The status callback must queue rather than activate the panel within the completion stack.
            beforeCompletion(); System.Windows.Forms.Application.DoEvents(); afterCompletion();
        }
        finally { overviewRequested = openingOverview = openingCloseRequested = false; }
    }
    internal void CheckPreferenceBindings()
    {
        automaticAdmission.Checked = false;
        if (wallpaperColors.Checked) wallpaperColors.PerformClick();
        if (overviewAnimation.Checked) overviewAnimation.PerformClick();
        ApplyNavigationSettings(new(false, true, false, 330), NavigationKeyBindings.Default with { Overview = new(3, 0x4E) });
        if (preferences.Current != CurrentPreferencesForCheck) throw new InvalidOperationException("Preference controls did not publish their actual choices.");
    }
    internal UserPreferences CurrentPreferencesForCheck => new(inputOptions, preferredShortcut, automaticAdmission.Checked, wallpaperColors.Checked, overviewAnimation.Checked, shortcutContext.Checked) { Bindings = preferredBindings };}
