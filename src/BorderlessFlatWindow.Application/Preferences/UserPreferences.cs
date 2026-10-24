using BorderlessFlatWindow.Application.Navigation;

namespace BorderlessFlatWindow.Application.Preferences;

// Legacy storage compatibility only. Manual navigation and its registrations have been removed.
public enum ManualNavigationShortcut { Default, ControlAltN, ControlAltM }

public sealed record UserPreferences(NavigationInputOptions Navigation, ManualNavigationShortcut Shortcut = ManualNavigationShortcut.Default,
    bool AutomaticAdmission = true, bool FollowWallpaper = true, bool AnimateOverview = true, bool ShortcutMenu = true)
{
    public NavigationKeyBindings Bindings { get; init; } = NavigationKeyBindings.Default;
    public static UserPreferences Default { get; } = new(new NavigationInputOptions());
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Navigation);
        if (!Enum.IsDefined(Shortcut)) throw new ArgumentException("不支持的导航组合。");
        ArgumentNullException.ThrowIfNull(Bindings); Bindings.Validate();
    }
}

public interface IPreferencesStore : IDisposable
{
    bool CanWrite { get; }
    UserPreferences? Load();
    void Save(UserPreferences preferences);
}
