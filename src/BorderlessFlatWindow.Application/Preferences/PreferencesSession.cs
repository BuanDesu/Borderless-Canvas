namespace BorderlessFlatWindow.Application.Preferences;

/// <summary>Runtime choices remain usable when persistence fails; invalid files are never replaced implicitly.</summary>
public sealed class PreferencesSession(Func<IPreferencesStore> createStore) : IDisposable
{
    private IPreferencesStore? store;
    private UserPreferences? saved;
    private bool disposed, initialized;
    public UserPreferences Current { get; private set; } = UserPreferences.Default;
    public bool CanSave { get; private set; }
    public bool HasUnsavedChanges => saved != Current;
    public string Message { get; private set; } = "尚未读取设置。";
    public string? Warning => CanSave && !HasUnsavedChanges ? null : Message;

    public bool Reload()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CanSave = false; store?.Dispose(); store = null;
        try
        {
            store = createStore(); var loaded = store.Load() ?? UserPreferences.Default; loaded.Validate();
            Current = saved = loaded; CanSave = store.CanWrite;
            initialized = true;
            Message = CanSave ? "已读取本地设置。" : "设置只读；更改仅本次会话生效，可在关闭其他实例后保存当前设置。";
            return true;
        }
        catch (Exception error) when (Expected(error))
        {
            if (!initialized) Current = UserPreferences.Default with { AutomaticAdmission = false };
            initialized = true;
            Message = $"未读取设置，原文件已保留；当前选择仅本次会话生效。{error.Message}"; return false;
        }
    }
    public bool Update(UserPreferences next)
    {
        ObjectDisposedException.ThrowIf(disposed, this); next.Validate();
        if (next == Current && !HasUnsavedChanges) return CanSave;
        Current = next; initialized = true; return Persist();
    }
    public bool SaveCurrent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var desired = Current;
        if (!Reload()) { Current = desired; return false; }
        Current = desired; return Persist();
    }
    private bool Persist()
    {
        if (!CanSave || store is null) return false;
        try { store.Save(Current); saved = Current; Message = "设置已保存到本机。"; return true; }
        catch (Exception error) when (Expected(error))
        { CanSave = false; Message = $"未保存设置，更改仅本次会话生效；原文件已保留。{error.Message}"; return false; }
    }
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException;
    public void Dispose() { if (disposed) return; disposed = true; CanSave = false; store?.Dispose(); store = null; }
}
