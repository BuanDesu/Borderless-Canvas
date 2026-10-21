namespace BorderlessFlatWindow.Application.Overview;

/// <summary>Startup selection shared by view warmup and presenter creation.</summary>
public sealed record OverviewHostOptions(bool UseNative, bool ForceSolid)
{
    public static OverviewHostOptions Resolve(IEnumerable<string> arguments, Version windowsVersion)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(windowsVersion);
        var flags = arguments.ToHashSet(StringComparer.Ordinal);
        // The compatibility launcher must win even when a shortcut also carries native flags.
        if (flags.Contains("--form-overview")) return new(false, false);
        var solid = flags.Contains("--native-overview-solid");
        var native = flags.Contains("--native-overview") || solid || windowsVersion >= new Version(10, 0, 22621);
        return new(native, solid);
    }
}
