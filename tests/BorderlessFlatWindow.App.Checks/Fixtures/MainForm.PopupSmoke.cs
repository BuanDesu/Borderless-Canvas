using BorderlessFlatWindow.Core.Appearance;
using BorderlessFlatWindow.Windows;
using Microsoft.Win32;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal void PreparePopupSmoke()
    {
        var scale = DeviceDpi / 96f;
        ClientSize = new((int)(432 * scale), (int)(700 * scale)); MinimumSize = new((int)(360 * scale), (int)(540 * scale));
        FormBorderStyle = FormBorderStyle.None; Padding = new(6);
        UpdatePopupRegion(); PerformLayout();
    }}
