using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Application.Startup;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LaunchArguments options;
        try { options = LaunchArguments.Parse(args); }
        catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
        ApplicationConfiguration.Initialize();
        using var pixelFont = new PixelFont();
        if (options.Has("--runtime-check"))
        {
            BorderlessFlatWindow.Presentation.SpatialOverviewView.WarmUp();
            Thread.Sleep(1200); // Bounded observation window for the external package verifier; no canvas or hooks.
            Console.WriteLine("PASS Production runtime and presentation initialization");
            return 0;
        }
        var fixedShortcut = options.Value("--launch-fixed-shortcut");
        if (fixedShortcut is not null)
        {
            if (!Path.IsPathFullyQualified(fixedShortcut) || !string.Equals(Path.GetExtension(fixedShortcut), ".lnk", StringComparison.OrdinalIgnoreCase)) return 1;
            var sent = ShortcutCommandChannel.Send(fixedShortcut).GetAwaiter().GetResult();
            if (sent == ShortcutDelivery.Delivered) return 0;
            if (sent == ShortcutDelivery.Unavailable)
            { MessageBox.Show("画布实例暂时无法接收便签请求，未重复打开目标。请稍后重试。", "便签启动", MessageBoxButtons.OK, MessageBoxIcon.Information); return 1; }
        }
        var host = OverviewHostOptions.Resolve(args, Environment.OSVersion.Version);
        OverviewRuntimeOptions.UseNative = host.UseNative; OverviewRuntimeOptions.ForceSolid = host.ForceSolid;
        FrostedBackdrop.DirectReadback = options.Has("--direct-readback");
        if (options.Value("--motion-metrics") is { } metrics) SpatialOverviewForm.MotionMetricsPath = Path.GetFullPath(metrics);
        var journal = options.Value("--journal"); var bookmarks = options.Value("--bookmarks"); var settings = options.Value("--settings");
        try { BorderlessFlatWindow.Windows.Preferences.JsonPreferencesStore.EnsureSeparateDataFiles(
            settings ?? BorderlessFlatWindow.Windows.Preferences.JsonPreferencesStore.DefaultPath,
            journal ?? BorderlessFlatWindow.Windows.Recovery.JsonRecoveryJournalStore.DefaultPath,
            bookmarks ?? BorderlessFlatWindow.Windows.Bookmarks.JsonBookmarkStore.DefaultPath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        { Console.Error.WriteLine("本地数据路径无效或重复。"); return 1; }
        using var form = new MainForm(new DesktopWindowCatalog(), journalPath: journal, bookmarksPath: bookmarks,
            reviewPanel: options.Has("--review-panel"), settingsPath: settings, initialFixedShortcut: fixedShortcut);
        System.Windows.Forms.Application.Run(form);
        return 0;
    }
}
