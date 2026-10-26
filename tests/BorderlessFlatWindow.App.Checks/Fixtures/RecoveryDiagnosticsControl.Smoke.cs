using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using BorderlessFlatWindow.Windows.Recovery;
using BorderlessFlatWindow.Application.Recovery;

namespace BorderlessFlatWindow.App;

internal sealed partial class RecoveryDiagnosticsControl
{
    internal void CheckCorruptJournalSmoke()
    {
        const string fixture = "{intentionally-invalid-gui-recovery-fixture";
        File.WriteAllText(journalPath, fixture);
        if (RefreshJournal() != 0 || !RequiresAttention || recover.Enabled || !summary.Text.Contains("文件已保留")
            || File.ReadAllText(journalPath) != fixture)
            throw new InvalidOperationException("Corrupt recovery data did not remain visible, disabled and preserved.");
    }

    internal Task RunFixtureRecovery() => RecoverAsync();

}
