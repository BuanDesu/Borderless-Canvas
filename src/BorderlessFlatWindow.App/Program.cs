using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(new DesktopWindowCatalog());
        if (args.Contains("--smoke-check", StringComparer.Ordinal))
        {
            // Realize child HWNDs without making an interactive window visible.
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            Directory.CreateDirectory(output);
            bitmap.Save(Path.Combine(output, "shell-smoke.png"));
            var candidateCount = new DesktopWindowCatalog().GetWindows().Count;
            File.WriteAllText(Path.Combine(output, "shell-smoke.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    os = Environment.OSVersion.VersionString,
                    candidateCount,
                    sampleWindows = 4,
                    desktopMutationEnabled = false
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            form.Close();
            return 0;
        }
        System.Windows.Forms.Application.Run(form);
        return 0;
    }
}
