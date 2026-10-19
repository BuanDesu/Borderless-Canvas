using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows;

public sealed record WindowMoveReceipt(bool Accepted, PixelRect? ObservedBounds, string Detail);

/// <summary>Accepts only IDs from a live recovery session. A queued request is not a verified placement.</summary>
public sealed class RegisteredWindowMover
{
    internal string? Validate(RecoverySession session, Guid entryId, PixelRect target)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Window movement requires an STA thread.");
        if (!WindowPlacementPlanner.WithinBudget(target)) return "Target exceeds the prototype coordinate budget.";
        var entry = session.Journal.Entries.FirstOrDefault(item => item.Id == entryId);
        if (entry is null || !session.CanMove(entryId)) return "No current durable ownership.";
        var window = entry.Identity.Handle;
        using var pixels = new PhysicalPixelScope();
        Marshal.SetLastPInvokeError(0);
        var extendedStyle = NativeMethods.GetWindowStyle(window, -20).ToInt64();
        if (extendedStyle == 0 && Marshal.GetLastPInvokeError() != 0) return "Window style unavailable.";
        if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window) || NativeMethods.IsZoomed(window)
            || NativeMethods.GetAncestor(window, 2) != window || (extendedStyle & 8) != 0)
            return "Only restored, visible, non-topmost top-level windows can be moved.";
        if (!NativeMethods.GetWindowRect(window, out var current)) return "Physical bounds unavailable.";
        if ((long)current.Right - current.Left != target.Width || (long)current.Bottom - current.Top != target.Height)
            return "Native size changed; movement must be reconciled before continuing.";
        if (!session.CanMove(entryId)) return "Window ownership changed before positioning.";
        return null;
    }

    public WindowMoveReceipt RequestMove(RecoverySession session, Guid entryId, PixelRect target)
    {
        var error = Validate(session, entryId, target);
        if (error is not null) return new(false, null, error);
        var window = session.Journal.Entries.Single(entry => entry.Id == entryId).Identity.Handle;
        using var pixels = new PhysicalPixelScope();
        const uint flags = 0x4000 | 0x0200 | 0x0010 | 0x0004 | 0x0001; // ASYNC, NOOWNERZORDER, NOACTIVATE, NOZORDER, NOSIZE
        if (!NativeMethods.SetWindowPos(window, 0, target.Left, target.Top, 0, 0, flags))
            return new(false, null, $"SetWindowPos failed: {Marshal.GetLastPInvokeError()}.");
        PixelRect? observed = null;
        if (NativeMethods.GetWindowRect(window, out var actual) && actual.Right > actual.Left && actual.Bottom > actual.Top)
            observed = new(actual.Left, actual.Top, actual.Right, actual.Bottom);
        return new(true, observed, observed == target ? "Placement observed." : "Request queued; await native observation.");
    }
}
