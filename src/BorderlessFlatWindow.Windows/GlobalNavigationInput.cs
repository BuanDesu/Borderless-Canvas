using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed record NavigationHotkey(uint Modifiers, uint Key, string Label)
{
    public static readonly NavigationHotkey Default = new(3, 0x20, "Ctrl+Alt+Space");
}
public sealed record GlobalNavigationState(bool Active, bool Registered, NavigationHotkey Hotkey, string Message, bool Manual = false);
public readonly record struct TimedNavigationInput(NavigationInput Input, DateTimeOffset At, long CapturedTimestamp = 0);

/// <summary>Dedicated message-loop thread. Subscribers only post a UI wake; never move windows here.</summary>
public sealed class GlobalNavigationInput : IDisposable
{
    private const uint CommandMessage = 0x8001;
    private const int ToggleId = 0x4BF0, EscapeId = 0x4BF1, HomeId = 0x4BF2, BackId = 0x4BF3, OverviewId = 0x4BF4;
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly ConcurrentQueue<TimedNavigationInput> inputs = new();
    private readonly NavigationInputGate gate = new();
    private readonly DesktopNavigationPolicy automatic = new();
    private readonly DesktopContextClick contextClick = new();
    private static readonly nuint ContextClickTag = 0x42465743;
    private nint contextTarget;
    private long contextGeneration;
    private readonly Action wakeUi;
    private readonly HookCallback callback;
    private readonly HookCallback keyboardCallback;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private GlobalNavigationState state = new(false, false, NavigationHotkey.Default, "正在注册快捷键。");
    private Exception? startupError;
    private uint threadId;
    private nint hook;
    private nint keyboardHook;
    private bool manual, tabHeld, tabCaptured;
    private readonly HashSet<uint> capturedKeys = [];
    private nuint timer;
    private int wakePending, overflow, stopping;
    private long heartbeat = Environment.TickCount64;
    private bool available, escapeRegistered, disposed;
    private readonly BorderlessFlatWindow.Core.Canvas.OverviewInputSession overviewInput = new();
    private long overviewSequence;
    private bool overviewOpen => overviewInput.IsOpen;
    public void SetOverviewHost(long generation, nint handle)
        => Post(() => overviewInput.AttachHost(generation, handle, WindowEventListener.ForegroundWindow));
    private bool overviewRegistered;
    private string overviewMessage = "正在注册总览快捷键。";

