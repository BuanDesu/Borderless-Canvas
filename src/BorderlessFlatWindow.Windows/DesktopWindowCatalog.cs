using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.Windows;

/// <summary>Read-only, fail-closed discovery. Use on an STA thread.</summary>
public sealed class DesktopWindowCatalog : IDesktopWindowCatalog
{
    private readonly WindowInspectionService inspection;

    public DesktopWindowCatalog() : this((uint)Environment.ProcessId) { }

    /// <summary>An explicit self-process override is useful for controlled fixture checks.</summary>
    public DesktopWindowCatalog(uint ownProcessId) => inspection = new(
        new WindowsWindowObservationSource(), new WindowAdmissionPolicy(ownProcessId));

    public IReadOnlyList<DesktopWindowInfo> GetWindows() => inspection.GetWindows();
    public WindowCatalogSnapshot Inspect() => inspection.Inspect();
}
