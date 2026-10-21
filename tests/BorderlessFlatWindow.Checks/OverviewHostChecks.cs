using BorderlessFlatWindow.Application.Overview;

internal static class OverviewHostChecks
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Overview defaults to native only at the supported Windows build boundary", BuildBoundary);
        yield return ("Form overview recovery override wins all conflicting flags and argument orders", FormOverride);
        yield return ("Explicit native and solid launchers remain usable on earlier Windows", ExplicitNative);
        yield return ("Unrelated arguments and native flag prefixes cannot change the overview host", UnrelatedArguments);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void BuildBoundary()
    {
        foreach (var version in new[] { new Version(6, 3, 9600), new(10, 0, 19045), new(10, 0, 22000), new(10, 0, 22620), new(10, 0) })
            Require(OverviewHostOptions.Resolve([], version) == new OverviewHostOptions(false, false), "Earlier/unknown build changed host.");
        foreach (var version in new[] { new Version(10, 0, 22621), new(10, 0, 26100), new(11, 0, 0) })
            Require(OverviewHostOptions.Resolve([], version) == new OverviewHostOptions(true, false), "Supported build did not use native.");
    }
    private static void FormOverride()
    {
        foreach (var version in new[] { new Version(10, 0, 19045), new(10, 0, 26100) })
            foreach (var flags in new string[][] { ["--form-overview"], ["--native-overview", "--form-overview"],
                ["--form-overview", "--native-overview-solid"], ["--native-overview-solid", "--form-overview", "--native-overview"] })
                Require(OverviewHostOptions.Resolve(flags, version) == new OverviewHostOptions(false, false), "Recovery override was lost.");
    }
    private static void ExplicitNative()
    {
        var old = new Version(10, 0, 19045);
        Require(OverviewHostOptions.Resolve(["--native-overview"], old) == new OverviewHostOptions(true, false), "Explicit native ignored.");
        Require(OverviewHostOptions.Resolve(["--native-overview-solid"], old) == new OverviewHostOptions(true, true), "Solid did not select native.");
    }
    private static void UnrelatedArguments()
    {
        var flags = new[] { "--native-overview-check", "--Native-Overview", "--form-overview-extra", "--journal", "report.json" };
        Require(!OverviewHostOptions.Resolve(flags, new(10, 0, 19045)).UseNative, "Check/prefix changed old-build selection.");
        Require(OverviewHostOptions.Resolve(flags, new(10, 0, 26100)) == new OverviewHostOptions(true, false), "Unrelated arguments disabled native.");
    }
}
