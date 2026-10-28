using System.Diagnostics;
using BorderlessFlatWindow.Core.Canvas;

namespace BorderlessFlatWindow.Windows;

public static class WorkspaceLauncher
{
    private static bool Broker(string executable) => new[] { "ApplicationFrameHost.exe", "rundll32.exe", "dllhost.exe", "svchost.exe", "explorer.exe", "conhost.exe", "OpenConsole.exe" }
        .Contains(Path.GetFileName(executable), StringComparer.OrdinalIgnoreCase);
    public static WorkspaceLaunch? Default(string? executable)
    {
        if (!WorkspaceLaunch.IsExecutablePath(executable)) return null;
        if (Broker(executable!)) return null;
        return new(executable!, Array.AsReadOnly(Array.Empty<string>()));
    }
    public static int Start(WorkspaceLaunch launch)
    {
        launch.Validate();
        if (Broker(launch.Executable)) throw new InvalidOperationException("系统代理没有可信应用启动上下文；请正常打开软件后明确选择窗口。");
        if (!File.Exists(launch.Executable)) throw new FileNotFoundException("软件已卸载或启动程序不存在。");
        var info = new ProcessStartInfo(launch.Executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launch.Executable)! };
        foreach (var argument in launch.Arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("软件未能启动。");
        return process.Id;
    }
}
