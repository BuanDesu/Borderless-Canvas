using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed record NavigationHotkey(uint Modifiers, uint Key, string Label)
{
    public static readonly NavigationHotkey Default = new(3, 0x20, "Ctrl+Alt+Space");
}
public sealed record GlobalNavigationState(bool Active, bool Registered, NavigationHotkey Hotkey, string Message);
public readonly record struct TimedNavigationInput(NavigationInput Input, DateTimeOffset At);

/// <summary>Dedicated message-loop thread. Subscribers only post a UI wake; never move windows here.</summary>
public sealed class GlobalNavigationInput : IDisposable
{
    private const uint CommandMessage = 0x8001;
    private const int ToggleId = 0x4BF0, EscapeId = 0x4BF1, HomeId = 0x4BF2, BackId = 0x4BF3;
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly ConcurrentQueue<TimedNavigationInput> inputs = new();
    private readonly NavigationInputGate gate = new();
    private readonly Action wakeUi;
    private readonly HookCallback callback;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private GlobalNavigationState state = new(false, false, NavigationHotkey.Default, "正在注册快捷键。");
    private Exception? startupError;
    private uint threadId;
    private nint hook;
    private nuint timer;
    private int wakePending, overflow, stopping;
    private long heartbeat = Environment.TickCount64;
    private bool available, escapeRegistered, disposed;

    public GlobalNavigationInput(Action wakeUi)
    {
        this.wakeUi = wakeUi; callback = MouseCallback;
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
    public void SetAvailable(bool value) => Post(() => { available = value; if (!value && gate.Active) Leave("画布未运行，导航已退出。"); });
    public void Toggle() => Post(ToggleCore);
    public void Exit() => Post(() => Leave("已退出导航；应用恢复正常鼠标交互。"));
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
            timer = SetTimer(0, 0, 100, 0);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启动输入看门狗。");
            RegisterToggle(NavigationHotkey.Default); ready.Set();
            while (Volatile.Read(ref stopping) == 0)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (message.MessageId == CommandMessage) while (commands.TryDequeue(out var command)) command();
                else if (message.MessageId == 0x312 && message.WParam == ToggleId) ToggleCore();
                else if (message.MessageId == 0x312 && message.WParam == EscapeId) Leave("Esc 已退出导航；应用恢复正常鼠标交互。");
                else if (message.MessageId == 0x312 && message.WParam == HomeId && gate.Active) Queue(new(NavigationInputKind.Home, PointD.Zero));
                else if (message.MessageId == 0x312 && message.WParam == BackId && gate.Active) Queue(new(NavigationInputKind.Back, PointD.Zero));
                else if (message.MessageId == 0x113 && gate.Active && Environment.TickCount64 - Volatile.Read(ref heartbeat) > 1800)
                {
                    Leave("界面响应超时，导航已自动退出。");
                    Queue(new(NavigationInputKind.Fault, PointD.Zero));
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
            UnregisterHotKey(0, ToggleId); ReleaseModeKeys();
            if (hook != 0) UnhookWindowsHookEx(hook);
            if (timer != 0) KillTimer(0, timer);
            ready.Set(); GC.KeepAlive(callback);
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
        if (gate.Active) { Leave("已退出导航；应用恢复正常鼠标交互。"); return; }
        if (!available) { Publish(false, State.Registered, State.Hotkey, "请先勾选窗口并开始画布管理。"); return; }
        if (!State.Registered) return;
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
        gate.TryEnter(false); Volatile.Write(ref heartbeat, Environment.TickCount64);
        var enabled = string.Join(" / ", new[] { gate.Options.VerticalWheel ? "滚轮纵移" : null,
            gate.Options.HorizontalWheel ? "Shift 横移" : null, gate.Options.TwoButtonDrag ? "双键拖动" : null }.Where(item => item is not null));
        Publish(true, true, State.Hotkey, $"导航中：{enabled}；Home 原点 / Backspace 上一视角 / Esc 退出。");
    }
    private void Leave(string message)
    {
        var active = gate.Active; gate.Exit();
        if (escapeRegistered) ReleaseModeKeys();
        if (active) Queue(new(NavigationInputKind.Exit, PointD.Zero));
        Publish(false, State.Registered, State.Hotkey, message);
    }
    private void ReleaseModeKeys()
    {
        UnregisterHotKey(0, EscapeId); UnregisterHotKey(0, HomeId); UnregisterHotKey(0, BackId); escapeRegistered = false;
    }
    private void Publish(bool active, bool registered, NavigationHotkey hotkey, string message)
    { Volatile.Write(ref state, new(active, registered, hotkey, message)); Wake(); }
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
        inputs.Enqueue(new(input, DateTimeOffset.UtcNow)); Wake();
    }
    private nint MouseCallback(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        try
        {
            NavigationInputKind? kind = message switch
            {
                0x200 => NavigationInputKind.Move, 0x201 => NavigationInputKind.LeftDown, 0x202 => NavigationInputKind.LeftUp,
                0x204 => NavigationInputKind.RightDown, 0x205 => NavigationInputKind.RightUp,
                0x20A => NavigationInputKind.Wheel, 0x20E => NavigationInputKind.HorizontalWheel, _ => null
            };
            if (kind is { } inputKind)
            {
                var shift = inputKind == NavigationInputKind.Wheel && (GetAsyncKeyState(0x10) & 0x8000) != 0;
                var route = gate.Route(inputKind, shift);
                if (route.Queue)
                {
                    var input = Marshal.PtrToStructure<MouseInput>(data);
                    Queue(new(inputKind, new(input.Point.X, input.Point.Y), unchecked((short)(input.Data >> 16)), shift));
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
    [StructLayout(LayoutKind.Sequential)] private struct Message { internal nint Window; internal uint MessageId; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Point; internal uint Private; }
    private delegate nint HookCallback(int code, nuint message, nint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int kind, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
}
