namespace BorderlessFlatWindow.Application.Navigation;

/// <summary>Generations prevent a delayed touch-entry callback from consuming a newer request.</summary>
public sealed class NavigationModeCoordinator
{
    private long generation;
    public bool TouchPending { get; private set; }
    public long RequestTouch() { TouchPending = true; return ++generation; }
    public void CancelTouch() { TouchPending = false; generation++; }
    public bool CompleteTouch(long request, bool canEnter)
    {
        if (!TouchPending || request != generation) return false;
        TouchPending = false;
        return canEnter;
    }
}
