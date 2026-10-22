using BorderlessFlatWindow.Application.Preferences;

namespace BorderlessFlatWindow.App;

/// <summary>Transparent UI fixtures without an explicit settings path never read the user's choices.</summary>
internal sealed class EphemeralPreferencesStore : IPreferencesStore
{
    private UserPreferences? value;
    public bool CanWrite => true;
    public UserPreferences? Load() => value;
    public void Save(UserPreferences preferences) { preferences.Validate(); value = preferences; }
    public void Dispose() { }
}
