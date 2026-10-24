using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

namespace BorderlessFlatWindow.App;

/// <summary>An explicit, temporary input surface. It never clips or hides the pointer.</summary>
internal sealed partial class TouchNavigationHost : IDisposable
{
    private readonly ContactPan contact = new();
    private readonly WindowCycleWheel cycleWheel = new();
    private readonly List<Form> windows = [];
    private readonly System.Windows.Forms.Timer focusTimer = new() { Interval = 100 };
    private bool closing;
    internal bool IsClosed => closing;
    internal event Action<NavigationInput>? Input;
    internal event Action? ExitRequested;
    internal NavigationInput? EndContact() => contact.Cancel();
    internal void AcceptContact(uint id, ContactPhase phase, PointD position)
    { if (contact.Accept(id, phase, position) is { } input) Input?.Invoke(input); }

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
        foreach (var area in workAreas)
        {
            var surface = new Surface(this) { Bounds = area };
            windows.Add(surface); surface.Show();
        }
        var selected = workAreas.FirstOrDefault(area => area.Contains(pointer));
        if (selected.IsEmpty) selected = workAreas[0];
        var tools = CreateTools(selected);
        windows.Add(tools); tools.Show(); tools.Activate();
        focusTimer.Tick += (_, _) =>
        {
            if (!closing && windows.All(window => window.Handle != WindowEventListener.ForegroundWindow)) ExitRequested?.Invoke();
        };
        focusTimer.Start();
    }
    private InputWindow CreateTools(Rectangle selected, bool passive = false)
    {
        var tools = new InputWindow(this, passive)
        {
            Text = "触控导航 · 单指拖动 / 两指滚动", FormBorderStyle = FormBorderStyle.FixedToolWindow,
            ClientSize = new(Math.Min(440, selected.Width - 24), 66), Padding = new(8)
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        void Add(string text, NavigationInputKind kind)
        {
            var button = new ChromeButton { Text = text, AccessibleName = kind == NavigationInputKind.Back ? "上一视角" : text,
                Size = new(Math.Max(44, (tools.ClientSize.Width - 16) / 4 - 6), 44), Margin = new(3) };
            button.Click += (_, _) => { if (kind == NavigationInputKind.Exit) ExitRequested?.Invoke(); else Input?.Invoke(new(kind, PointD.Zero)); };
            actions.Controls.Add(button);
        }
        Add("总览", NavigationInputKind.Overview); Add("原点", NavigationInputKind.Home);
        Add("返回", NavigationInputKind.Back); Add("退出", NavigationInputKind.Exit);
        tools.Controls.Add(actions); ChromeTheme.Apply(tools);
        tools.Location = new(selected.Left + (selected.Width - tools.Width) / 2, selected.Top + 12);
        tools.FormClosing += (_, args) => { if (!closing) { args.Cancel = true; tools.BeginInvoke(() => ExitRequested?.Invoke()); } };
        return tools;
    }
    public void Dispose()
    {
        if (closing) return;
        closing = true; focusTimer.Stop(); focusTimer.Dispose();
        foreach (var window in windows) { window.Close(); window.Dispose(); }
        windows.Clear();
    }
    private class InputWindow : Form
    {
        protected readonly TouchNavigationHost Host;
        private readonly bool passive;
        internal InputWindow(TouchNavigationHost host, bool passive = false)
        { Host = host; this.passive = passive; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation => passive;
        protected override CreateParams CreateParams
        { get { var value = base.CreateParams; value.ExStyle |= passive ? 0x08000080 : 0x80; return value; } }
        protected override void WndProc(ref Message message)
        {
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
    private sealed class Surface : InputWindow
    {
        private const uint MouseContact = uint.MaxValue;
        internal Surface(TouchNavigationHost host) : base(host)
        {
            Text = "BFW 触控导航"; FormBorderStyle = FormBorderStyle.None;
            BackColor = ChromeTheme.Purple; Opacity = .025; Cursor = Cursors.SizeAll;
        }
        protected override void WndProc(ref Message message)
        {
            if (DirectPointerInput.TryRead(message.Msg, message.WParam, out var sample))
            { Host.AcceptContact(sample.Id, sample.Phase, sample.Position); message.Result = 0; return; }
            base.WndProc(ref message);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !DirectPointerInput.IsPromotedContactMouse)
            { var point = PointToScreen(e.Location); Host.AcceptContact(MouseContact, ContactPhase.Down, new(point.X, point.Y)); Capture = true; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!DirectPointerInput.IsPromotedContactMouse)
            { var point = PointToScreen(e.Location); Host.AcceptContact(MouseContact, ContactPhase.Move, new(point.X, point.Y)); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !DirectPointerInput.IsPromotedContactMouse)
            { var point = PointToScreen(e.Location); Host.AcceptContact(MouseContact, ContactPhase.Up, new(point.X, point.Y)); Capture = false; }
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { if (!Capture) Host.AcceptContact(MouseContact, ContactPhase.Cancel, PointD.Zero); base.OnMouseCaptureChanged(e); }
    }
}
