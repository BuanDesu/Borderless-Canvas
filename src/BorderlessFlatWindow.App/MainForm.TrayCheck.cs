using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Windows.Recovery;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    private void CheckTrayCommands()
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Fixture entry only.");
        var presentation = lastTrayPresentation;
        for (var i = 0; i < 100; i++) UpdateCameraStatus();
        if (!ReferenceEquals(presentation, lastTrayPresentation)) throw new InvalidOperationException("Unchanged camera status rewrote tray presentation.");
        Hide();
        management.CheckPreparingOverviewTrayDeferral(() =>
        {
            trayOpen!.PerformClick(); trayRecovery!.PerformClick();
        }, () =>
        {
            if (Visible || pendingManagerTarget != ManagerTarget.Recovery) throw new InvalidOperationException("Tray opened before overview completion or lost the latest target.");
        }, () =>
        {
            if (!Visible || pendingManagerTarget is not null || !ReferenceEquals(tabs.SelectedTab, recovery.Parent)) throw new InvalidOperationException("Deferred tray action did not complete after overview cleanup.");
        });
        management.SetTransparentMenusForCheck();
        foreach (var appearance in new[] { true, false })
        {
            ((ToolStripMenuItem)traySettings!.DropDownItems[appearance ? 1 : 2]).PerformClick();
            System.Windows.Forms.Application.DoEvents(); management.CheckTraySettingsMenu(appearance);
        }
        var prior = management.CurrentPreferencesForCheck; var topmost = TopMost; TopMost = true;
        try
        {
            foreach (var confirm in new[] { false, true })
            {
                using var timer = new System.Windows.Forms.Timer { Interval = 30 };
                Exception? failure = null; var done = false; var ticks = 0;
                timer.Tick += (_, _) =>
                {
                    var dialog = OwnedForms.OfType<NavigationSettingsDialog>().SingleOrDefault(form => form.Visible);
                    try
                    {
                        if (++ticks > 100) throw new InvalidOperationException("Tray settings modal timed out.");
                        if (dialog is null) return;
                        if (TrayCheckGetWindow(dialog.Handle, 4) != Handle || TrayCheckIsEnabled(Handle) || !TrayCheckIsEnabled(dialog.Handle)
                            || !dialog.TopMost || TrayState.CanOpen || TrayState.CanPause || TrayState.CanConfigure || TrayState.CanExit)
                            throw new InvalidOperationException("Settings modal did not own and gate the root/tray.");
                        DescendantsForTray(dialog).OfType<NumericUpDown>().Single().Value = 450;
                        dialog.DialogResult = confirm ? DialogResult.OK : DialogResult.Cancel; dialog.Close(); done = true; timer.Stop();
                    }
                    catch (Exception error) { failure = error; timer.Stop(); if (dialog is not null) { dialog.DialogResult = DialogResult.Abort; dialog.Close(); } }
                };
                timer.Start();
                ((ToolStripMenuItem)traySettings!.DropDownItems[0]).PerformClick();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!done && failure is null && watch.ElapsedMilliseconds < 5000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                timer.Stop();
                if (failure is not null) throw failure;
                var current = management.CurrentPreferencesForCheck;
                if (!done || !TrayCheckIsEnabled(Handle) || !TrayState.CanConfigure || current.Navigation.WheelDistance != (confirm ? 450 : prior.Navigation.WheelDistance)
                    || current.Shortcut != prior.Shortcut || management.Diagnostics().Running)
                    throw new InvalidOperationException("Tray settings save/cancel lost choices or started management.");
            }
        }
        finally { TopMost = topmost; }
        Console.WriteLine("PASS Tray shared settings menus, deferred panel selection, stable shell presentation and real modal save/cancel ownership");
    }
    internal static void CheckTrayRecoveryProcess(string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Recovery fixture output must be a new or empty directory.");
        Directory.CreateDirectory(directory);
        var journalPath = Path.Combine(directory, "recovery.json");
        var journal = RecoveryJournal.Empty().WithEntries([new(Guid.NewGuid(), new(0x42424242, 424242, 123456789),
            new(new(0, 1, -1, -1, -1, -1, 100, 100, 500, 400), new(100, 100, 500, 400)), 123, false)]);
        using (var store = new JsonRecoveryJournalStore(journalPath)) store.Save(journal);
        var foreground = WindowEventListener.ForegroundWindow;
        using var form = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true, journalPath: journalPath,
            bookmarksPath: Path.Combine(directory, "bookmarks.json"), settingsPath: Path.Combine(directory, "settings.json")) { Opacity = 0, ShowInTaskbar = false };
        form.Show(); form.InitializeTray();
        if (!form.TrayState.Caption.Contains("待恢复")) throw new InvalidOperationException("Inherited recovery was hidden from tray.");
        var busyObserved = false; var completed = false;
        form.recovery.StateChanged += () =>
        {
            if (form.recovery.IsRunning)
            {
                busyObserved = true;
                if (form.TrayState.CanConfigure || form.TrayState.CanExit || form.management.Diagnostics().Running) throw new InvalidOperationException("Recovery did not block conflicting actions.");
                form.Close();
                if (form.IsDisposed) throw new InvalidOperationException("Recovery allowed closing its manager mid-process.");
            }
        };
        form.recovery.RecoveryCompleted += report => completed = report.IsComplete && report.Results.Single().Outcome == RecoveryOutcome.ReleasedUnarmed;
        var task = form.recovery.RunFixtureRecovery();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.ElapsedMilliseconds < 30000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new InvalidOperationException("Independent recovery fixture timed out.");
        task.GetAwaiter().GetResult();
        using (var store = new JsonRecoveryJournalStore(journalPath))
            if (store.Load()!.Entries.Count != 0) throw new InvalidOperationException("Independent recovery left fixture records behind.");
        if (!busyObserved || !completed || form.recovery.RequiresAttention || form.recovery.IsRunning
            || form.management.Diagnostics().Running || !form.TrayState.CanConfigure || !form.TrayState.CanExit
            || WindowEventListener.ForegroundWindow != foreground) throw new InvalidOperationException("Independent recovery/tray fixture failed or changed foreground.");
        foreach (var invalid in new[] { "{}", "null", "{\"Results\":[]}", "{\"Remaining\":0}", "{\"Results\":[],\"Remaining\":1}",
            "{\"Results\":[{\"EntryId\":\"11111111-1111-1111-1111-111111111111\",\"Outcome\":\"Failed\",\"Detail\":\"fixture\"}],\"Remaining\":0}" })
        {
            try { RecoveryDiagnosticsControl.ParseReport(invalid, 0); }
            catch (System.Text.Json.JsonException) { continue; }
            throw new InvalidOperationException("Malformed recovery report was accepted.");
        }
        try { RecoveryDiagnosticsControl.ParseReport("{\"Results\":[],\"Remaining\":0}", 0, journal.Entries.Select(entry => entry.Id).ToHashSet());
            throw new InvalidOperationException("Missing recovery batch was accepted as success."); }
        catch (System.Text.Json.JsonException) { }
        Console.WriteLine("PASS Actual independent recovery process updates tray busy/completed states, blocks close, validates report and releases a synthetic unarmed journal without desktop movement");
    }
    private static IEnumerable<Control> DescendantsForTray(Control owner) => owner.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(DescendantsForTray(c)));
    [DllImport("user32.dll", EntryPoint = "GetWindow")] private static extern nint TrayCheckGetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrayCheckIsEnabled(nint window);
}
