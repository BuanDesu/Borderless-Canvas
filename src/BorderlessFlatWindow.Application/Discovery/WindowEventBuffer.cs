using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.Application.Discovery;

public enum WindowChangeKind { Created, Destroyed, Shown, Hidden, Location, Name, State, Foreground, DesktopChanged }
public readonly record struct WindowChange(nint Handle, WindowChangeKind Kind);

/// <summary>UI-thread buffer. Notifications are hints: reread current state, never replay stale rectangles.</summary>
public sealed class WindowEventBuffer
{
    private Dictionary<nint, bool> pending = [];
    private bool reconcile;
    private bool identityGap;
    public int Count => pending.Count;
    public nint? ForegroundHandle { get; private set; }

    public void Add(WindowChange change)
    {
        if (change.Kind == WindowChangeKind.DesktopChanged) { reconcile = true; return; }
        if (change.Handle == 0) return;
        if (change.Kind == WindowChangeKind.Foreground) ForegroundHandle = change.Handle;
        if (pending.Count >= 2048 && !pending.ContainsKey(change.Handle))
        { reconcile = true; identityGap = true; return; }
        pending.TryGetValue(change.Handle, out var destroyed);
        pending[change.Handle] = destroyed || change.Kind == WindowChangeKind.Destroyed;
    }

    public WindowCatalogSnapshot Drain(ITrackedDesktopWindowCatalog catalog)
    {
        // COM reads may pump messages and reenter Add. New hints must belong to the next batch.
        var batch = pending; pending = [];
        var full = reconcile; reconcile = false;
        var gap = identityGap; identityGap = false;
        try
        {
            // Overflow may have dropped a destroy hint: retire all IDs rather than inherit a reused HWND.
            if (gap)
                foreach (var entry in catalog.Current.Entries) catalog.ForgetWindow(entry.Observation.Handle);
            foreach (var item in batch.Where(item => item.Value)) catalog.ForgetWindow(item.Key);
            if (full) catalog.Inspect();
            else foreach (var handle in batch.Keys) catalog.RefreshWindow(handle);
            return catalog.Current;
        }
        catch
        {
            foreach (var item in batch) Add(new(item.Key, item.Value ? WindowChangeKind.Destroyed : WindowChangeKind.State));
            reconcile |= full; identityGap |= gap;
            throw;
        }
    }

    public void Clear() { pending.Clear(); reconcile = false; identityGap = false; ForegroundHandle = null; }
}
