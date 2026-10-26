using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Presentation;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    internal void SetSmokeEntrance(double progress)
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Synthetic rendering only.");
        if (!entering) PrepareEntrance(); entranceProgress = OverviewMotion.Ease(progress); AnimatePreviews(); transitionCover?.Refresh();
    }
    internal void EndSmokeEntrance() => CompleteEntrance();
}
