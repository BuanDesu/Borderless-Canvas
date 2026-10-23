using System.Diagnostics;
using BorderlessFlatWindow.Application.Discovery;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class LifecycleChecks
{
    internal static void Run(Action<string, Action> check)
    {
        var changes = new List<WindowChange>();
        var buffer = new WindowEventBuffer();
        var catalog = new DesktopWindowCatalog(0);
        using var listener = new WindowEventListener((uint)Environment.ProcessId, includeOwnProcess: true);
        listener.EventReceived += change => { changes.Add(change); buffer.Add(change); };
        catalog.Inspect();
        using var fixture = new Fixture { Text = "BFW lifecycle fixture", Bounds = new(180, 180, 420, 320) };
        fixture.Show();
        var handle = fixture.Handle;
        FixtureDesktop.PlaceOwnWindow(handle);
        Guid? firstId = null;

        check("Native create/show events populate the catalog without full enumeration", () =>
        {
            PumpUntil(() => changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Shown));
            var entry = AwaitAdmission(buffer, catalog, handle);
            firstId = entry.Id;
            Require(listener.IsRunning && listener.LastCallbackError is null, "Hook callback failed.");
        });
        check("Native move/resize and name events refresh current bounds with a stable ID", () =>
        {
            changes.Clear(); fixture.Bounds = new(350, 230, 500, 350); fixture.Text = "BFW lifecycle renamed";
            PumpUntil(() => changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Location)
                && changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Name));
            var entry = AwaitAdmission(buffer, catalog, handle, window => window.Title == fixture.Text && window.Bounds.Position.X == fixture.Left && window.Bounds.Size.Width == fixture.Width);
            Require(entry.Id == firstId && entry.Title == fixture.Text && entry.Bounds.Position.X == fixture.Left
                && entry.Bounds.Size.Width == fixture.Width, "Event did not update physical bounds/title.");
        });
        check("Child HWND events never add controls to the top-level catalog", () =>
        {
            changes.Clear();
            using var button = new Button { Text = "BFW child fixture" };
            fixture.Controls.Add(button);
            var child = button.Handle;
            PumpUntil(() => changes.Any(change => change.Handle == child));
            buffer.Drain(catalog);
            Require(catalog.Current.Entries.All(entry => entry.Observation.Handle != child), "Child control entered desktop catalog.");
        });
        check("Native hide/show events remove and readmit the window without changing identity", () =>
        {
            changes.Clear(); fixture.Hide();
            PumpUntil(() => changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Hidden));
            buffer.Drain(catalog);
            Require(catalog.Current.AdmittedWindows.All(window => window.Handle != handle), "Hidden window stayed admitted.");
            changes.Clear(); fixture.Show(); FixtureDesktop.PlaceOwnWindow(handle);
            PumpUntil(() => changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Shown));
            Require(AwaitAdmission(buffer, catalog, handle).Id == firstId, "Hide/show replaced ID.");
        });
        check("Native destroy event retires a window even when later metadata is unavailable", () =>
        {
            changes.Clear(); fixture.Close();
            PumpUntil(() => changes.Any(change => change.Handle == handle && change.Kind == WindowChangeKind.Destroyed));
            buffer.Drain(catalog);
            Require(catalog.Current.Entries.All(entry => entry.Observation.Handle != handle), "Destroyed window was retained.");
        });
        check("Rapid native creation and destruction leaves no ghosts after event coalescing", () =>
        {
            var transientHandles = new HashSet<nint>();
            for (var i = 0; i < 12; i++)
            {
                using var transient = new Fixture { Text = "BFW transient fixture" };
                transient.Show(); transientHandles.Add(transient.Handle); transient.Close();
            }
            PumpUntil(() => transientHandles.All(item => changes.Any(change => change.Handle == item && change.Kind == WindowChangeKind.Destroyed)));
            buffer.Drain(catalog);
            Require(catalog.Current.Entries.All(entry => !transientHandles.Contains(entry.Observation.Handle)), "Rapid lifecycle left ghosts.");
        });
        check("Unhook stops delivery and callback failures remain inside the managed boundary", () =>
        {
            listener.EventReceived += _ => throw new InvalidOperationException("Controlled callback failure");
            using var transient = new Fixture { Text = "BFW callback fixture" };
            transient.Show();
            PumpUntil(() => listener.LastCallbackError is not null);
            listener.Dispose(); var count = changes.Count;
            transient.Text = "after unhook"; transient.Close();
            PumpFor(TimeSpan.FromMilliseconds(100));
            Require(!listener.IsRunning && changes.Count == count, "Events arrived after unhook.");
        });
    }

    private static BorderlessFlatWindow.Application.Ports.DesktopWindowInfo AwaitAdmission(WindowEventBuffer buffer, DesktopWindowCatalog catalog,
        nint handle, Func<BorderlessFlatWindow.Application.Ports.DesktopWindowInfo, bool>? matches = null)
    {
        // A show hint may precede the native state/metadata hints already queued behind it.
        // Exercise the production event buffer; do not populate the catalog by a separate refresh/enumeration.
        BorderlessFlatWindow.Application.Ports.DesktopWindowInfo? result = null;
        var timeout = Stopwatch.StartNew();
        do
        {
            System.Windows.Forms.Application.DoEvents(); buffer.Drain(catalog);
            result = catalog.Current.AdmittedWindows.FirstOrDefault(window => window.Handle == handle);
            if (result is not null && (matches is null || matches(result))) return result;
            Thread.Sleep(5);
        } while (timeout.Elapsed < TimeSpan.FromSeconds(3));
        var observation = catalog.Current.Entries.FirstOrDefault(entry => entry.Observation.Handle == handle);
        throw new InvalidOperationException($"Native event sequence did not reach the required state: reason={observation?.Admission.Reason}, pendingHints={buffer.Count}, admitted={result is not null}.");
    }
    private static void PumpUntil(Func<bool> completed)
    {
        var timeout = Stopwatch.StartNew();
        while (!completed() && timeout.Elapsed < TimeSpan.FromSeconds(3))
        { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Require(completed(), "Timed out waiting for a real WinEvent.");
    }
    private static void PumpFor(TimeSpan duration)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < duration) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture : Form
    {
        internal Fixture() => Opacity = 0;
        protected override bool ShowWithoutActivation => true;
    }
}
