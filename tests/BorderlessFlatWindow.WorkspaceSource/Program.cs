using System.Text.Json;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.WorkspaceSource;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Source(args);
        System.Windows.Forms.Application.Run(form);
    }
    private sealed class Source : Form
    {
        private readonly System.Windows.Forms.Timer lifetime = new() { Interval = 120000 };
        private readonly string ready = Path.ChangeExtension(Environment.ProcessPath!, ".ready");
        internal Source(string[] args)
        {
            var area = Screen.PrimaryScreen!.WorkingArea;
            var second = string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "B", StringComparison.Ordinal);
            Text = "Synthetic workspace " + (second ? "B" : "A"); Bounds = new(area.Left + (second ? 660 : 80), area.Top + 100, 500, 340);
            StartPosition = FormStartPosition.Manual; Opacity = 0; ShowInTaskbar = true;
            Shown += (_, _) =>
            {
                File.WriteAllText(Path.ChangeExtension(ready, ".args.json"), JsonSerializer.Serialize(args));
                File.WriteAllText(ready, Environment.ProcessId + ":" + Handle.ToInt64());
                lifetime.Start();
            };
            lifetime.Tick += (_, _) => Close();
        }
        protected override bool ShowWithoutActivation => true;
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Existing user managers must leave this fixture alone. The native
            // harness explicitly owns these test handles through its injected port.
            if (!SetPropW(Handle, "BorderlessFlatWindow.Fixed.v1", 0x42465750)) throw new InvalidOperationException("Cannot isolate own workspace fixture.");
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetPropW(nint window, string name, nint value);
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000000; return value; } }
        protected override void Dispose(bool disposing) { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
    }
}
