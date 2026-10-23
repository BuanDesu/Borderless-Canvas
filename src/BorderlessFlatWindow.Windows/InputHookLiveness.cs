namespace BorderlessFlatWindow.Windows;

internal sealed class InputHookLiveness
{
    private long lastCallback;
    private long? missingSince;
    internal void Callback(long now) { lastCallback = now; missingSince = null; }
    internal bool ObserveRawMovement(long now, bool eligible)
    {
        if (!eligible || now - lastCallback < 600) { missingSince = null; return false; }
        missingSince ??= now;
        return now - missingSince >= 250;
    }
}
