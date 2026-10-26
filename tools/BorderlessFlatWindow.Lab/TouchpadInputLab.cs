using System.Text.Json;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;
using BorderlessFlatWindow.Windows;

using System.Drawing;
using System.Windows.Forms;

namespace BorderlessFlatWindow.App;

/// <summary>Uses the production receiver in a bounded own-window area, without a canvas session or recovery data.</summary>
internal sealed class TouchpadInputLab : Form
{
    private readonly ObservedPanel area = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 42, 58) };
    private readonly TextBox report = new() { Dock = DockStyle.Bottom, Height = 200, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox editor = new() { Width = 300, Text = "点此输入，验证普通点击仍可用" };
    private readonly System.Windows.Forms.Timer frame = new() { Interval = 16 };
    private readonly ContextMenuStrip context = new();
    private readonly string? reportPath;
    private GlobalNavigationInput? input;
    private readonly NavigationGestureSession gesture;
    private PointD camera;
    private string selectedMode = "停止", capturedInput = "未开启", capturedReceiver = "未开启";
    private bool running, stopping;
    private int vertical, horizontal, applied, leftClicks, rightClicks, textChanges, windowVertical, windowHorizontal, pointerMessages, gestureMessages;
    private long lastReport;
    internal TouchpadInputLab(string? reportPath)
    {
        this.reportPath = reportPath; Text = "BFW Touchpad Check"; ClientSize = new(880, 650); MinimumSize = new(740, 580);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 10);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new(8) };
        void Add(string text, Action action) { var button = new Button { Text = text, AutoSize = true, Height = 38 }; button.Click += (_, _) => action(); controls.Controls.Add(button); }
        Add("无焦点接收（对照）", () => Start(false)); Add("焦点接收（修复）", () => Start(true)); Add("停止并保留计数", Stop);
        Controls.Add(area); Controls.Add(report); Controls.Add(controls);
        var instructions = new Label { Dock = DockStyle.Top, Height = 76, Padding = new(16, 8, 16, 8), ForeColor = Color.White,
            Text = "先选接收方式，再把光标放到此深色区域，用触控板两指上下、左右及斜向滑动。\n下方显示实际消息和模拟相机位移；单击输入框、双指轻点测试原生右键。Esc 停止。" };
        area.Controls.Add(instructions); editor.Location = new(24, 110); area.Controls.Add(editor);
        editor.TextChanged += (_, _) => textChanges++;
        area.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) leftClicks++; if (e.Button == MouseButtons.Right) rightClicks++; };
        editor.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) leftClicks++; if (e.Button == MouseButtons.Right) rightClicks++; };
        context.Items.Add("本地右键菜单已打开"); area.ContextMenuStrip = context;
        area.MessageSeen += ObserveMessage;
        gesture = new(() => camera, (position, _) => { camera = position; applied++; area.Invalidate(); return true; }, _ => { });
        area.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(70, 130, 160));
            for (var x = (int)(-camera.X % 50); x < area.Width; x += 50) e.Graphics.DrawLine(pen, x, 160, x, area.Height);
            for (var y = 160 + (int)(-camera.Y % 50); y < area.Height; y += 50) e.Graphics.DrawLine(pen, 0, y, area.Width, y);
        };
        frame.Tick += (_, _) => TickInput();
        ResizeBegin += (_, _) => Stop(); LocationChanged += (_, _) => { if (running) Stop(); };
        FormClosing += (_, _) => Stop();
    }
    private void Start(bool focus)
    {
        Stop();
        input ??= new(() => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) frame.Start(); }); }, new(twoButtonDrag: false, edgePan: false));
        var bounds = area.RectangleToScreen(area.ClientRectangle);
        input.ConfigureScreens([new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)], [new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)]);
        input.SetAvailable(true);
        if (!input.SetTouchNavigation(true)) { report.Text = "接收器未能开启"; return; }
        selectedMode = focus ? "焦点接收" : "无焦点对照"; running = true;
        vertical = horizontal = applied = leftClicks = rightClicks = textChanges = windowVertical = windowHorizontal = pointerMessages = gestureMessages = 0; camera = PointD.Zero;
        if (focus) input.FocusTouchNavigation(); frame.Start(); TickInput();
    }
    private void TickInput()
    {
        if (input is null || stopping) return;
        if (!running) { input.Drain(); frame.Stop(); return; }
        foreach (var packet in input.Drain())
        {
            var kind = packet.Input.Kind;
            if (kind is NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel)
            { if (kind == NavigationInputKind.HorizontalWheel || packet.Input.Shift) horizontal++; else vertical++; gesture.Accept(packet.Input, packet.At); }
            else if (kind == NavigationInputKind.Fault || kind == NavigationInputKind.Exit && input.State.Touch != true) { Stop(); return; }
        }
        gesture.Tick(DateTimeOffset.UtcNow);
        capturedInput = input.TouchInputMessage;
        capturedReceiver = input.TouchReceiverMessage;
        if (Environment.TickCount64 - lastReport < 100) return; lastReport = Environment.TickCount64;
        report.Text = $"touchpad-hit-r2 · {selectedMode} · 可用 {input.State.Available} / 触控 {input.State.Touch}\r\n{capturedInput}\r\n{capturedReceiver}\r\n"
            + $"UI 纵向 {vertical} / 横向 {horizontal} · 相机帧 {applied} · X={camera.X:0.##} Y={camera.Y:0.##}\r\n"
            + $"测试页面收到：纵向 {windowVertical} / 横向 {windowHorizontal} / Pointer {pointerMessages} / Gesture {gestureMessages}\r\n"
            + $"下方应用：左键 {leftClicks} / 右键 {rightClicks} / 输入变化 {textChanges}\r\n"
            + "此页仅移动网格。先点停止，再选中这些文字复制。";
    }
    private void Stop()
    {
        if (stopping) return; stopping = true;
        try
        {
            running = false; frame.Stop(); input?.SetTouchNavigation(false); input?.SetAvailable(false); gesture.Cancel();
            if (reportPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Build = "touchpad-hit-r2", Mode = selectedMode, Vertical = vertical,
                    Horizontal = horizontal, CameraFrames = applied, CameraX = camera.X, CameraY = camera.Y, LeftClicks = leftClicks,
                    RightClicks = rightClicks, TextChanges = textChanges, WindowVertical = windowVertical, WindowHorizontal = windowHorizontal,
                    PointerMessages = pointerMessages, GestureMessages = gestureMessages, Input = capturedInput, Receiver = capturedReceiver }));
            }
        }
        finally { stopping = false; }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { Stop(); frame.Dispose(); input?.Dispose(); input = null; context.Dispose(); } base.Dispose(disposing); }
    private void ObserveMessage(int message)
    {
        if (!running) return;
        if (message == 0x20A) windowVertical++;
        if (message == 0x20E) windowHorizontal++;
        if (message is >= 0x245 and <= 0x24F) pointerMessages++;
        if (message == 0x119) gestureMessages++;
    }
    protected override void WndProc(ref Message message) { ObserveMessage(message.Msg); base.WndProc(ref message); }
    private sealed class ObservedPanel : Panel
    {
        internal event Action<int>? MessageSeen;
        protected override void WndProc(ref Message message) { MessageSeen?.Invoke(message.Msg); base.WndProc(ref message); }
    }
}
