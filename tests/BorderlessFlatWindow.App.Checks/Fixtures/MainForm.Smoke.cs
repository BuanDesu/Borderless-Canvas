using System.ComponentModel;
using BorderlessFlatWindow.Application;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Ports;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;
using BorderlessFlatWindow.Application.Bookmarks;
using BorderlessFlatWindow.Windows.Bookmarks;
using BorderlessFlatWindow.Application.Preferences;
using BorderlessFlatWindow.Windows.Preferences;

namespace BorderlessFlatWindow.App;

internal sealed partial class MainForm
{
    internal void PrepareSampleSmoke() => tabs.SelectedIndex = 1;
    internal void PrepareRuntimeChromeSmoke()
    {
        if (!showWithoutActivation) throw new InvalidOperationException("Verification entry only.");
        var pages = tabs.TabPages.Cast<TabPage>().ToArray();
        tabs.TabPages.Clear();
        var diagnostics = new TabPage("设备诊断");
        var children = new ChromeTabs { Dock = DockStyle.Fill };
        pages[2].Text = "窗口候选"; pages[3].Text = "显示器与坐标"; pages[4].Text = "真实空间地图";
        children.TabPages.AddRange([pages[2], pages[3], pages[4]]); diagnostics.Controls.Add(children);
        tabs.TabPages.AddRange([pages[0], diagnostics, pages[5]]); pages[5].Text = "恢复中心";
        verificationPages.AddRange([pages[1], pages[6]]); ChromeTheme.Apply(diagnostics);
        if (tabs.TabCount != 3 || children.TabCount != 3) throw new InvalidOperationException("Runtime page hierarchy is invalid.");
    }
    internal void PrepareRuntimePageSmoke(int index, int diagnostic = 0)
    {
        tabs.SelectedIndex = index;
        if (index == 1) tabs.TabPages[1].Controls.OfType<TabControl>().Single().SelectedIndex = diagnostic;
    }
    internal void CheckTraySmoke()
    {
        InitializeTray();
        if (tray!.Icon?.Handle == 0 || tray.Icon is null || !tray.Visible) throw new InvalidOperationException("Tray icon resource is missing.");
        using (var icon = tray.Icon.ToBitmap())
        {
            if (icon.Width < 16 || icon.Height < 16) throw new InvalidOperationException("Tray icon is too small.");
            var visible = 0;
            for (var y = 0; y < icon.Height; y++) for (var x = 0; x < icon.Width; x++) if (icon.GetPixel(x, y).A > 128) visible++;
            if (visible < icon.Width * icon.Height / 2) throw new InvalidOperationException("Tray icon artwork is transparent.");
        }
        Hide();
        if (Visible) throw new InvalidOperationException("Manager did not hide.");
        trayOpen!.PerformClick(); System.Windows.Forms.Application.DoEvents();
        if (!Visible || WindowState != FormWindowState.Normal) throw new InvalidOperationException("Tray open action did not restore the manager.");
        if (trayPause!.Enabled || trayOverview!.Enabled || !traySettings!.Enabled || tray!.Text.Length > 63)
            throw new InvalidOperationException("Idle tray command state is invalid.");
        trayRecovery!.PerformClick(); System.Windows.Forms.Application.DoEvents();
        if (!ReferenceEquals(tabs.SelectedTab, recovery.Parent)) throw new InvalidOperationException("Tray recovery action did not select the recovery page.");
        CheckTrayCommands();
        tray!.Visible = false; tray.Dispose(); tray = null; trayMenu!.Dispose(); trayMenu = null;
        lastTrayPresentation = null;
    }

    internal void PrepareCatalogSmoke()
    {
        tabs.SelectedIndex = 2;
        showExcluded.Checked = true;
        RefreshCatalog();
    }

    internal void PrepareDisplaySmoke()
    {
        tabs.SelectedIndex = 3;
        displays.RefreshDisplays();
    }

    internal void PrepareLiveSmoke()
    {
        tabs.SelectedIndex = 4;
        RefreshCatalog();
        livePreview.FitToContent();
    }

    internal (int WindowCount, bool Watching) GetTrackingDiagnostics() =>
        (liveWorkspace.Windows.Count, listener?.IsRunning == true);

    internal void PrepareRecoverySmoke()
    {
        tabs.SelectedIndex = 5;
        recovery.RefreshJournal();
    }
    internal void CheckRecoveryErrorSmoke() { tabs.SelectedIndex = 5; recovery.CheckCorruptJournalSmoke(); }
    internal void PrepareBookmarkSmoke()
    {
        bookmarks.Add("工作区", new(0, 0)); bookmarks.Add("工作区", new(-4800, 2700));
        bookmarks.Add("资料", new(9600, -3600)); bookmarkControl.RefreshSmoke(); tabs.SelectedIndex = 6;
    }
    internal void PrepareBookmarkSearchSmoke() => bookmarkControl.SetSmokeQuery("资料");
    internal void CheckBookmarkCommandsSmoke() => bookmarkControl.CheckSmokeCommands();
    internal void ReloadBookmarksSmoke() { bookmarks.Reload(); bookmarkControl.RefreshSmoke(); }
    internal void ResetBookmarksSmoke()
    { if (!bookmarks.BackupAndReset()) throw new InvalidOperationException(bookmarks.Message); bookmarkControl.RefreshSmoke(); }

}
