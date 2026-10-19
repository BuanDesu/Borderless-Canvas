using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.Windows;

/// <summary>Read-only, fail-closed discovery. Use on an STA thread.</summary>
public sealed class DesktopWindowCatalog : ITrackedDesktopWindowCatalog
{
    private readonly WindowInspectionService inspection;
    private readonly WindowsWindowObservationSource source = new();

    public DesktopWindowCatalog() : this((uint)Environment.ProcessId) { }

    /// <summary>An explicit self-process override is useful for controlled fixture checks.</summary>
    public DesktopWindowCatalog(uint ownProcessId) => inspection = new(
        source, new WindowAdmissionPolicy(ownProcessId));

    public IReadOnlyList<DesktopWindowInfo> GetWindows() => inspection.GetWindows();
    public WindowCatalogSnapshot Inspect() => inspection.Inspect();
    public WindowCatalogSnapshot Current => inspection.Current;
    public void RefreshWindow(nint handle) => inspection.ApplyObservation(source.ReadWindow(handle));
    public void ForgetWindow(nint handle) => inspection.Forget(handle);
}
