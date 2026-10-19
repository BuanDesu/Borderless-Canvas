using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Discovery;

/// <summary>Process creation time is the native UTC FILETIME.</summary>
public readonly record struct NativeWindowIdentity(nint Handle, uint ProcessId, long ProcessCreationFileTime)
{
    public bool IsValid => Handle != 0 && ProcessId != 0 && ProcessCreationFileTime > 0;
}

/// <summary>One read-only observation; unknown native state stays nullable.</summary>
public sealed record WindowObservation
{
    public required nint Handle { get; init; }
    public uint ProcessId { get; init; }
    public long? ProcessCreationFileTime { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public RectD? Bounds { get; init; }
    public nint OwnerHandle { get; init; }
    public bool IsAlive { get; init; }
    public bool MetadataAvailable { get; init; }
    public bool IsVisible { get; init; }
    public bool IsChildWindow { get; init; }
    public bool IsToolWindow { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsTopMost { get; init; }
    public bool? IsCloaked { get; init; }
    public bool? IsOnCurrentDesktop { get; init; }

    public NativeWindowIdentity? Identity => ProcessCreationFileTime is > 0 && ProcessId != 0 && Handle != 0
        ? new(Handle, ProcessId, ProcessCreationFileTime.Value) : null;
}

public interface IWindowObservationSource
{
    /// <summary>Return a complete enumeration, or throw without updating the catalog.</summary>
    IReadOnlyList<WindowObservation> ReadWindows();
}
