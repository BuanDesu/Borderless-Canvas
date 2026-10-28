using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Core.Canvas;

/// <summary>Local user data. Runtime IDs are hints scoped to one manager instance, never native handles.</summary>
public sealed record WorkspaceSnapshot(Guid SourceSession, uint Dpi, IReadOnlyList<WorkspaceMember> Windows)
{
    public const int MaxWindows = 64;
    public void Validate()
    {
        if (SourceSession == Guid.Empty || Dpi is < 48 or > 768 || Windows is null || Windows.Count > MaxWindows)
            throw new ArgumentException("工作区布局无效或超过 64 个窗口。");
        foreach (var member in Windows) member.Validate();
        if (Windows.Select(w => w.Id).Distinct().Count() != Windows.Count || Windows.Select(w => w.SourceWindow).Distinct().Count() != Windows.Count)
            throw new ArgumentException("工作区成员重复。");
    }
}

public sealed record WorkspaceMember(Guid Id, Guid SourceWindow, string Label, string? Executable, string WindowClass,
    RectD Bounds, WorkspaceLaunch? Launch)
{
    public void Validate()
    {
        if (Id == Guid.Empty || SourceWindow == Guid.Empty || string.IsNullOrWhiteSpace(Label) || Label.Length > 512
            || WindowClass is null || WindowClass.Length > 512 || !Bounds.Size.IsPositive
            || Math.Abs(Bounds.Position.X) > 1000000 || Math.Abs(Bounds.Position.Y) > 1000000 || Bounds.Size.Width > 100000 || Bounds.Size.Height > 100000
            || Executable is not null && !WorkspaceLaunch.IsExecutablePath(Executable)) throw new ArgumentException("工作区窗口数据无效。");
        Launch?.Validate();
        if (Launch is not null && !string.Equals(Executable, Launch.Executable, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("启动程序必须与窗口的已验证程序相同。");
    }
}

public sealed record WorkspaceLaunch(string Executable, IReadOnlyList<string> Arguments)
{
    public static bool IsExecutablePath(string? path) => path is { Length: > 6 and < 32768 } && !path.Any(c => c is '\0' or '"' or '\r' or '\n')
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && (path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' || path.StartsWith("\\\\", StringComparison.Ordinal));
    public void Validate()
    {
        if (!IsExecutablePath(Executable) || Arguments is null || Arguments.Count > 64 || Arguments.Any(a => a is null || a.Length > 8192 || a.Contains('\0'))
            || Arguments.Sum(a => a.Length + 3) > 30000) throw new ArgumentException("启动项需要完整 EXE 路径和独立参数，不能使用 shell 命令。");
    }
}
