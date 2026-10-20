using System.Diagnostics;
using BorderlessFlatWindow.Application.Ports;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class FixtureReadiness
{
    internal static DesktopWindowInfo Wait(nint handle)
    {
        var catalog = new DesktopWindowCatalog(0); var watch = Stopwatch.StartNew(); var stable = 0;
        do
        {
            System.Windows.Forms.Application.DoEvents(); catalog.RefreshWindow(handle);
            var current = catalog.Current.AdmittedWindows.FirstOrDefault(window => window.Handle == handle);
            if (current is not null) { if (++stable >= 4) return current; } else stable = 0;
            Thread.Sleep(20);
        } while (watch.Elapsed < TimeSpan.FromSeconds(3));
        throw new InvalidOperationException("Fixture never reached stable admission: " + catalog.Current.Entries.FirstOrDefault(entry => entry.Observation.Handle == handle)?.Admission.Reason);
    }
}
