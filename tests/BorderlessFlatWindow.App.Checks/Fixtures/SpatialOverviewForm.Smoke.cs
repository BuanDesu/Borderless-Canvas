using System.Drawing.Drawing2D;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Bookmarks;

namespace BorderlessFlatWindow.App;

internal sealed partial class SpatialOverviewForm
{
    internal void SetSmokeQuery(string text) { if (scene is not null) scene.SetQuery(text); else search.Text = text; }}
