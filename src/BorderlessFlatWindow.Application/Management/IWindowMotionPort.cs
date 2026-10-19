using BorderlessFlatWindow.Application.Recovery;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Application.Management;

public sealed record WindowTranslation(Guid RecoveryId, PixelRect Target);
public sealed record MotionBatchResult(bool Accepted, string Detail);
public interface IWindowMotionPort
{
    MotionBatchResult RequestBatch(RecoverySession recovery, IReadOnlyList<WindowTranslation> translations);
    bool RequestActivation(RecoverySession recovery, Guid recoveryId);
}
