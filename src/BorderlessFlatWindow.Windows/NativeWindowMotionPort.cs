using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Management;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

/// <summary>Preflight the group, then submit native deferred positions; outcomes still require observation.</summary>
public sealed class NativeWindowMotionPort : IWindowMotionPort
{
    private readonly RegisteredWindowMover validator = new();
    public MotionBatchResult RequestBatch(RecoverySession recovery, IReadOnlyList<WindowTranslation> translations)
    {
        if (translations.Count == 0) return new(true, "No movement needed.");
        if (translations.Select(item => item.RecoveryId).Distinct().Count() != translations.Count)
            return new(false, "Duplicate window in movement batch.");
        foreach (var item in translations)
        {
            var reason = validator.Validate(recovery, item.RecoveryId, item.Target);
            if (reason is not null) return new(false, reason);
        }
        var handles = recovery.Journal.Entries.ToDictionary(entry => entry.Id, entry => entry.Identity.Handle);
        if (translations.Any(item => NativeMethods.GetWindowThreadProcessId(handles[item.RecoveryId], out _) != NativeMethods.GetCurrentThreadId()))
        {
            // DeferWindowPos rejects ASYNCWINDOWPOS. Other threads must use documented nonblocking SetWindowPos requests.
            foreach (var item in translations)
            {
                var receipt = validator.RequestMove(recovery, item.RecoveryId, item.Target);
                if (!receipt.Accepted) return new(false, receipt.Detail);
            }
            return new(true, "Asynchronous cross-thread movement batch submitted.");
        }
        using var pixels = new PhysicalPixelScope();
        var batch = NativeMethods.BeginDeferWindowPos(translations.Count);
        if (batch == 0) return new(false, $"BeginDeferWindowPos failed: {Marshal.GetLastPInvokeError()}.");
        foreach (var item in translations)
        {
            batch = NativeMethods.DeferWindowPos(batch, handles[item.RecoveryId], 0, item.Target.Left, item.Target.Top,
                0, 0, 0x0200 | 0x0010 | 0x0004 | 0x0001);
            if (batch == 0) return new(false, $"DeferWindowPos failed: {Marshal.GetLastPInvokeError()}.");
        }
        return NativeMethods.EndDeferWindowPos(batch) ? new(true, "Native movement batch submitted.")
            : new(false, $"EndDeferWindowPos failed: {Marshal.GetLastPInvokeError()}.");
    }
    public bool RequestActivation(RecoverySession recovery, Guid recoveryId)
    {
        if (!recovery.CanMove(recoveryId)) return false;
        var window = recovery.Journal.Entries.Single(entry => entry.Id == recoveryId).Identity.Handle;
        return NativeMethods.SetForegroundWindow(window) && NativeMethods.GetForegroundWindow() == window;
    }
}
