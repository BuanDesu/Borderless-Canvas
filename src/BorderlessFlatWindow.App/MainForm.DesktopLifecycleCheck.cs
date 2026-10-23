using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal void CheckInputAvailabilitySmoke() => management.CheckInputAvailabilityBoundaries();
    internal void CheckOwnerInputWakeSmoke() => management.CheckOwnerInputWake();
    internal static void CheckDesktopLifecycleDispose(string directory)
    {
        Directory.CreateDirectory(directory);
        var journal = Path.Combine(directory, "recovery.json");
        using var form = new MainForm(new PreviewWindowCatalog(), showWithoutActivation: true, journalPath: journal,
            bookmarksPath: Path.Combine(directory, "bookmarks.json"), settingsPath: Path.Combine(directory, "settings.json")) { Opacity = 0, ShowInTaskbar = false };
        form.Show(); LifecycleSend(form.Handle, 0x0218, 4, 0);
        var applied = form.appliedDesktopBatches; form.Dispose(); System.Windows.Forms.Application.DoEvents();
        if (!form.IsDisposed || !form.desktopLifecycleStopped || form.appliedDesktopBatches != applied || File.Exists(journal))
            throw new InvalidOperationException("Disposed manager dispatched stale system work or created recovery ownership.");
        Console.WriteLine("PASS Disposed manager invalidates queued lifecycle work before releasing its controls and event listener");
    }
    internal void CheckDesktopLifecycleSmoke()
    {
        if (!showWithoutActivation || !IsHandleCreated) throw new InvalidOperationException("Fixture entry only.");
        var foreground = WindowEventListener.ForegroundWindow;
        var before = appliedDesktopBatches;
        RequestManager(ManagerTarget.Appearance);
        LifecycleSend(Handle, 0x0218, 4, 0); // Own HWND receives a message; this does not suspend Windows.
        if (!desktopChanges.HasWork || !management.DesktopTransitionPending || management.CanUsePanelCommands)
            throw new InvalidOperationException("System message did not synchronously gate input before deferred work.");
        System.Windows.Forms.Application.DoEvents();
        if (desktopChanges.HasWork || appliedDesktopBatches <= before || management.DesktopTransitionPending || management.Diagnostics().Running || pendingManagerTarget is not null)
            throw new InvalidOperationException("Deferred suspend processing did not remain stopped.");
        before = appliedDesktopBatches;
        LifecycleSend(Handle, 0x000A, 0, 0); LifecycleSend(Handle, 0x02B1, 7, 0);
        System.Windows.Forms.Application.DoEvents();
        if (appliedDesktopBatches != before || !desktopChanges.HasWork) throw new InvalidOperationException("Disabled/modal owner was interrupted or lost its pause request.");
        LifecycleSend(Handle, 0x000A, 1, 0); System.Windows.Forms.Application.DoEvents();
        if (desktopChanges.HasWork || appliedDesktopBatches <= before) throw new InvalidOperationException("Enabling the owner did not resume queued system work.");
        var injected = false;
        void DuringPause()
        {
            if (processingDesktopChange && !injected) { injected = true; QueueDesktopChange(DesktopLifecycleSignal.SessionUnlocked); }
        }
        management.StatusChanged += DuringPause;
        try
        {
            before = appliedDesktopBatches; LifecycleSend(Handle, 0x0218, 0x12, 0); System.Windows.Forms.Application.DoEvents();
            if (!injected || appliedDesktopBatches < before + 2 || desktopChanges.HasWork) throw new InvalidOperationException("A reentrant unlock request was lost.");
        }
        finally { management.StatusChanged -= DuringPause; }
        management.InvalidateDisplay("Synthetic display read failure.");
        if (management.CanStartForLifecycleCheck) throw new InvalidOperationException("Stale display data could start management.");
        displays.RefreshDisplays();
        if (!management.CanStartForLifecycleCheck) throw new InvalidOperationException("Valid same-layout reread did not release the display gate.");
        before = appliedDesktopBatches; LifecycleSend(Handle, 0x0218, 10, 0); System.Windows.Forms.Application.DoEvents();
        if (appliedDesktopBatches != before || management.Diagnostics().Running || WindowEventListener.ForegroundWindow != foreground)
            throw new InvalidOperationException("Unrelated power status changed lifecycle or fixture changed foreground.");
        Console.WriteLine("PASS Own-window suspend/lock/resume messages gate input, defer behind disabled owners, preserve reentrant work, reject stale display data and stay stopped without OS session changes");
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint LifecycleSend(nint window, uint message, nuint parameter, nint data);
}
