using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.App;

internal sealed class RecoveryDiagnosticsControl : UserControl
{
    private readonly string journalPath;
    private readonly Label summary = new() { Dock = DockStyle.Fill, Padding = new(14) };
    private readonly ChromeButton recover = new() { Text = "启动独立恢复程序", AutoSize = true, Enabled = false };
    private readonly ChromeButton refresh = new() { Text = "检查恢复日志", AutoSize = true };
    private bool running;
    internal bool RequiresAttention { get; private set; }

    internal RecoveryDiagnosticsControl(string? journalPath = null)
    {
        this.journalPath = Path.GetFullPath(journalPath ?? JsonRecoveryJournalStore.DefaultPath);
        var path = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = this.journalPath, Height = 32 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(6) };
        actions.Controls.AddRange([refresh, recover]);
        refresh.Click += (_, _) => RefreshJournal();
        recover.Click += async (_, _) => await RecoverAsync();
        var hint = new WrapLabel
        {
            Dock = DockStyle.Bottom,
            Padding = new(14),
            Text = "恢复程序会核验窗口身份和实例标记，再恢复原始位置与显示状态。\n正常退出和异常恢复共用日志；失败项目保留，关闭或身份不符的窗口会跳过。\n也可独立运行 BorderlessFlatWindow.Recovery.exe；加 --inspect 只检查日志。"
        };
        Controls.Add(summary); Controls.Add(actions); Controls.Add(path); Controls.Add(hint);
    }

    internal int RefreshJournal()
    {
        if (running) return 0;
        RequiresAttention = false;
        recover.Enabled = false;
        if (!File.Exists(journalPath))
        {
            summary.Text = "没有待恢复日志。\n\n开始画布管理时会先保存每扇窗口的恢复记录。";
            return 0;
        }
        try
        {
            using var store = new JsonRecoveryJournalStore(journalPath);
            var journal = store.Load()!;
            recover.Enabled = journal.Entries.Count > 0;
            RequiresAttention = recover.Enabled;
            summary.Text = $"待处理记录：{journal.Entries.Count}，其中已完成接管登记：{journal.Entries.Count(entry => entry.Armed)}。\n\n"
                + "点击恢复会启动独立程序，已验证的旧会话窗口将回到原始位置；未完成登记的记录只清理标记。";
            return journal.Entries.Count;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            RequiresAttention = true;
            summary.Text = $"无法读取或日志正被使用；文件已保留。\n\n{exception.Message}";
            return 0;
        }
    }

    internal void CheckCorruptJournalSmoke()
    {
        const string fixture = "{intentionally-invalid-gui-recovery-fixture";
        File.WriteAllText(journalPath, fixture);
        if (RefreshJournal() != 0 || !RequiresAttention || recover.Enabled || !summary.Text.Contains("文件已保留")
            || File.ReadAllText(journalPath) != fixture)
            throw new InvalidOperationException("Corrupt recovery data did not remain visible, disabled and preserved.");
    }

    private async Task RecoverAsync()
    {
        running = true; recover.Enabled = false; refresh.Enabled = false;
        summary.Text = "独立恢复程序正在处理；结果将在此显示。";
        try
        {
            var executable = FindRecoveryExecutable();
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--journal"); start.ArgumentList.Add(journalPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动恢复程序。");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var text = await output; var errors = await error;
            if (IsDisposed) return;
            running = false; RefreshJournal();
            if (process.ExitCode is 0 or 2)
            {
                using var report = JsonDocument.Parse(text);
                var outcomes = report.RootElement.GetProperty("Results").EnumerateArray().ToArray();
                var restored = outcomes.Count(item => item.GetProperty("Outcome").GetString() == "Restored");
                summary.Text = $"已恢复 {restored} 个窗口；剩余 {report.RootElement.GetProperty("Remaining").GetInt32()} 项。\n\n"
                    + string.Join("\n", outcomes.Select(item => item.GetProperty("Detail").GetString()));
            }
            else summary.Text = $"恢复未完成，日志已保留。\n\n{errors}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException or JsonException)
        { if (!IsDisposed) summary.Text = $"恢复未完成，日志已保留。\n\n{exception.Message}"; }
        finally { running = false; if (!IsDisposed) refresh.Enabled = true; }
    }

    private static string FindRecoveryExecutable()
    {
        var adjacent = Path.Combine(AppContext.BaseDirectory, "BorderlessFlatWindow.Recovery.exe");
        if (File.Exists(adjacent)) return adjacent;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var development = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/BorderlessFlatWindow.Recovery/bin",
            configuration, "net10.0-windows", "BorderlessFlatWindow.Recovery.exe"));
        return File.Exists(development) ? development : throw new FileNotFoundException("请先构建整个解决方案，或把独立恢复程序放在应用旁边。");
    }
}
