using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.App;

internal sealed partial class ManagedCanvasControl
{
    private readonly PreferencesSession preferences;
    private ManualNavigationShortcut preferredShortcut;
    private bool applyingPreferences;
    private NavigationHotkey PreferredHotkey => preferredShortcut switch
    {
        ManualNavigationShortcut.ControlAltN => new(3, 0x4E, "Ctrl+Alt+N"),
        ManualNavigationShortcut.ControlAltM => new(3, 0x4D, "Ctrl+Alt+M"),
        _ => NavigationHotkey.Default
    };
    private static ManualNavigationShortcut ChoiceFor(NavigationHotkey hotkey) => hotkey.Key switch
    { 0x4E => ManualNavigationShortcut.ControlAltN, 0x4D => ManualNavigationShortcut.ControlAltM, _ => ManualNavigationShortcut.Default };
    private void InitializePreferences()
    {
        ApplyPreferences();
        automaticAdmission.CheckedChanged += (_, _) => SavePreferences();
        wallpaperColors.CheckedChanged += (_, _) => SavePreferences();
        overviewAnimation.CheckedChanged += (_, _) => SavePreferences();
        var menu = new ToolStripMenuItem("本地设置");
        menu.DropDownItems.Add("重新读取保存的设置", null, (_, _) =>
        {
            if (reading || session?.IsBusy == true || overview is not null || openingOverview || editingArrangement) return;
            EndNavigationForEditing();
            if (preferences.Reload()) ApplyPreferences();
            notice = preferences.Warning; RefreshState();
        });
        menu.DropDownItems.Add("保存当前设置", null, (_, _) =>
        { EndNavigationForEditing(); preferences.SaveCurrent(); notice = preferences.Warning; RefreshState(); });
        menu.DropDownItems.Add("存储状态…", null, (_, _) => MessageBox.Show(this, preferences.Message, "本地设置", MessageBoxButtons.OK, MessageBoxIcon.Information));
        ChromeTheme.StyleMenu(menu.DropDown); moreMenu.Items.Add(menu);
    }
    private void ApplyPreferences()
    {
        applyingPreferences = true;
        try
        {
            var value = preferences.Current;
            inputOptions = value.Navigation; preferredShortcut = value.Shortcut;
            automaticAdmission.Checked = value.AutomaticAdmission;
            wallpaperColors.Checked = value.FollowWallpaper; overviewAnimation.Checked = value.AnimateOverview;
            if (gestures is not null) gestures.Options = inputOptions;
            globalInput?.Configure(inputOptions);
            if (globalInput is not null && globalInput.State.Hotkey != PreferredHotkey) globalInput.ChangeHotkey(PreferredHotkey);
            InvalidateOverviewPreparation();
        }
        finally { applyingPreferences = false; }
    }
    private void SavePreferences()
    {
        if (applyingPreferences) return;
        preferences.Update(new(inputOptions, preferredShortcut, automaticAdmission.Checked, wallpaperColors.Checked, overviewAnimation.Checked));
        notice = preferences.Warning; InvalidateOverviewPreparation(); RefreshState();
    }
    private void ApplyNavigationSettings(NavigationInputOptions options, NavigationHotkey hotkey)
    {
        gestures?.End(DateTimeOffset.UtcNow); inputOptions = options; preferredShortcut = ChoiceFor(hotkey);
        if (gestures is not null) gestures.Options = inputOptions;
        globalInput?.Configure(inputOptions);
        if (globalInput is not null && hotkey != globalInput.State.Hotkey) globalInput.ChangeHotkey(hotkey);
        SavePreferences();
    }
    internal void CheckPreferenceBindings()
    {
        automaticAdmission.Checked = false;
        if (wallpaperColors.Checked) wallpaperColors.PerformClick();
        if (overviewAnimation.Checked) overviewAnimation.PerformClick();
        ApplyNavigationSettings(new(false, true, false, 330), new(3, 0x4E, "Ctrl+Alt+N"));
        if (preferences.Current != CurrentPreferencesForCheck) throw new InvalidOperationException("Preference controls did not publish their actual choices.");
    }
    internal UserPreferences CurrentPreferencesForCheck => new(inputOptions, preferredShortcut, automaticAdmission.Checked, wallpaperColors.Checked, overviewAnimation.Checked);
}
