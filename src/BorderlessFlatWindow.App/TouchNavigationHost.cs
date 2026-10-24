using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>A compact, nonactivating toolbar; no full-screen surface or contact capture.</summary>
internal sealed partial class TouchNavigationHost : IDisposable
{
    private readonly WindowCycleWheel cycleWheel = new();
    private readonly List<Form> windows = [];
    private bool closing;
    internal bool IsClosed => closing;
    internal event Action<NavigationInput>? Input;
    internal event Action? ExitRequested;

    internal void AcceptWheel(int delta, bool horizontal, bool shift, bool control)
    {
        if (delta == 0) return;
        if (control && !horizontal)
        {
            var steps = cycleWheel.Add(delta, Environment.TickCount64);
            if (steps != 0) Input?.Invoke(new(NavigationInputKind.CycleWindow, PointD.Zero, steps * 120));
        }
        else
        {
            cycleWheel.Reset();
            Input?.Invoke(new(horizontal ? NavigationInputKind.HorizontalWheel : NavigationInputKind.Wheel, PointD.Zero, delta, shift));
        }
    }
    internal void Show(Rectangle[] workAreas, Point pointer)
    {
        if (windows.Count != 0 || workAreas.Length == 0) throw new InvalidOperationException("Touch navigation needs fresh display surfaces.");
        var selected = workAreas.FirstOrDefault(area => area.Contains(pointer));
        if (selected.IsEmpty) selected = workAreas[0];
        var tools = CreateTools(selected, passive: true); tools.TopMost = true;
        windows.Add(tools); tools.Show();
    }
    private InputWindow CreateTools(Rectangle selected, bool passive = false)
    {
        var tools = new InputWindow(this, passive)
        {
            Text = "触控导航", ClientSize = new(Math.Min(440, selected.Width - 24), 60), Padding = new(8), TopMost = false
        };
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actions.RowStyles.Add(new(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.Absolute, 96)); actions.ColumnStyles.Add(new(SizeType.Absolute, 40));
        actions.Controls.Add(new Label { Text = "双指平移", AccessibleName = "触控导航 · 双指平移", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        void Add(string text, NavigationInputKind kind)
        {
            var button = new Button { Text = text, AccessibleName = kind == NavigationInputKind.Exit ? "退出触控导航" : text,
                Dock = DockStyle.Fill, Margin = new(2), FlatStyle = FlatStyle.Flat, ForeColor = tools.ForeColor, BackColor = tools.BackColor, TabStop = false };
            button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 67, 77);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(53, 85, 90);
            button.Click += (_, _) => { if (kind == NavigationInputKind.Exit) ExitRequested?.Invoke(); else Input?.Invoke(new(kind, PointD.Zero)); };
            actions.Controls.Add(button, kind == NavigationInputKind.Exit ? 2 : 1, 0);
        }
        Add("总览", NavigationInputKind.Overview); Add("×", NavigationInputKind.Exit);
        tools.Controls.Add(actions);
        tools.Location = new(selected.Left + (selected.Width - tools.Width) / 2, selected.Top + 12);
        tools.FormClosing += (_, args) => { if (!closing) { args.Cancel = true; tools.BeginInvoke(() => ExitRequested?.Invoke()); } };
        return tools;
    }
    public void Dispose()
    {
        if (closing) return;
        closing = true;
        foreach (var window in windows) { window.Close(); window.Dispose(); }
        windows.Clear();
    }
    private class InputWindow : NavigationOverlay
    {
        protected readonly TouchNavigationHost Host;
        private readonly bool passive;
        internal InputWindow(TouchNavigationHost host, bool passive = false) : base(mouseTransparent: false)
        { Host = host; this.passive = passive; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = false; }
        protected override void WndProc(ref Message message)
        {
            if (passive && message.Msg == 0x21) { message.Result = 3; return; }
            if (message.Msg is 0x20A or 0x20E)
            {
                var modifiers = unchecked((uint)(long)message.WParam) & 0xFFFF;
                Host.AcceptWheel(unchecked((short)((long)message.WParam >> 16)), message.Msg == 0x20E,
                    (modifiers & 4) != 0, (modifiers & 8) != 0);
                message.Result = 0; return;
            }
            base.WndProc(ref message);
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == Keys.Escape) { Host.ExitRequested?.Invoke(); return true; }
            var kind = keys switch
            { Keys.Tab or (Keys.Shift | Keys.Tab) => NavigationInputKind.Overview, Keys.Home => NavigationInputKind.Home, Keys.Back => NavigationInputKind.Back, _ => (NavigationInputKind?)null };
            if (kind is { } command) { Host.Input?.Invoke(new(command, PointD.Zero)); return true; }
            return base.ProcessCmdKey(ref message, keys);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.KeyCode is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey) Host.cycleWheel.Reset(); base.OnKeyUp(e); }
    }
}
