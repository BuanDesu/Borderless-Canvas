using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.App;

/// <summary>Synthetic UI smoke data; screenshots never need titles from the user's desktop.</summary>
internal sealed class PreviewWindowCatalog : ITrackedDesktopWindowCatalog
{
    private readonly WindowCatalogSnapshot snapshot;
    internal PreviewWindowCatalog(bool empty = false)
    {
        var titles = empty ? [] : new[] { "Studio / 编辑器", "Research / 浏览器", "Notes / 资料", "Terminal / 终端" };
        snapshot = new(DateTimeOffset.UtcNow, titles.Select((title, index) => new InspectedWindow(Guid.NewGuid(), new WindowObservation
        {
            Handle = 100 + index, ProcessId = 1000u + (uint)index, ProcessCreationFileTime = 1,
            Title = title, ClassName = "BFW smoke fixture", IsAlive = true, IsVisible = true, MetadataAvailable = true,
            IsCloaked = false, IsOnCurrentDesktop = true,
            Bounds = new(new(index * 740 - 740, index % 2 * 560 + 80), new(680, 480))
        }, new(WindowExclusionReason.None))).ToArray());
    }
    public WindowCatalogSnapshot Current => snapshot;
    public WindowCatalogSnapshot Inspect() => snapshot;
    public IReadOnlyList<DesktopWindowInfo> GetWindows() => snapshot.AdmittedWindows;
    public void RefreshWindow(nint handle) { }
    public void ForgetWindow(nint handle) { }
}
