namespace BorderlessFlatWindow.Application.Startup;

/// <summary>Production commands are explicit; laboratory commands cannot accidentally start a canvas.</summary>
public sealed class LaunchArguments
{
    private static readonly HashSet<string> Flags = ["--native-overview", "--native-overview-solid", "--form-overview", "--direct-readback", "--review-panel", "--runtime-check"];
    private static readonly HashSet<string> Values = ["--journal", "--bookmarks", "--settings", "--motion-metrics", "--launch-fixed-shortcut"];
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    public bool Has(string name) => options.ContainsKey(name);
    public string? Value(string name) => options.GetValueOrDefault(name);
    public static LaunchArguments Parse(IReadOnlyList<string> arguments)
    {
        var result = new LaunchArguments();
        for (var i = 0; i < arguments.Count; i++)
        {
            var key = arguments[i];
            if (!Flags.Contains(key) && !Values.Contains(key)) throw new ArgumentException("未知启动参数；实验和检查请使用独立工具。");
            if (result.Has(key)) throw new ArgumentException("启动参数重复。");
            string? value = null;
            if (Values.Contains(key))
            {
                if (++i >= arguments.Count || string.IsNullOrWhiteSpace(arguments[i]) || arguments[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("启动参数缺少值。");
                value = arguments[i];
            }
            result.options.Add(key, value);
        }
        return result;
    }
}
