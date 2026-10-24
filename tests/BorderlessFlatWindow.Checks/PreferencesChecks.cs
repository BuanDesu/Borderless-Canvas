using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Preferences;

internal static class PreferencesChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Preferences load defaults or saved choices without implicitly saving", Load);
        yield return ("Failed preference writes retain runtime choices and the previously saved document", WriteFailure);
        yield return ("Invalid preference reload preserves current choices and makes persistence explicit", InvalidReload);
        yield return ("Read-only preference choices remain usable and can be explicitly saved after reopening", ReadOnlyRetry);
        yield return ("Explicit preference reload replaces unsaved choices with saved values", ExplicitReload);
        yield return ("Invalid preference updates do not change runtime choices or call storage", Invalid);
        yield return ("First corrupt preference load disables automatic admission without losing later runtime choices", InitialFailure);
        yield return ("Navigation bindings accept custom commands and preserve context-only Home and Backspace", CustomBindings);
        yield return ("Navigation bindings reject shell, editing, accessibility and reserved key combinations", ReservedBindings);
        yield return ("Navigation bindings reject duplicates, missing commands and unsupported modifiers", DuplicateBindings);
    }
    private static void CustomBindings()
    {
        NavigationKeyBindings.Default.Validate();
        var value = NavigationKeyBindings.Default with { Touch = new(7, 0x58), Home = new(3, 0x48), Back = new(3, 0x42) };
        value.Validate(); Require(value.Home.Matches(0x48, 3) && !value.Home.Matches(0x48, 0), "Context modifier match failed.");
    }
    private static void ReservedBindings()
    {
        foreach (var chord in new[] { new KeyChord(8, 9), new(1, 9), new(2, 0x43), new(3, 0x2E), new(3, 0x1B), new(3, 9), new(3, 0x73), new(3, 0x7B), new(0, 0x56), new(3, 0x24) })
        { var rejected = false; try { (NavigationKeyBindings.Default with { Touch = chord }).Validate(); } catch (ArgumentException) { rejected = true; } Require(rejected, "Reserved chord accepted."); }
    }
    private static void DuplicateBindings()
    {
        foreach (var value in new[] { NavigationKeyBindings.Default with { Touch = NavigationKeyBindings.Default.Crosshair }, NavigationKeyBindings.Default with { Pin = null! }, NavigationKeyBindings.Default with { Home = new(15, 0x48) } })
        { var rejected = false; try { value.Validate(); } catch (ArgumentException) { rejected = true; } Require(rejected, "Invalid binding set accepted."); }
    }
    private static readonly UserPreferences Custom = new(new(false, true, false, 360), ManualNavigationShortcut.ControlAltM, false, false, false);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class State { internal UserPreferences? Value; internal bool Writable = true, ReadFailure, WriteFailure; internal int Writes, Closed; }
    private sealed class Store(State state) : IPreferencesStore
    {
        private bool closed;
        public bool CanWrite => state.Writable;
        public UserPreferences? Load() => state.ReadFailure ? throw new InvalidDataException("Controlled invalid settings.") : state.Value;
        public void Save(UserPreferences value) { state.Writes++; if (state.WriteFailure) throw new IOException("Controlled write failure."); state.Value = value; }
        public void Dispose() { if (closed) return; closed = true; state.Closed++; }
    }
    private static void Load()
    {
        var state = new State(); using var session = new PreferencesSession(() => new Store(state));
        Require(session.Reload() && session.Current == UserPreferences.Default && state.Writes == 0 && session.Warning is null, "Missing configuration caused a write or lost defaults.");
        state.Value = Custom;
        Require(session.Reload() && session.Current == Custom && !session.HasUnsavedChanges && state.Writes == 0, "Saved choices were not loaded.");
    }
    private static void WriteFailure()
    {
        var state = new State { Value = UserPreferences.Default }; using var session = new PreferencesSession(() => new Store(state)); session.Reload();
        state.WriteFailure = true;
        Require(!session.Update(Custom) && session.Current == Custom && state.Value == UserPreferences.Default && session.HasUnsavedChanges
            && !session.CanSave && session.Warning is not null, "Write failure discarded runtime intent or overwrote persisted choices.");
        state.WriteFailure = false;
        Require(session.SaveCurrent() && session.Current == Custom && state.Value == Custom && !session.HasUnsavedChanges && session.Warning is null, "Explicit retry lost desired choices.");
    }
    private static void InvalidReload()
    {
        var state = new State { Value = Custom }; using var session = new PreferencesSession(() => new Store(state)); session.Reload();
        state.ReadFailure = true;
        Require(!session.Reload() && session.Current == Custom && !session.CanSave && session.Warning is not null && state.Writes == 0, "Invalid reload replaced runtime choices.");
        Require(!session.SaveCurrent() && session.Current == Custom && state.Writes == 0, "Invalid file was overwritten by save-current.");
    }
    private static void ReadOnlyRetry()
    {
        var state = new State { Writable = false, Value = Custom }; using var session = new PreferencesSession(() => new Store(state)); session.Reload();
        var changed = Custom with { AnimateOverview = true };
        Require(!session.Update(changed) && session.Current == changed && state.Value == Custom && state.Writes == 0, "Read-only mode discarded choices or wrote storage.");
        state.Writable = true;
        Require(session.SaveCurrent() && state.Value == changed && state.Closed == 1, "Reopening persistence failed to retain unsaved changes.");
    }
    private static void ExplicitReload()
    {
        var state = new State { Value = Custom, Writable = false }; using var session = new PreferencesSession(() => new Store(state)); session.Reload();
        session.Update(UserPreferences.Default); state.Value = Custom with { FollowWallpaper = true };
        Require(session.Reload() && session.Current == state.Value && !session.HasUnsavedChanges && state.Writes == 0, "Explicit reload retained unsaved data or wrote a file.");
    }
    private static void Invalid()
    {
        var state = new State(); using var session = new PreferencesSession(() => new Store(state)); session.Reload();
        foreach (var value in new[] { Custom with { Navigation = null! }, Custom with { Shortcut = (ManualNavigationShortcut)800 } })
        {
            var rejected = false; try { session.Update(value); } catch (ArgumentException) { rejected = true; }
            Require(rejected && session.Current == UserPreferences.Default && state.Writes == 0, "Invalid settings were published.");
        }
    }
    private static void InitialFailure()
    {
        var state = new State { ReadFailure = true }; using var session = new PreferencesSession(() => new Store(state));
        Require(!session.Reload() && !session.Current.AutomaticAdmission && state.Writes == 0, "Corrupt initial settings enabled automatic admission.");
        session.Update(UserPreferences.Default);
        Require(!session.SaveCurrent() && session.Current.AutomaticAdmission && state.Writes == 0, "Failed explicit save discarded a later runtime choice.");
    }
}
