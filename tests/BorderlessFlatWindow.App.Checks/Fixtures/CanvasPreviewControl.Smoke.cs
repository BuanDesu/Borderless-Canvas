using System.Drawing.Drawing2D;
using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.App;

internal sealed partial class CanvasPreviewControl
{
    internal static void CheckCompactSurfaces()
    {
        var workspace = new CanvasWorkspace(new(1280, 720));
        foreach (var populated in new[] { false, true })
        {
            if (populated) workspace.UpsertWindow(new(Guid.NewGuid(), "Synthetic compact map", new(new(-500, 30), new(640, 480))));
            foreach (var size in new[] { new Size(65, 65), new(96, 72), new(72, 96), new(432, 80), new(80, 432), new(432, 64), new(432, 300) })
            {
                using var control = new CanvasPreviewControl(workspace) { Size = size };
                using var bitmap = new Bitmap(size.Width, size.Height); using var graphics = Graphics.FromImage(bitmap);
                // Direct paint keeps a regression exception on the test call stack instead of opening a framework error dialog.
                control.OnPaint(new PaintEventArgs(graphics, control.ClientRectangle));
                if (size.Width > 64 && size.Height > 64 && (control.projection is null || !double.IsFinite(control.projection.Scale)))
                    throw new InvalidOperationException("Compact map projection is unavailable.");
            }
        }
        Console.WriteLine("PASS Compact empty/populated maps preserve a valid uniform projection with adaptive padding");
    }}
