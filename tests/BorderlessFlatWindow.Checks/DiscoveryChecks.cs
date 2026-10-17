using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Core.Geometry;

internal static class DiscoveryChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        var baseline = Normal();
        yield return ("Admission accepts a verified ordinary window", () =>
            Require(new WindowAdmissionPolicy(999).Evaluate(baseline).IsAdmitted, "Ordinary window excluded."));
        var exclusions = new (WindowObservation Window, WindowExclusionReason Reason)[]
        {
            (baseline with { IsAlive = false }, WindowExclusionReason.WindowDisappeared),
            (baseline with { Handle = 0 }, WindowExclusionReason.WindowDisappeared),
            (baseline with { ProcessId = 999 }, WindowExclusionReason.OwnProcess),
            (baseline with { ClassName = "Shell_TrayWnd" }, WindowExclusionReason.DesktopShell),
            (baseline with { ClassName = "workerw" }, WindowExclusionReason.DesktopShell),
            (baseline with { IsVisible = false }, WindowExclusionReason.NotVisible),
            (baseline with { IsChildWindow = true }, WindowExclusionReason.ChildWindow),
            (baseline with { IsToolWindow = true }, WindowExclusionReason.ToolWindow),
            (baseline with { OwnerHandle = 88 }, WindowExclusionReason.OwnedWindow),
            (baseline with { Title = " " }, WindowExclusionReason.Untitled),
            (baseline with { MetadataAvailable = false }, WindowExclusionReason.MetadataUnavailable),
            (baseline with { ProcessCreationFileTime = null }, WindowExclusionReason.IdentityUnavailable),
            (baseline with { ProcessCreationFileTime = 0 }, WindowExclusionReason.IdentityUnavailable),
            (baseline with { ProcessId = 0 }, WindowExclusionReason.IdentityUnavailable),
            (baseline with { IsCloaked = null }, WindowExclusionReason.CloakingUnknown),
            (baseline with { IsCloaked = true }, WindowExclusionReason.Cloaked),
            (baseline with { IsOnCurrentDesktop = null }, WindowExclusionReason.VirtualDesktopUnknown),
            (baseline with { IsOnCurrentDesktop = false }, WindowExclusionReason.OtherVirtualDesktop),
            (baseline with { IsMinimized = true }, WindowExclusionReason.Minimized),
            (baseline with { Bounds = null }, WindowExclusionReason.InvalidBounds),
            (baseline with { Bounds = default(RectD) }, WindowExclusionReason.InvalidBounds)
        };
        foreach (var (window, reason) in exclusions)
            yield return ($"Admission excludes {reason}", () =>
            {
                var result = new WindowAdmissionPolicy(999).Evaluate(window);
                Require(!result.IsAdmitted && result.Reason == reason && !string.IsNullOrWhiteSpace(result.Description),
                    $"Expected {reason}, got {result.Reason}.");
            });

        yield return ("Identity stays stable across title, position and size updates", StableIdentity);
        yield return ("Reused HWND or PID cannot inherit another process identity", ReusedIdentity);
        yield return ("Closed or unverified identities retire; explicit destroy supports same-process reuse", RetiredIdentity);
        yield return ("Excluded owned windows retain owner metadata without independent admission", OwnedWindows);
        yield return ("Minimize and restore do not replace a verified model identity", MinimizeRestore);
        yield return ("Failed and duplicate enumerations preserve the previous identity table", EnumerationFailure);
        yield return ("Observed close during enumeration cannot enter the admitted model", CloseDuringEnumeration);
    }

    private static WindowObservation Normal() => new()
    {
        Handle = 42, ProcessId = 100, ProcessCreationFileTime = 123456789,
        Title = "Fixture", ClassName = "FixtureClass", Bounds = new(new PointD(-100, 40), new SizeD(640, 480)),
        IsAlive = true, MetadataAvailable = true, IsVisible = true, IsCloaked = false, IsOnCurrentDesktop = true
    };

    private static (FakeSource Source, WindowInspectionService Service) Setup()
    {
        var source = new FakeSource { Items = [Normal()] };
        return (source, new(source, new(999)));
    }

    private static void StableIdentity()
    {
        var (source, service) = Setup();
        var first = service.Inspect().AdmittedWindows.Single();
        source.Items = [Normal() with { Title = "Renamed", Bounds = new(new PointD(3000, -1000), new SizeD(900, 700)) }];
        var second = service.Inspect().AdmittedWindows.Single();
        Require(first.Id == second.Id && second.Title == "Renamed" && second.Bounds.Position.X == 3000, "Identity or metadata update failed.");
    }

    private static void ReusedIdentity()
    {
        var (source, service) = Setup();
        var id = service.Inspect().AdmittedWindows.Single().Id;
        source.Items = [Normal() with { ProcessId = 101 }];
        var changed = service.Inspect().AdmittedWindows.Single().Id;
        Require(changed != id, "Reused handle inherited an ID.");
        source.Items = [source.Items[0] with { ProcessCreationFileTime = 987654321 }];
        Require(service.Inspect().AdmittedWindows.Single().Id != changed, "Reused PID inherited an ID.");
    }

    private static void RetiredIdentity()
    {
        var (source, service) = Setup();
        var id = service.Inspect().AdmittedWindows.Single().Id;
        source.Items = []; service.Inspect(); source.Items = [Normal()];
        var next = service.Inspect().AdmittedWindows.Single().Id;
        Require(next != id, "Absent identity was not retired.");
        service.Forget(42);
        var afterDestroy = service.Inspect().AdmittedWindows.Single().Id;
        Require(afterDestroy != next, "Explicit destroy did not invalidate identity.");
        source.Items = [Normal() with { ProcessCreationFileTime = null }]; service.Inspect();
        source.Items = [Normal()];
        Require(service.Inspect().AdmittedWindows.Single().Id != afterDestroy, "Unverified identity remained cached.");
    }

    private static void OwnedWindows()
    {
        var (source, service) = Setup();
        source.Items = [Normal(), Normal() with { Handle = 43, OwnerHandle = 42, Title = "Modal" }];
        var snapshot = service.Inspect();
        Require(snapshot.AdmittedWindows.Count == 1 && snapshot.Entries[1].Observation.OwnerHandle == 42
            && snapshot.Entries[1].Admission.Reason == WindowExclusionReason.OwnedWindow, "Modal ownership lost.");
    }

    private static void MinimizeRestore()
    {
        var (source, service) = Setup();
        var id = service.Inspect().AdmittedWindows.Single().Id;
        source.Items = [Normal() with { IsMinimized = true }];
        var excluded = service.Inspect();
        Require(excluded.AdmittedWindows.Count == 0 && excluded.Entries.Single().Id == id, "Minimization lost identity.");
        source.Items = [Normal()];
        Require(service.Inspect().AdmittedWindows.Single().Id == id, "Restore replaced identity.");
    }

    private static void EnumerationFailure()
    {
        var (source, service) = Setup();
        var id = service.Inspect().AdmittedWindows.Single().Id;
        source.Fail = true;
        ExpectFailure(service); source.Fail = false;
        source.Items = [Normal(), Normal()]; ExpectFailure(service);
        source.Items = [Normal()];
        Require(service.Inspect().AdmittedWindows.Single().Id == id, "Failed enumeration destroyed the previous IDs.");
    }

    private static void CloseDuringEnumeration()
    {
        var (source, service) = Setup();
        service.Inspect();
        source.Items = [Normal() with { IsAlive = false }];
        var snapshot = service.Inspect();
        Require(snapshot.AdmittedWindows.Count == 0 && snapshot.Entries.Single().Id is null, "Closed window admitted.");
    }

    private static void ExpectFailure(WindowInspectionService service)
    {
        try { service.Inspect(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected enumeration failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeSource : IWindowObservationSource
    {
        internal IReadOnlyList<WindowObservation> Items { get; set; } = [];
        internal bool Fail { get; set; }
        public IReadOnlyList<WindowObservation> ReadWindows() => Fail
            ? throw new InvalidOperationException("Injected enumeration failure.") : Items;
    }
}
