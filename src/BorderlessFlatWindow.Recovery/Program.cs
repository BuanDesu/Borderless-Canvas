using System.Text.Json;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.Recovery;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("BorderlessFlatWindow.Recovery [--inspect] [--journal <path>]\nWithout --inspect, restores only verified marked windows. Exit: 0 complete, 2 pending, 1 error.");
            return 0;
        }
        try
        {
            var path = JsonRecoveryJournalStore.DefaultPath;
            var inspect = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--inspect") inspect = true;
                else if (args[i] == "--journal" && i + 1 < args.Length) path = args[++i];
                else throw new ArgumentException("Unknown or incomplete argument. Use --help.");
            }
            using var store = new JsonRecoveryJournalStore(path);
            if (inspect)
            {
                var journal = store.Load();
                Console.WriteLine(JsonSerializer.Serialize(new { version = RecoveryJournal.CurrentVersion,
                    pending = journal?.Entries.Count ?? 0, armed = journal?.Entries.Count(entry => entry.Armed) ?? 0 }));
                return 0;
            }
            using var session = new RecoverySession(store, new WindowsRecoveryWindowPort());
            var report = session.RecoverAll();
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
            return report.IsComplete ? 0 : 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.ComponentModel.Win32Exception)
        { Console.Error.WriteLine($"Recovery stopped: {exception.Message}"); return 1; }
    }
}
