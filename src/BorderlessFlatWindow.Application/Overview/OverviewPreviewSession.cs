using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Overview;

public enum PreviewState { Preview, Disabled, Offscreen, Unmatched, TooSmall, Overlap, Capacity, Unavailable }
public sealed record PreviewCard(Guid Id, NativeWindowIdentity? Identity, PixelRect? ContentBounds, PreviewState? Fallback, RectD? SourceFraction = null);
public interface IOverviewPreview : IDisposable
{
    bool IsCurrent { get; }
    SizeD? SourceSize { get; }
    bool Update(PixelRect destination);
    bool Update(PixelRect destination, RectD? sourceFraction, byte opacity)
        => sourceFraction is null && opacity == 255 && Update(destination);
}
public interface IOverviewPreviewPort { IOverviewPreview? Create(NativeWindowIdentity identity); }

/// <summary>Resources follow visible cards, with no default window-count limit. Failed sources retry explicitly.</summary>
public sealed class OverviewPreviewSession(IOverviewPreviewPort port, int capacity = int.MaxValue) : IDisposable
{
    private sealed record Resource(NativeWindowIdentity Identity, IOverviewPreview Preview)
    {
        internal (PixelRect Bounds, RectD? Crop, byte Opacity)? Last;
        internal SizeD? Size;
    }
    private readonly Dictionary<Guid, Resource> resources = [];
    private readonly Dictionary<Guid, NativeWindowIdentity> failed = [];
    private readonly Dictionary<Guid, PreviewState> states = [];
    private readonly int capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private bool disposed, synchronizing;
    public IReadOnlyDictionary<Guid, PreviewState> States => states;
    public int ResourceCount => resources.Count;
    public SizeD? SourceSize(Guid id) => resources.GetValueOrDefault(id)?.Size;
    public void RetryFailed() => failed.Clear();

    public void Synchronize(IEnumerable<PreviewCard> source, Guid? selected, bool enabled, byte opacity = 255)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (synchronizing) return; // Native identity queries may dispatch a nested UI callback.
        synchronizing = true;
        try { SynchronizeCore(source, selected, enabled, opacity); }
        finally { synchronizing = false; }
    }
    private void SynchronizeCore(IEnumerable<PreviewCard> source, Guid? selected, bool enabled, byte opacity)
    {
        var cards = source.ToArray();
        if (cards.Any(card => card.Id == Guid.Empty) || cards.Select(card => card.Id).Distinct().Count() != cards.Length)
            throw new ArgumentException("Preview cards must have unique nonempty identities.", nameof(source));
        states.Clear();
        var eligible = cards.Where(card => enabled && card.Fallback is null && card.Identity is { IsValid: true }
            && card.ContentBounds is { IsValid: true }).OrderByDescending(card => card.Id == selected).Take(capacity).Select(card => card.Id).ToHashSet();
        var lookup = cards.ToDictionary(card => card.Id);
        foreach (var id in resources.Keys.ToArray())
            if (!eligible.Contains(id) || lookup[id].Identity != resources[id].Identity) Release(id);
        foreach (var id in failed.Keys.ToArray())
            if (!lookup.TryGetValue(id, out var card) || card.Identity != failed[id]) failed.Remove(id);

        foreach (var card in cards.OrderByDescending(card => card.Id == selected))
        {
            if (disposed) return;
            var state = !enabled ? PreviewState.Disabled : card.Fallback;
            if (state is null && (card.Identity is not { IsValid: true } || card.ContentBounds is not { IsValid: true })) state = PreviewState.Unavailable;
            if (state is null && !eligible.Contains(card.Id)) state = PreviewState.Capacity;
            if (state is not null) { states[card.Id] = state.Value; continue; }
            var identity = card.Identity!.Value;
            if (failed.ContainsKey(card.Id)) { states[card.Id] = PreviewState.Unavailable; continue; }
            if (!resources.TryGetValue(card.Id, out var resource))
            {
                var preview = port.Create(identity);
                if (disposed) { preview?.Dispose(); return; }
                if (preview is null) { Fail(card.Id, identity); continue; }
                resources[card.Id] = resource = new(identity, preview);
            }
            var current = resource.Preview.IsCurrent;
            if (disposed) return;
            if (!current) { Fail(card.Id, identity); continue; }
            var size = resource.Preview.SourceSize;
            if (disposed) return;
            if (size is not { IsPositive: true }) { Fail(card.Id, identity); continue; }
            resource.Size = size;
            var bounds = card.SourceFraction is null ? Fit(size.Value, card.ContentBounds!.Value) : card.ContentBounds!.Value;
            var command = (bounds, card.SourceFraction, opacity);
            var updated = resource.Last == command || resource.Preview.Update(bounds, card.SourceFraction, opacity);
            if (disposed) return;
            if (!updated) { Fail(card.Id, identity); continue; }
            resource.Last = command;
            states[card.Id] = PreviewState.Preview;
        }
    }
    /// <summary>Animate already registered previews. No enumeration, COM queries or registration in this path.</summary>
    public void Animate(IEnumerable<PreviewCard> cards, byte opacity = 255)
    {
        if (disposed || synchronizing) return;
        synchronizing = true;
        try
        {
            foreach (var card in cards)
            {
                if (disposed) return;
                if (resources.TryGetValue(card.Id, out var resource) && card.Identity == resource.Identity
                    && card.ContentBounds is { IsValid: true } bounds)
                {
                    var command = (bounds, card.SourceFraction, opacity);
                    if (resource.Last != command && resource.Preview.Update(bounds, card.SourceFraction, opacity)) resource.Last = command;
                }
            }
        }
        finally { synchronizing = false; }
    }
    public static PixelRect Fit(SizeD source, PixelRect bounds)
    {
        if (!source.IsPositive || !bounds.IsValid) throw new ArgumentException("Valid source and destination sizes required.");
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, bounds.Width);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, bounds.Height);
        var left = bounds.Left + (bounds.Width - width) / 2; var top = bounds.Top + (bounds.Height - height) / 2;
        return new(left, top, left + width, top + height);
    }
    private void Fail(Guid id, NativeWindowIdentity identity) { Release(id); failed[id] = identity; states[id] = PreviewState.Unavailable; }
    private void Release(Guid id) { if (resources.Remove(id, out var resource)) resource.Preview.Dispose(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var id in resources.Keys.ToArray()) Release(id);
        states.Clear(); failed.Clear();
    }
}
