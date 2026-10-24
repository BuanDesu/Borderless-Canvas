using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.Windows.Checks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--input-routing-only", StringComparer.Ordinal)) return NavigationRoutingChecks.RunStandalone();
        if (args.Contains("--capture-fixture", StringComparer.Ordinal)) return RecoveryChecks.CaptureFixture(args);
        if (args.Contains("--serve-management-fixtures", StringComparer.Ordinal)) return ManagedCanvasChecks.Serve();
        if (args.Contains("--serve-pin-menu", StringComparer.Ordinal)) return PointerAndPinChecks.ServeMenu();
        if (args.Contains("--recovery-display-only", StringComparer.Ordinal))
        {
            ApplicationConfiguration.Initialize(); var displayFailures = 0;
            RecoveryChecks.RunDisplayChanges((name, action) =>
            {
                try { action(); Console.WriteLine("PASS " + name); }
                catch (Exception error) { displayFailures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
            });
            return displayFailures == 0 ? 0 : 1;
        }
        if (args.Contains("--overview-only", StringComparer.Ordinal))
        {
            ApplicationConfiguration.Initialize();
            var errors = 0;
            OverviewChecks.Run((name, action) =>
            {
                try { action(); Console.WriteLine("PASS " + name); }
                catch (Exception error) { errors++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
            });
            return errors == 0 ? 0 : 1;
        }
        var storage = StorageChecks.Run();
        var bookmarkStorage = BookmarkStorageChecks.Run();
        var preferenceStorage = PreferencesStorageChecks.Run();
        if (preferenceStorage != 0) return preferenceStorage;
        if (bookmarkStorage != 0) return bookmarkStorage;
        if (storage != 0) return storage;
        if (args.Contains("--storage-only", StringComparer.Ordinal)) return NavigationRoutingChecks.RunStandalone();
        ApplicationConfiguration.Initialize();
        // These fixtures are transparent and never activate. Only their own state is changed.
        using var normal = new Fixture { Text = "BFW native fixture", Bounds = new(100, 100, 400, 300) };
        using var tool = new Fixture { Text = "BFW tool fixture", FormBorderStyle = FormBorderStyle.FixedToolWindow, ShowInTaskbar = false };
        using var owned = new Fixture { Text = "BFW owned fixture", ShowInTaskbar = false };
        normal.Show(); tool.Show(); owned.Show(normal);
        System.Windows.Forms.Application.DoEvents();
        var catalog = new DesktopWindowCatalog(0);
        var handle = normal.Handle;
        var first = Find(catalog, handle);
        if (first.Admission.IsAdmitted) FixtureDesktop.SetAnchor(handle);
        var results = new List<object>();
        var failures = 0;

        Check("Verified ordinary fixture is admitted", () =>
        {
            Require(first.Admission.IsAdmitted, $"Fixture excluded: {first.Admission.Reason}.");
            Require(first.Id is not null && first.Observation.Identity is { IsValid: true }, "No verified identity.");
        });
        Check("Default catalog excludes its own process", () =>
            Require(Find(new DesktopWindowCatalog(), handle).Admission.Reason == WindowExclusionReason.OwnProcess, "Own process admitted."));
        Check("Native tool style and owner relation are observed", () =>
        {
            Require(Find(catalog, tool.Handle).Admission.Reason == WindowExclusionReason.ToolWindow, "Tool style missed.");
            var modal = Find(catalog, owned.Handle);
            Require(modal.Observation.OwnerHandle == handle && modal.Admission.Reason == WindowExclusionReason.OwnedWindow, "Owner relation missed.");
        });
        Check("Rename and move preserve ID and update physical bounds", () =>
        {
            normal.Text = "BFW renamed fixture";
            normal.Location = new(280, 170);
            var changed = Find(catalog, handle);
            Require(changed.Id == first.Id && changed.Observation.Title == normal.Text, "Title update replaced identity.");
            Require(changed.Observation.Bounds?.Position.X == normal.Left, "Physical bounds do not match.");
        });
        DisplayChecks.Run(normal, Check);
        DesktopSessionChecks.Run(Check);
        Check("Read-only Windows wallpaper metadata matches the physical monitor without activating windows", () =>
        {
            var screen = Screen.PrimaryScreen!.Bounds;
            var foreground = WindowEventListener.ForegroundWindow;
            var wallpaper = DesktopWallpaperSource.Read(new(screen.Left, screen.Top, screen.Right, screen.Bottom));
            Require(wallpaper is not null && wallpaper.Monitor.Position.X == screen.Left && wallpaper.Monitor.Size.Width == screen.Width
                && wallpaper.Monitor.Size.Height == screen.Height, "Wallpaper monitor metadata unavailable or inconsistent.");
            Require(Enum.IsDefined(wallpaper!.Placement), "Unknown wallpaper placement.");
            Require(WindowEventListener.ForegroundWindow == foreground, "Wallpaper inspection changed foreground.");
        });
        Check("Wallpaper inspection rejects MTA before accessing COM or image files", () =>
        {
            var rejected = Task.Run(() =>
            {
                try { DesktopWallpaperSource.Read(new(0, 0, 100, 100)); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            Require(rejected, "MTA wallpaper access was accepted.");
        });
        LifecycleChecks.Run(Check);
        RecoveryChecks.Run(Check);
        OffscreenChecks.Run(Check);
        ManagedCanvasChecks.Run(Check);
        NavigationInputChecks.Run(Check);
        TouchReceiverChecks.Run(Check);
        NavigationRoutingChecks.Run(Check);
        PointerAndPinChecks.Run(normal, Check);
        OverviewChecks.Run(Check);
        Check("Hidden native fixture is excluded; showing it preserves its ID", () =>
        {
            normal.Hide();
            Require(Find(catalog, handle).Admission.Reason == WindowExclusionReason.NotVisible, "Hidden fixture admitted.");
            normal.Show();
            Require(Find(catalog, handle).Id == first.Id, "Visibility transition replaced identity.");
        });
        Check("Closed fixture disappears on the next complete inspection", () =>
        {
            owned.Close(); normal.Close();
            Require(catalog.Inspect().Entries.All(entry => entry.Observation.Handle != handle), "Closed HWND still present.");
        });

        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "native-discovery-checks.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                os = Environment.OSVersion.VersionString,
                fixture = new
                {
                    reason = first.Admission.Reason.ToString(),
                    first.Observation.MetadataAvailable,
                    identityVerified = first.Observation.Identity is not null,
                    first.Observation.IsCloaked,
                    first.Observation.IsOnCurrentDesktop,
                    owner = first.Observation.OwnerHandle.ToString()
                },
                results,
                failures,
                desktopMutationEnabled = false
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{results.Count - failures}/{results.Count} native checks passed.");
        return failures == 0 ? 0 : 1;

        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception)
            {
                failures++; results.Add(new { name, passed = false, error = exception.Message });
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }
    }

    private static InspectedWindow Find(DesktopWindowCatalog catalog, nint handle) =>
        catalog.Inspect().Entries.Single(entry => entry.Observation.Handle == handle);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : Form
    {
        internal Fixture() => Opacity = 0;
        protected override bool ShowWithoutActivation => true;
    }
}
