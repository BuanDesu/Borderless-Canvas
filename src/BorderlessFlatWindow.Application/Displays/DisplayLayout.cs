using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Displays;

public sealed record DisplayMonitor
{
    public DisplayMonitor(string deviceName, bool isPrimary, PixelRect bounds, PixelRect workArea, uint? effectiveDpi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (!bounds.Contains(workArea)) throw new ArgumentException("Work area must be inside monitor bounds.");
        if (effectiveDpi == 0) throw new ArgumentOutOfRangeException(nameof(effectiveDpi));
        DeviceName = deviceName; IsPrimary = isPrimary; Bounds = bounds; WorkArea = workArea; EffectiveDpi = effectiveDpi;
    }

    public string DeviceName { get; }
    public bool IsPrimary { get; }
    public PixelRect Bounds { get; }
    public PixelRect WorkArea { get; }
    public uint? EffectiveDpi { get; }
}

/// <summary>A read-only sample, not an atomic OS topology transaction or a movement permission.</summary>
public sealed class DisplayLayout
{
    public DisplayLayout(IEnumerable<DisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var copy = monitors.ToArray();
        if (copy.Length == 0 || copy.Any(monitor => monitor is null)
            || copy.Count(monitor => monitor.IsPrimary) != 1
            || copy.Select(monitor => monitor.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException("Need unique monitors and exactly one primary.", nameof(monitors));
        Monitors = Array.AsReadOnly(copy);
        Primary = copy.Single(monitor => monitor.IsPrimary);
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<DisplayMonitor> Monitors { get; }
    public DisplayMonitor Primary { get; }
    public DateTimeOffset CapturedAt { get; }
    public bool IsSingleScreen100Percent => Monitors.Count == 1 && Primary.EffectiveDpi == 96;
    public string BaselineDescription => IsSingleScreen100Percent
        ? "符合单屏 100% DPI 基线（仅只读诊断）"
        : "超出单屏 100% DPI 基线；多屏 / DPI 兼容待验收";
}
