using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows.Interop;

namespace BorderlessFlatWindow.Windows.Recovery;

public sealed class WindowsRecoveryWindowPort : IRecoveryWindowPort
{
    private readonly WindowAdmissionPolicy policy;
    private readonly WindowsWindowObservationSource source = new();
    public WindowsRecoveryWindowPort() : this((uint)Environment.ProcessId) { }
    public WindowsRecoveryWindowPort(uint ownProcessId) => policy = new(ownProcessId);

    public OriginalWindowState Capture(NativeWindowIdentity identity)
    {
        CheckThread();
        var observation = source.ReadWindow(identity.Handle);
        var admission = policy.Evaluate(observation);
        if (observation.Identity != identity || !admission.IsAdmitted && admission.Reason != WindowExclusionReason.Minimized)
            throw new InvalidOperationException($"Window is no longer eligible for management: {admission.Reason}; identityMatches={observation.Identity == identity}.");
        using var pixels = new PhysicalPixelScope();
        var placement = ReadPlacement(identity.Handle);
        var bounds = observation.Bounds!.Value;
        var physical = new PixelRect(checked((int)bounds.Position.X), checked((int)bounds.Position.Y),
            checked((int)bounds.Right), checked((int)bounds.Bottom));
        if (observation.IsMinimized)
        {
            var monitor = NativeMethods.MonitorFromWindow(identity.Handle, 2);
            var info = new NativeMethods.NativeMonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.NativeMonitorInfo>(), DeviceName = "" };
            if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Cannot capture minimized normal bounds.");
            var x = info.WorkArea.Left - info.Bounds.Left; var y = info.WorkArea.Top - info.Bounds.Top;
            physical = new(checked(placement.Left + x), checked(placement.Top + y), checked(placement.Right + x), checked(placement.Bottom + y));
        }
        if (!placement.IsValid || ReadIdentity(identity.Handle) != identity)
            throw new InvalidOperationException("Cannot capture a stable original window state.");
        return new(placement, physical);
    }

    public void Mark(RecoveryEntry entry, string markerName)
    {
        CheckThread(); CheckMarkerName(markerName);
        if (!entry.Identity.IsValid || entry.Marker <= 0 || ReadIdentity(entry.Identity.Handle) != entry.Identity)
            throw new InvalidOperationException("Window changed before ownership registration.");
        if (NativeMethods.GetPropW(entry.Identity.Handle, markerName) != 0)
            throw new InvalidOperationException("Recovery marker is already in use.");
        if (!NativeMethods.SetPropW(entry.Identity.Handle, markerName, entry.Marker))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot mark this window; it must not be moved.");
        if (!Matches(entry, markerName)) throw new InvalidOperationException("Window changed while setting recovery marker.");
    }

    public bool Matches(RecoveryEntry entry, string markerName)
    {
        CheckThread(); CheckMarkerName(markerName);
        return entry.Identity.IsValid && entry.Marker > 0 && ReadIdentity(entry.Identity.Handle) == entry.Identity
            && NativeMethods.GetPropW(entry.Identity.Handle, markerName) == entry.Marker;
    }

    public void RemoveMarker(RecoveryEntry entry, string markerName)
    {
        CheckThread(); CheckMarkerName(markerName);
        if (NativeMethods.GetPropW(entry.Identity.Handle, markerName) != entry.Marker) return;
        if (NativeMethods.RemovePropW(entry.Identity.Handle, markerName) != entry.Marker)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot release recovery marker.");
    }

    public RecoveryResult Restore(RecoveryEntry entry, string markerName)
    {
        CheckThread(); CheckMarkerName(markerName);
        if (!entry.Armed || !entry.Original.Placement.IsValid)
            return new(entry.Id, RecoveryOutcome.Failed, "Entry has no durable movement authorization.");
        var guard = Verify(entry, markerName);
        if (guard is not null) return guard;
        using var pixels = new PhysicalPixelScope();
        var saved = entry.Original.Placement;
        var originalBounds = ToNative(entry.Original.PhysicalBounds);
        RecoveryPlacementTarget? fallback = null; nint fallbackMonitor = 0;
        NativeMethods.NativeMonitorInfo fallbackInfo = default;
        if (NativeMethods.MonitorFromRect(in originalBounds, 0) == 0)
        {
            fallbackMonitor = NativeMethods.MonitorFromRect(in originalBounds, 2);
            fallbackInfo = new() { Size = (uint)Marshal.SizeOf<NativeMethods.NativeMonitorInfo>(), DeviceName = "" };
            if (fallbackMonitor == 0 || !NativeMethods.GetMonitorInfo(fallbackMonitor, ref fallbackInfo))
                return new(entry.Id, RecoveryOutcome.Failed, "Original display is unavailable and current work area could not be verified; journal retained.");
            fallback = RecoveryPlacementFallback.Plan(entry.Original, ToPixels(fallbackInfo.Bounds), ToPixels(fallbackInfo.WorkArea));
            if (fallback is null) return new(entry.Id, RecoveryOutcome.Failed, "Original display is unavailable; a safe normal-state placement could not be planned. Journal retained.");
            saved = fallback.Placement;
        }
        var placement = new NativeMethods.NativeWindowPlacement
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.NativeWindowPlacement>(), Flags = saved.Flags | 4,
            ShowCommand = saved.ShowCommand switch { 1 => 4u, 2 => 7u, _ => saved.ShowCommand },
            MinPosition = new() { X = saved.MinX, Y = saved.MinY }, MaxPosition = new() { X = saved.MaxX, Y = saved.MaxY },
            NormalPosition = new() { Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom }
        };
        // Repeat immediately before the operation. No stale catalog is used for recovery.
        guard = Verify(entry, markerName);
        if (guard is not null) return guard;
        if (!NativeMethods.SetWindowPlacement(entry.Identity.Handle, in placement))
            return new(entry.Id, RecoveryOutcome.Failed, $"SetWindowPlacement failed: {Marshal.GetLastPInvokeError()}.");
        // Always queue a restore, even when current bounds already match: older asynchronous moves may still be pending.
        if (NativeMethods.SendMessageTimeout(entry.Identity.Handle, 0, 0, 0, 1 | 2 | 0x20, 150, out _) == 0)
            return new(entry.Id, RecoveryOutcome.Failed, "Target did not respond; a restore was queued and the journal is retained.");
        var timeout = Stopwatch.StartNew();
        do
        {
            guard = Verify(entry, markerName);
            if (guard is not null) return guard;
            var current = ReadPlacement(entry.Identity.Handle);
            if (SamePlacement(current, saved) && (fallback is null || VerifyFallback(entry.Identity.Handle, originalBounds, fallback, fallbackMonitor, fallbackInfo)))
            {
                RemoveMarker(entry, markerName);
                return new(entry.Id, RecoveryOutcome.Restored, fallback is null ? "Original placement and show state verified."
                    : "Original display unavailable; nearest work-area placement, native dimensions and show state verified.");
            }
            Thread.Sleep(10);
        } while (timeout.Elapsed < TimeSpan.FromMilliseconds(1200));
        return new(entry.Id, RecoveryOutcome.Failed, "Placement not confirmed within 1200ms; journal retained for retry.");
    }

    private static bool SamePlacement(SavedWindowPlacement current, SavedWindowPlacement saved) =>
        current.ShowCommand == saved.ShowCommand && current.Left == saved.Left && current.Top == saved.Top
        && current.Right == saved.Right && current.Bottom == saved.Bottom;

    private static bool VerifyFallback(nint window, NativeMethods.NativeRect original, RecoveryPlacementTarget target,
        nint monitor, NativeMethods.NativeMonitorInfo prior)
    {
        var current = new NativeMethods.NativeMonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.NativeMonitorInfo>(), DeviceName = "" };
        return NativeMethods.MonitorFromRect(in original, 0) == 0 && NativeMethods.MonitorFromRect(in original, 2) == monitor
            && NativeMethods.GetMonitorInfo(monitor, ref current) && current.DeviceName == prior.DeviceName
            && ToPixels(current.Bounds) == ToPixels(prior.Bounds) && ToPixels(current.WorkArea) == ToPixels(prior.WorkArea)
            && NativeMethods.GetWindowRect(window, out var actual) && ToPixels(actual) == target.PhysicalBounds;
    }
    private static PixelRect ToPixels(NativeMethods.NativeRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    private static NativeMethods.NativeRect ToNative(PixelRect rect) => new() { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };

    private static RecoveryResult? Verify(RecoveryEntry entry, string markerName)
    {
        if (!NativeMethods.IsWindow(entry.Identity.Handle))
            return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "Window closed; no operation.");
        NativeMethods.GetWindowThreadProcessId(entry.Identity.Handle, out var pid);
        if (pid != entry.Identity.ProcessId)
            return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "HWND now belongs to a different process; no operation.");
        var current = ReadIdentity(entry.Identity.Handle);
        if (current is null) return new(entry.Id, RecoveryOutcome.Failed, "Process identity unavailable; no operation.");
        if (current != entry.Identity || NativeMethods.GetPropW(entry.Identity.Handle, markerName) != entry.Marker)
            return new(entry.Id, RecoveryOutcome.SkippedClosedOrReused, "Instance identity or ownership marker differs; no operation.");
        return null;
    }

    private static NativeWindowIdentity? ReadIdentity(nint window)
    {
        if (!NativeMethods.IsWindow(window)) return null;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        var created = WindowsWindowObservationSource.ReadCreationTime(pid);
        NativeMethods.GetWindowThreadProcessId(window, out var finalPid);
        return created is > 0 && pid != 0 && pid == finalPid && NativeMethods.IsWindow(window)
            ? new(window, pid, created.Value) : null;
    }

    private static SavedWindowPlacement ReadPlacement(nint window)
    {
        var placement = new NativeMethods.NativeWindowPlacement { Length = (uint)Marshal.SizeOf<NativeMethods.NativeWindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(window, ref placement))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read WINDOWPLACEMENT.");
        return new(placement.Flags, placement.ShowCommand, placement.MinPosition.X, placement.MinPosition.Y,
            placement.MaxPosition.X, placement.MaxPosition.Y, placement.NormalPosition.Left, placement.NormalPosition.Top,
            placement.NormalPosition.Right, placement.NormalPosition.Bottom);
    }

    private static void CheckThread()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Window recovery requires an STA thread.");
    }
    private static void CheckMarkerName(string name)
    {
        const string prefix = "BorderlessFlatWindow.Recovery.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new ArgumentException("Invalid recovery property name.", nameof(name));
    }
}
