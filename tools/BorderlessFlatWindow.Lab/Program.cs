namespace BorderlessFlatWindow.App;

internal static class LabProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        using var font = new PixelFont();
        if (args.Length == 0) { Console.Error.WriteLine("Select touchpad, overview or benchmark, with an output path."); return 2; }
        if (args[0] == "touchpad")
        {
            using var lab = new TouchpadInputLab(args.Length > 1 ? Path.GetFullPath(args[1]) : null);
            System.Windows.Forms.Application.Run(lab); return 0;
        }
        if (args.Length < 2) return 2;
        var output = Path.GetFullPath(args[1]);
        return args[0] switch
        {
            "overview" => LiveOverviewLab.Run(output, args.Contains("--interactive"), args.Contains("--solid")),
            "benchmark" => OverviewLoadBenchmark.Run(output),
            _ => 2
        };
    }
}