    public GlobalNavigationInput(Action wakeUi)
    {
        this.wakeUi = wakeUi; callback = MouseCallback; keyboardCallback = KeyboardCallback;
        thread = new(ThreadMain) { IsBackground = true, Name = "BFW navigation input" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Interlocked.Exchange(ref stopping, 1); if (threadId != 0) PostThreadMessage(threadId, 0x12, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2)); throw new InvalidOperationException("导航输入线程启动超时。");
        }
        if (startupError is not null) throw new InvalidOperationException("导航输入不可用。", startupError);
    }
    public GlobalNavigationState State => Volatile.Read(ref state);
    public string OverviewMessage => Volatile.Read(ref overviewMessage);
    public long BeginOverview()
    {
        var generation = Interlocked.Increment(ref overviewSequence);
        Post(() => BeginOverviewCore(generation)); return generation;
    }
    private void BeginOverviewCore(long generation)
    {
        if (overviewInput.Begin(generation, WindowEventListener.ForegroundWindow)) Leave("总览中，导航已暂停。");
    }
    public void EndOverview(long generation) => Post(() =>
    {
        if (overviewInput.Close(generation)) Leave("总览已关闭；可再次切换导航，Esc 退出。");
    });
    public void SetAvailable(bool value) => Post(() =>
    {
        available = value;
        if (!value && (overviewInput.Close(overviewInput.Generation) || gate.Active)) Leave("画布未运行，导航已退出。");
    });
    public void Toggle() => Post(ToggleCore);
    public void Exit() => Post(() => { automatic.Suspend(); Leave("已退出导航；进入应用窗口后可再次自动导航。"); });
    public void Configure(NavigationInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Post(() => { Leave("手势设置已更新，导航已退出。"); gate.Options = options; });
    }
    public void ChangeHotkey(NavigationHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (hotkey.Key is < 0x20 or > 0xFE || hotkey.Key == 0x7B || hotkey.Modifiers is 0 or > 7)
            throw new ArgumentException("请选择包含 Ctrl/Alt/Shift 的非保留快捷键。", nameof(hotkey));
        Post(() => RegisterToggle(hotkey));
    }
    public IReadOnlyList<TimedNavigationInput> Drain()
    {
        Volatile.Write(ref heartbeat, Environment.TickCount64); Interlocked.Exchange(ref wakePending, 0);
        var result = new List<TimedNavigationInput>();
        if (Interlocked.Exchange(ref overflow, 0) != 0)
        {
            while (inputs.TryDequeue(out _)) { }
            return [new(new(NavigationInputKind.Fault, PointD.Zero), DateTimeOffset.UtcNow)];
        }
        for (var count = 0; count < 256 && inputs.TryDequeue(out var input); count++) result.Add(input);
        return result;
    }
    public void Heartbeat() => Volatile.Write(ref heartbeat, Environment.TickCount64);

    private void Post(Action command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!thread.IsAlive) return;
        commands.Enqueue(command);
        if (!PostThreadMessage(threadId, CommandMessage, 0, 0))
            Publish(false, false, State.Hotkey, "导航输入线程不可用，请重新启动程序。");
    }
    private void ThreadMain()
    {
        try
        {
            PeekMessage(out _, 0, 0, 0, 0); threadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法安装导航鼠标钩子。");
            keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            if (keyboardHook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法安装桌面总览键盘钩子。");
            timer = SetTimer(0, 0, 100, 0);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启动输入看门狗。");
            RegisterToggle(NavigationHotkey.Default);
            overviewRegistered = RegisterHotKey(0, OverviewId, 3 | 0x4000, 0x4F);
            Volatile.Write(ref overviewMessage, overviewRegistered ? "桌面空白处 Tab / Shift+Tab 进入总览，总览内再次按下返回；Ctrl+Alt+O 也可打开/关闭。"
                : $"总览快捷键 Ctrl+Alt+O 注册失败（{Marshal.GetLastPInvokeError()}），可用界面按钮。");
            Wake(); ready.Set();
            while (Volatile.Read(ref stopping) == 0)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (message.MessageId == CommandMessage) while (commands.TryDequeue(out var command)) command();
                else if (message.MessageId == 0x312 && message.WParam == ToggleId) ToggleCore();
                else if (message.MessageId == 0x312 && message.WParam == EscapeId) { automatic.Suspend(); Leave("Esc 已退出导航；应用恢复正常鼠标交互。"); }
                else if (message.MessageId == 0x312 && message.WParam == HomeId && gate.Active) Queue(new(NavigationInputKind.Home, PointD.Zero));
                else if (message.MessageId == 0x312 && message.WParam == BackId && gate.Active) Queue(new(NavigationInputKind.Back, PointD.Zero));
                else if (message.MessageId == 0x312 && message.WParam == OverviewId)
                {
                    if (available || overviewOpen) { Leave("切换空间总览，导航已退出。"); Queue(new(NavigationInputKind.Overview, PointD.Zero)); }
                }
                else if (message.MessageId == 0x113)
                {
                    if (gate.Active && Environment.TickCount64 - Volatile.Read(ref heartbeat) > 1800)
                    {
                        available = false; Leave("界面响应超时，导航已自动退出。");
                        Queue(new(NavigationInputKind.Fault, PointD.Zero));
                    }
                    else if (GetCursorPos(out var pointer)) UpdateAutomatic(pointer);
                }
            }
        }
        catch (Exception exception)
        {
            if (!ready.IsSet) startupError = exception;
            gate.Exit(); Publish(false, false, State.Hotkey, $"导航输入停止：{exception.Message}");
            Queue(new(NavigationInputKind.Fault, PointD.Zero));
        }
        finally
        {
            gate.Exit();
            UnregisterHotKey(0, ToggleId); UnregisterHotKey(0, OverviewId); ReleaseModeKeys();
            if (hook != 0) UnhookWindowsHookEx(hook);
            if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
            if (timer != 0) KillTimer(0, timer);
            ready.Set(); GC.KeepAlive(callback); GC.KeepAlive(keyboardCallback);
        }
    }
    private void RegisterToggle(NavigationHotkey hotkey)
    {
        Leave("快捷键已更改，导航已退出。");
        UnregisterHotKey(0, ToggleId);
        var registered = RegisterHotKey(0, ToggleId, hotkey.Modifiers | 0x4000, hotkey.Key);
        Publish(false, registered, hotkey, registered ? $"{hotkey.Label} 切换导航；Esc 退出。"
            : $"快捷键 {hotkey.Label} 注册失败（{Marshal.GetLastPInvokeError()}），请换一个组合。");
    }
    private void ToggleCore()
    {
        if (gate.Active) { automatic.Suspend(); Leave("已退出导航；应用恢复正常鼠标交互。"); return; }
        if (overviewOpen) { Publish(false, State.Registered, State.Hotkey, "请先关闭总览，再进入导航。"); return; }
        if (!State.Registered) return;
        if (!available) { Publish(false, State.Registered, State.Hotkey, "请先勾选窗口并开始画布管理。"); return; }
        var held = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
        if (held || gate.HasHeldButtons)
        { Publish(false, true, State.Hotkey, "请先松开鼠标左右键，再进入导航。"); return; }
        if (!RegisterHotKey(0, EscapeId, 0x4000, 0x1B) || !RegisterHotKey(0, HomeId, 0x4000, 0x24)
            || !RegisterHotKey(0, BackId, 0x4000, 0x08))
        {
            ReleaseModeKeys();
            Publish(false, true, State.Hotkey, "Esc/Home/Backspace 注册失败，未进入导航；请解决快捷键冲突。"); return;
        }
        escapeRegistered = true;
        manual = true;
        gate.TryEnter(false); Volatile.Write(ref heartbeat, Environment.TickCount64);
        var enabled = string.Join(" / ", new[] { gate.Options.VerticalWheel ? "滚轮纵移" : null,
            gate.Options.HorizontalWheel ? "Shift 横移" : null, gate.Options.TwoButtonDrag ? "双键拖动" : null }.Where(item => item is not null));
        Publish(true, true, State.Hotkey, $"导航中：{enabled}；Home 原点 / Backspace 上一视角 / Esc 退出。");
    }
    private void Leave(string message)
    {
        contextClick.Cancel(); contextTarget = 0; contextGeneration++;
        var active = gate.Active; gate.Exit(); manual = false;
        if (escapeRegistered) ReleaseModeKeys();
        if (active) Queue(new(NavigationInputKind.Exit, PointD.Zero));
        Publish(false, State.Registered, State.Hotkey, State.Registered ? message : State.Message);
    }
    private void ReleaseModeKeys()
    {
        UnregisterHotKey(0, EscapeId); UnregisterHotKey(0, HomeId); UnregisterHotKey(0, BackId); escapeRegistered = false;
    }
    private void Publish(bool active, bool registered, NavigationHotkey hotkey, string message)
    { Volatile.Write(ref state, new(active, registered, hotkey, message, manual)); Wake(); }
    private void Wake()
    {
        if (Interlocked.Exchange(ref wakePending, 1) != 0) return;
        try { wakeUi(); } catch (InvalidOperationException) { } // UI may already be closing.
    }
    private void Queue(NavigationInput input)
    {
        if (inputs.Count >= 256)
        {
            gate.Exit(); if (escapeRegistered) ReleaseModeKeys();
            Interlocked.Exchange(ref overflow, 1);
            Publish(false, State.Registered, State.Hotkey, "输入积压，导航已退出并请求暂停恢复。"); return;
        }
        inputs.Enqueue(new(input, DateTimeOffset.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp())); Wake();
    }
    private nint MouseCallback(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        try
        {
            if (message is 0x207 or 0x208 or 0x20B or 0x20C) { contextGeneration++; contextClick.Cancel(); }
            NavigationInputKind? kind = message switch
            {
                0x200 => NavigationInputKind.Move,
                0x201 => NavigationInputKind.LeftDown,
                0x202 => NavigationInputKind.LeftUp,
                0x204 => NavigationInputKind.RightDown,
                0x205 => NavigationInputKind.RightUp,
                0x20A => NavigationInputKind.Wheel,
                0x20E => NavigationInputKind.HorizontalWheel,
                _ => null
            };
            if (kind is { } inputKind)
            {
                var input = Marshal.PtrToStructure<MouseInput>(data);
                // Only our balanced replay bypasses routing; unrelated injected input follows normal policy.
                if ((input.Flags & 1) != 0 && input.Extra == ContextClickTag) return CallNextHookEx(0, code, message, data);
                if (inputKind != NavigationInputKind.Move) contextGeneration++;
                UpdateAutomatic(input.Point);
                var shift = inputKind == NavigationInputKind.Wheel && (GetAsyncKeyState(0x10) & 0x8000) != 0;
                var control = inputKind == NavigationInputKind.Wheel && Down(0x11);
                var route = control && gate.Active ? (Suppress: true, Queue: true) : gate.Route(inputKind, shift);
                automatic.ObserveButton(inputKind, route.Suppress);
                var desktop = IsDesktop(input.Point);
                if (inputKind == NavigationInputKind.RightDown) contextTarget = desktop ? WindowFromPoint(input.Point) : 0;
                if (contextClick.Accept(inputKind, new(input.Point.X, input.Point.Y), route.Suppress && gate.Active, desktop,
                    Math.Max(GetSystemMetrics(68), GetSystemMetrics(69))))
                {
                    var target = contextTarget; var point = input.Point; var generation = contextGeneration;
                    // Run after the physical release has left this hook. Never move the pointer or click an application.
                    Post(() => RestoreDesktopContextClick(target, point, generation));
                }
                if (route.Queue)
                {
                    Queue(new(control ? NavigationInputKind.CycleWindow : inputKind, new(input.Point.X, input.Point.Y), unchecked((short)(input.Data >> 16)), shift));
                }
                if (route.Suppress) return 1;
            }
        }
        catch (Exception)
        {
            gate.Exit(); if (escapeRegistered) ReleaseModeKeys();
            Interlocked.Exchange(ref overflow, 1);
            Publish(false, State.Registered, State.Hotkey, "输入回调异常，导航已退出并请求暂停恢复。");
        }
        return CallNextHookEx(0, code, message, data);
    }
    private void UpdateAutomatic(Point pointer)
    {
        if (manual) return;
        var enter = automatic.ShouldNavigate(IsDesktop(pointer) && !MenuActive(), available, overviewOpen, gate.HasHeldButtons, Down(1) || Down(2));
        if (enter && !gate.Active && gate.TryEnter(false))
        {
            Volatile.Write(ref heartbeat, Environment.TickCount64);
            Publish(true, State.Registered, State.Hotkey, "桌面导航 · 滚轮纵移 / Shift 横移 / 双键拖动 / Ctrl+滚轮轮选 / Tab 总览");
        }
        else if (!enter && gate.Active) Leave("应用交互 · 移至桌面空白处自动导航");
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool MenuActive()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) && (info.Flags & 0x1C) != 0;
    }
    private void RestoreDesktopContextClick(nint target, Point released, long generation)
    {
        if (generation != contextGeneration || !gate.Active || !available || overviewOpen || target == 0 || Down(1) || Down(2)
            || Environment.TickCount64 - Volatile.Read(ref heartbeat) > 1800
            || !GetCursorPos(out var pointer) || !IsDesktop(pointer) || WindowFromPoint(pointer) != target
            || Math.Abs(pointer.X - released.X) > 2 || Math.Abs(pointer.Y - released.Y) > 2) return;
        Leave("桌面右键菜单 · 关闭菜单后恢复自动导航");
        // A physical single right click was withheld to disambiguate the two-button gesture.
        // Reinsert exactly one balanced pair at the unchanged cursor; no key or pointer movement is synthesized.
        NativeInput[] pair = [new() { Mouse = new() { Flags = 0x08, Extra = ContextClickTag } },
            new() { Mouse = new() { Flags = 0x10, Extra = ContextClickTag } }];
        var sent = SendInput(2, pair, Marshal.SizeOf<NativeInput>());
        if (sent != 2)
        {
            // If Windows accepted only the down, make one bounded attempt to balance it.
            if (sent == 1) SendInput(1, [pair[1]], Marshal.SizeOf<NativeInput>());
            automatic.Suspend();
            Publish(false, State.Registered, State.Hotkey, "Windows 未接受桌面右击；自动导航已暂停，可直接再次右击。");
        }
    }
    private static bool IsDesktop(Point point)
    {
        var window = WindowFromPoint(point);
        if (window == 0) return false;
        var root = GetAncestor(window, 2);
        var name = new StringBuilder(128);
        if (GetClassName(root == 0 ? window : root, name, name.Capacity) == 0) return false;
        return name.ToString() is "Progman" or "WorkerW";
    }
    private nint KeyboardCallback(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        try
        {
            var key = unchecked((uint)Marshal.ReadInt32(data));
            var up = message is 0x101 or 0x105;
            contextGeneration++;
            if (!up && GetCursorPos(out var cursor)) UpdateAutomatic(cursor);
            if (key == 9)
            {
                if (up)
                {
                    tabHeld = false;
                    if (tabCaptured) { tabCaptured = false; return 1; }
                }
                else if (!tabHeld)
                {
                    tabHeld = true;
                    var host = overviewInput.Host;
                    var hostUnderPointer = host != 0 && GetCursorPos(out var overviewPointer)
                        && GetAncestor(WindowFromPoint(overviewPointer), 2) == host;
                    if (available && overviewInput.CapturesTab(WindowEventListener.ForegroundWindow,
                        Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C), MenuActive(),
                        hostUnderPointer, host == 0 || IsWindowEnabled(host)))
                    {
                        tabCaptured = true; Queue(new(NavigationInputKind.Overview, PointD.Zero)); return 1;
                    }
                    if (GetCursorPos(out var pointer) && DesktopNavigationPolicy.OpensOverview(IsDesktop(pointer) && !MenuActive(), available,
                        overviewOpen, Down(0x11), Down(0x12), Down(0x5B) || Down(0x5C)) && !gate.HasHeldButtons)
                    {
                        tabCaptured = true; BeginOverviewCore(Interlocked.Increment(ref overviewSequence));
                        Queue(new(NavigationInputKind.Overview, PointD.Zero)); return 1;
                    }
                }
                else if (tabCaptured) return 1;
            }
            if (up && capturedKeys.Remove(key)) return 1;
            if (!up && capturedKeys.Contains(key)) return 1;
            if (!up && !manual && gate.Active && !overviewOpen && !Down(0x11) && !Down(0x12) && !Down(0x5B) && !Down(0x5C))
            {
                var kind = key switch { 0x1B => NavigationInputKind.Exit, 0x24 => NavigationInputKind.Home, 0x08 => NavigationInputKind.Back, _ => (NavigationInputKind?)null };
                if (kind is { } action)
                {
                    capturedKeys.Add(key);
                    if (action == NavigationInputKind.Exit) { automatic.Suspend(); Leave("Esc 已暂停自动导航；进入应用后恢复。"); }
                    else Queue(new(action, PointD.Zero));
                    return 1;
                }
            }
        }
        catch (Exception) { available = false; Leave("键盘输入异常，已暂停导航。"); Queue(new(NavigationInputKind.Fault, PointD.Zero)); }
        return CallNextHookEx(0, code, message, data);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Interlocked.Exchange(ref stopping, 1);
        PostThreadMessage(threadId, 0x12, 0, 0);
        if (!thread.Join(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("导航输入线程未能退出。");
        ready.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal Point Point; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct SyntheticMouse { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint Type; internal SyntheticMouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { internal uint Size, Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { internal nint Window; internal uint MessageId; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Point; internal uint Private; }
    private delegate nint HookCallback(int code, nuint message, nint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int kind, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
}
