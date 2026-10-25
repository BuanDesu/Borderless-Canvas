using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed record NavigationHotkey(uint Modifiers, uint Key, string Label)
{
    public static readonly NavigationHotkey Default = new(3, 0x4F, "Ctrl+Alt+O");
}
public enum NavigationMode { Inactive, Desktop, Crosshair, Touch }
public sealed record GlobalNavigationState(bool Active, bool Registered, NavigationHotkey Hotkey, string Message, NavigationMode Mode = NavigationMode.Inactive, PointD? CrosshairCenter = null, bool Available = false)
{
    public bool Crosshair => Mode == NavigationMode.Crosshair;
    public bool Touch => Mode == NavigationMode.Touch;
}
public readonly record struct TimedNavigationInput(NavigationInput Input, DateTimeOffset At, long CapturedTimestamp = 0);

/// <summary>Dedicated message-loop thread. Subscribers only post a UI wake; never move windows here.</summary>
public sealed partial class GlobalNavigationInput : IDisposable
{
    private const uint CommandMessage = 0x8001;
    private const int EscapeId = 0x4BF1, OverviewId = 0x4BF4;
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly ConcurrentQueue<TimedNavigationInput> inputs = new();
    private readonly NavigationInputGate gate = new();
    private readonly WindowCycleWheel cycleWheel = new();
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
    private NavigationMode mode;
    private bool crosshair => mode == NavigationMode.Crosshair;
    private bool tabHeld, tabCaptured;
    private readonly HashSet<uint> capturedKeys = [];
    private nuint timer;
    private int wakePending, wakeDirty, wakeAwaitingDrain, overflow, stopping;
    private long lastWakePost;
    private long heartbeat = Environment.TickCount64;
    private bool available, escapeRegistered, disposed;
    private string lastExitReason = "尚未退出导航。";
    public string LastExitReason => Volatile.Read(ref lastExitReason);
    private readonly BorderlessFlatWindow.Core.Canvas.OverviewInputSession overviewInput = new();
    private long overviewSequence;
    private bool overviewOpen => overviewInput.IsOpen;
    public void SetOverviewHost(long generation, nint handle)
        => Post(() => overviewInput.AttachHost(generation, handle, ForegroundWindow));
    private bool overviewRegistered;
    private string overviewMessage = "正在注册总览快捷键。";

    public GlobalNavigationInput(Action wakeUi, NavigationInputOptions? options = null, NavigationHotkey? hotkey = null, NavigationKeyBindings? keyBindings = null)
        : this(wakeUi, options, hotkey, new WindowsNavigationPlatform(), true, keyBindings) { }
    internal GlobalNavigationInput(Action wakeUi, NavigationInputOptions? options, NavigationHotkey? hotkey, INavigationInputPlatform platform)
        : this(wakeUi, options, hotkey, platform, false) { }
    private GlobalNavigationInput(Action wakeUi, NavigationInputOptions? options, NavigationHotkey? hotkey, INavigationInputPlatform platform, bool nativeInput, NavigationKeyBindings? keyBindings = null)
    {
        this.platform = platform; this.nativeInput = nativeInput; heartbeat = InputTime;
        bindings = keyBindings ?? NavigationKeyBindings.Default;
        if (hotkey is not null) bindings = bindings with { Overview = new(hotkey.Modifiers, hotkey.Key) };
        bindings.Validate();
        gate.Options = options ?? new();
        state = new(false, false, hotkey ?? new(bindings.Overview.Modifiers, bindings.Overview.Key, Describe(bindings.Overview)), "正在注册快捷键。");
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
    public bool HasPendingUiNotification => Volatile.Read(ref wakeDirty) != 0;
    // A delivered callback is not the same as a consumed queue. If startup stops
    // its UI timer before Drain, the watchdog may post one replacement callback.
    public void AcknowledgeUiWake() => Interlocked.Exchange(ref wakePending, 0);
    public string OverviewMessage => Volatile.Read(ref overviewMessage);
    public long BeginOverview()
    {
        var generation = Interlocked.Increment(ref overviewSequence);
        Post(() => BeginOverviewCore(generation)); return generation;
    }
    private void BeginOverviewCore(long generation)
    {
        if (overviewInput.Begin(generation, ForegroundWindow)) Leave("总览中，导航已暂停。");
    }
    public void EndOverview(long generation) => Post(() =>
    {
        if (overviewInput.Close(generation)) Leave("总览已关闭；可再次切换导航，Esc 退出。");
    });
    public void SetAvailable(bool value) => Post(() =>
    {
        var changed = available != value;
        available = value;
        if (!value) { pendingCrosshair = false; edgeMotion.Reset(); }
        if (value && changed)
        {
            automatic.Resume();
            if (!ReestablishInputHooks()) return;
        }
        if (!value && (overviewInput.Close(overviewInput.Generation) || gate.Active || touchEnabled)) Leave("画布未运行，导航已退出。");
        else if (changed) Publish(gate.Active, State.Registered, State.Hotkey, value ? "画布输入已就绪；移至桌面空白处自动导航。" : "画布输入已暂停。");
    });
    public void Exit() => Post(() => { automatic.Suspend(); Leave("已退出导航；进入应用窗口后可再次自动导航。"); });
    public void Configure(NavigationInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Post(() => { Leave("手势设置已更新，导航已退出。"); gate.Options = options; });
    }
    public IReadOnlyList<TimedNavigationInput> Drain()
    {
        Volatile.Write(ref heartbeat, InputTime); Interlocked.Exchange(ref wakeDirty, 0);
        Interlocked.Exchange(ref wakeAwaitingDrain, 0); Interlocked.Exchange(ref wakePending, 0);
        var result = new List<TimedNavigationInput>();
        if (Interlocked.Exchange(ref overflow, 0) != 0)
        {
            while (inputs.TryDequeue(out _)) { }
            return [new(new(NavigationInputKind.Fault, PointD.Zero), DateTimeOffset.UtcNow)];
        }
        for (var count = 0; count < 256 && inputs.TryDequeue(out var input); count++) result.Add(input);
        return result;
    }
    public void Heartbeat() => Volatile.Write(ref heartbeat, InputTime);

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
            InstallInputHooks();
            timer = SetTimer(0, 0, 100, 0);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启动输入看门狗。");
            RegisterCommands(bindings);
            Publish(false, overviewRegistered, State.Hotkey, bindingsMessage);
            InitializePointerInput();
            UpdateOverviewMessage();
            Wake(); ready.Set();
            while (Volatile.Read(ref stopping) == 0)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (message.MessageId == CommandMessage) while (commands.TryDequeue(out var command)) command();
                else if (message.MessageId == 0x312 && message.WParam == EscapeId) { automatic.Suspend(); Leave("Esc 已退出导航；应用恢复正常鼠标交互。"); }
                else if (message.MessageId == 0x312 && message.WParam == OverviewId)
                {
                    if (available || overviewOpen) { Leave("切换空间总览，导航已退出。"); Queue(new(NavigationInputKind.Overview, PointD.Zero)); }
                }
                else if (message.MessageId == 0x312 && message.WParam == CrosshairId) ToggleCrosshairCore();
                else if (message.MessageId == 0x312 && message.WParam == PinId) QueueFocusedPin();
                else if (message.MessageId == 0x312 && message.WParam == TouchId && available && !overviewOpen) Queue(new(NavigationInputKind.ToggleTouch, PointD.Zero));
                else if (message.MessageId == 0xFF && message.Window == rawPointer?.Handle)
                { ProcessRelativePointer(message.LParam); DispatchMessage(ref message); }
                else if (message.MessageId == 0x113)
                {
                    RetryInputWake();
                    RefreshTouchReceivers();
                    TryPendingCrosshair();
                    CheckRelativeSource();
                    if (!crosshair && clipOwned) ReleasePointerCapture();
                    if ((gate.Active || touchEnabled) && InputTime - Volatile.Read(ref heartbeat) > 1800)
                    {
                        available = false; Leave("界面响应超时，导航已自动退出。");
                        Queue(new(NavigationInputKind.Fault, PointD.Zero));
                    }
                    else if (GetPointer(out var pointer)) UpdateAutomatic(pointer);
                }
                else if (message.Window != 0) DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            if (!ready.IsSet) startupError = exception;
            available = false; Leave($"导航输入停止：{exception.Message}");
            Publish(false, false, State.Hotkey, $"导航输入停止：{exception.Message}");
            Queue(new(NavigationInputKind.Fault, PointD.Zero));
        }
        finally
        {
            DisposeTouchReceivers();
            ReleasePointerCapture(); rawPointer?.Dispose(); rawPointer = null;
            ReleaseCommands();
            gate.Exit();
            ReleaseModeKeys();
            if (hook != 0) UnhookWindowsHookEx(hook);
            if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
            if (timer != 0) KillTimer(0, timer);
            ready.Set(); GC.KeepAlive(callback); GC.KeepAlive(keyboardCallback);
        }
    }
    private void Leave(string message)
    {
        DisposeTouchReceivers();
        edgeMotion.Reset();
        cycleWheel.Reset();
        pendingCrosshair = false;
        if (gate.Active || crosshair || clipOwned) Volatile.Write(ref lastExitReason, message);
        ReleasePointerCapture();
        contextClick.Cancel(); contextTarget = 0; contextGeneration++;
        var active = gate.Active || touchEnabled; touchEnabled = false; gate.Exit(); mode = NavigationMode.Inactive;
        if (escapeRegistered) ReleaseModeKeys();
        if (active) Queue(new(NavigationInputKind.Exit, PointD.Zero));
        Publish(false, State.Registered, State.Hotkey, State.Registered ? message : State.Message);
    }
    private void ReleaseModeKeys()
    {
        ReleaseKey(EscapeId); escapeRegistered = false;
    }
    private void Publish(bool active, bool registered, NavigationHotkey hotkey, string message)
    { Volatile.Write(ref state, new(active, registered, hotkey, message, mode, crosshair ? new PointD(pointerCenter.X, pointerCenter.Y) : null, available)); Wake(); }
    private void Wake()
    {
        Volatile.Write(ref wakeDirty, 1); RetryInputWake();
    }
    private void RetryInputWake()
    {
        if (Volatile.Read(ref wakeDirty) == 0) return;
        var now = InputTime; var previous = Volatile.Read(ref lastWakePost);
        // Keep raw packets merged until Drain. A delivered but unconsumed wake
        // may retry at watchdog cadence, never once per high-rate mouse packet.
        if (Volatile.Read(ref wakeAwaitingDrain) != 0 && now >= previous && now - previous < 100) return;
        if (Interlocked.Exchange(ref wakePending, 1) != 0) return;
        Volatile.Write(ref lastWakePost, now); Volatile.Write(ref wakeAwaitingDrain, 1);
        try { wakeUi(); }
        catch (InvalidOperationException)
        { Interlocked.Exchange(ref wakeAwaitingDrain, 0); Interlocked.Exchange(ref wakePending, 0); } // Failed delivery cannot acknowledge future input.
    }
    private void Queue(NavigationInput input)
    {
        if (inputs.Count >= 256)
        {
            ReleasePointerCapture(); mode = NavigationMode.Inactive;
            gate.Exit(); if (escapeRegistered) ReleaseModeKeys();
            Interlocked.Exchange(ref overflow, 1);
            Publish(false, State.Registered, State.Hotkey, "输入积压，导航已退出并请求暂停恢复。"); return;
        }
        inputs.Enqueue(new(input, DateTimeOffset.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp())); Wake();
    }
    private nint MouseCallback(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        hookLiveness.Callback(InputTime);
        try
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            if (RouteMouse(message, input)) return 1;
        }
        catch (Exception)
        {
            ReleasePointerCapture(); mode = NavigationMode.Inactive;
            gate.Exit(); if (escapeRegistered) ReleaseModeKeys();
            Interlocked.Exchange(ref overflow, 1);
            Publish(false, State.Registered, State.Hotkey, "输入回调异常，导航已退出并请求暂停恢复。");
        }
        return CallNextHookEx(0, code, message, data);
    }
    private bool RouteMouse(nuint message, MouseInput input)
    {
        if (touchEnabled && message is 0x20A or 0x20E) touchHookSeen++;
        if (ConsumePointerAuxClick(message, input)) return true;
        if (touchEnabled && message is 0x207 or 0x208 or 0x20B or 0x20C) { TouchPointerPassThrough(); return false; }
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
            // Our context replay and system-menu command keep their ownership
            // even while ordinary touch clicks pass directly to applications.
            if ((input.Flags & 1) != 0 && input.Extra == ContextClickTag) return false;
            if (ConsumePinMenuClick(inputKind, input.Point)) return true;
            if (ConsumeCrosshairButton(inputKind)) return true;
            if (touchEnabled)
            {
                TouchButtonPassThrough(inputKind);
                if (inputKind == NavigationInputKind.Move) TouchPointerMoved(input.Point);
                if (inputKind is NavigationInputKind.LeftUp or NavigationInputKind.RightUp && gate.Route(inputKind).Suppress) return true;
                return RouteTouchWheel(inputKind, input);
            }
            if (!crosshair && inputKind == NavigationInputKind.Move)
                edgeMotion.ConstrainToPointer(new(input.Point.X, input.Point.Y), pointerScreens, gate.Options);
            if (inputKind is NavigationInputKind.LeftDown or NavigationInputKind.RightDown) edgeMotion.Reset();
            if (inputKind == NavigationInputKind.Move && ConsumeCrosshairMove(input.Point)) return true;
            if (inputKind != NavigationInputKind.Move) contextGeneration++;
            // A low-level callback can precede or follow the async button state.
            // Do not mistake this very down for a drag that began in another app.
            var physicalHeld = Down(1) && inputKind != NavigationInputKind.LeftDown || Down(2) && inputKind != NavigationInputKind.RightDown;
            UpdateAutomatic(input.Point, physicalHeld);
            var shift = inputKind == NavigationInputKind.Wheel && Down(0x10);
            var control = inputKind == NavigationInputKind.Wheel && Down(0x11);
            var route = control && gate.Active ? (Suppress: true, Queue: true) : gate.Route(inputKind, shift);
            if (inputKind is NavigationInputKind.Wheel or NavigationInputKind.HorizontalWheel && !control) cycleWheel.Reset();
            automatic.ObserveButton(inputKind, route.Suppress);
            var desktop = IsDesktop(input.Point);
            if (inputKind == NavigationInputKind.RightDown) contextTarget = desktop && nativeInput ? WindowFromPoint(input.Point) : 0;
            if (contextClick.Accept(inputKind, new(input.Point.X, input.Point.Y), route.Suppress && gate.Active, desktop,
                Math.Max(GetSystemMetrics(68), GetSystemMetrics(69))))
            {
                var target = contextTarget; var point = input.Point; var generation = contextGeneration;
                // Run after the physical release has left this hook. Never move the pointer or click an application.
                Post(() => RestoreDesktopContextClick(target, point, generation));
            }
            if (route.Queue)
            {
                var delta = (int)unchecked((short)(input.Data >> 16));
                if (control)
                {
                    delta = cycleWheel.Add(delta, InputTime) * 120;
                    if (delta == 0) return route.Suppress;
                }
                Queue(new(control ? NavigationInputKind.CycleWindow : inputKind, new(input.Point.X, input.Point.Y), delta, shift));
            }
            if (route.Suppress) return true;
        }
        return false;
    }
    private void UpdateAutomatic(Point pointer, bool? physicalHeld = null)
    {
        if (touchEnabled || crosshair) return;
        var enter = automatic.ShouldNavigate(IsDesktop(pointer) && !MenuActive(), available, overviewOpen, gate.HasHeldButtons, physicalHeld ?? (Down(1) || Down(2)));
        if (enter && !gate.Active && gate.TryEnter(false))
        {
            mode = NavigationMode.Desktop;
            Volatile.Write(ref heartbeat, InputTime);
            Publish(true, State.Registered, State.Hotkey, "桌面导航 · 滚轮纵移 / Shift 横移 / 双键拖动 / Ctrl+滚轮轮选 / Tab 总览");
        }
        else if (!enter && gate.Active) Leave("应用交互 · 移至桌面空白处自动导航");
    }
    private static bool NativeMenuActive()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) && (info.Flags & 0x1C) != 0;
    }
    private void RestoreDesktopContextClick(nint target, Point released, long generation)
    {
        if (generation != contextGeneration || !gate.Active || !available || overviewOpen || target == 0 || Down(1) || Down(2)
            || InputTime - Volatile.Read(ref heartbeat) > 1800
            || !GetPointer(out var pointer) || !IsDesktop(pointer) || WindowFromPoint(pointer) != target
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
    private static bool NativeIsDesktop(Point point)
    {
        var window = WindowFromPoint(point);
        if (window == 0) return false;
        var root = GetAncestor(window, 2);
        return NativeIsDesktopRoot(root == 0 ? window : root);
    }
    private static bool NativeIsDesktopRoot(nint root)
    {
        var name = new StringBuilder(128);
        if (root == 0 || GetClassName(root, name, name.Capacity) == 0) return false;
        return name.ToString() is "Progman" or "WorkerW";
    }
    private nint KeyboardCallback(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        try
        {
            var key = unchecked((uint)Marshal.ReadInt32(data));
            if (RouteKeyboard(message, key)) return 1;
        }
        catch (Exception) { available = false; Leave("键盘输入异常，已暂停导航。"); Queue(new(NavigationInputKind.Fault, PointD.Zero)); }
        return CallNextHookEx(0, code, message, data);
    }
    private bool RouteKeyboard(nuint message, uint key)
    {
        var up = message is 0x101 or 0x105;
        if (!up && key is 0x11 or 0x12 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C) edgeMotion.Reset();
        if (up && key is 0x11 or 0xA2 or 0xA3) cycleWheel.Reset();
        if (ConsumePinMenuEnter(key, up)) return true;
        if (!up && (crosshair || touchEnabled) && key == 0x1B) { capturedKeys.Add(key); automatic.Suspend(); Leave(crosshair ? "已退出准星模式，鼠标恢复自由移动。" : "已退出触控导航，鼠标恢复应用交互。"); return true; }
        if (!up && crosshair && (key is 0x5B or 0x5C || key == 9 && Down(0x12))) Leave("已退出准星模式，鼠标恢复自由移动。");
        contextGeneration++;
        if (!up && GetPointer(out var cursor)) UpdateAutomatic(cursor);
        if (key == 9)
        {
            if (up)
            {
                tabHeld = false;
                if (tabCaptured) { tabCaptured = false; return true; }
            }
            else if (!tabHeld)
            {
                tabHeld = true;
                var host = overviewInput.Host;
                var hostUnderPointer = host != 0 && GetPointer(out var overviewPointer)
                    && GetAncestor(WindowFromPoint(overviewPointer), 2) == host;
                if (available && overviewInput.CapturesTab(ForegroundWindow,
                    Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C), MenuActive(),
                    hostUnderPointer, host == 0 || IsWindowEnabled(host)))
                {
                    tabCaptured = true; Queue(new(NavigationInputKind.Overview, PointD.Zero)); return true;
                }
                if (GetPointer(out var pointer) && DesktopNavigationPolicy.OpensOverview(IsDesktop(pointer) && !MenuActive(), available,
                    overviewOpen, Down(0x11), Down(0x12), Down(0x5B) || Down(0x5C)) && !gate.HasHeldButtons)
                {
                    tabCaptured = true; BeginOverviewCore(Interlocked.Increment(ref overviewSequence));
                    Queue(new(NavigationInputKind.Overview, PointD.Zero)); return true;
                }
            }
            else if (tabCaptured) return true;
        }
        if (up && capturedKeys.Remove(key)) return true;
        if (!up && capturedKeys.Contains(key)) return true;
        if (!up && gate.Active && !overviewOpen && !Down(0x5B) && !Down(0x5C))
        {
            var modifiers = (Down(0x11) ? 2u : 0) | (Down(0x12) ? 1u : 0) | (Down(0x10) ? 4u : 0);
            var kind = key == 0x1B && modifiers == 0 ? NavigationInputKind.Exit : bindings.Home.Matches(key, modifiers) ? NavigationInputKind.Home
                : bindings.Back.Matches(key, modifiers) ? NavigationInputKind.Back : (NavigationInputKind?)null;
            if (kind is { } action)
            {
                capturedKeys.Add(key);
                if (action == NavigationInputKind.Exit) { automatic.Suspend(); Leave("Esc 已暂停自动导航；进入应用后恢复。"); }
                else Queue(new(action, PointD.Zero));
                return true;
            }
        }
        return false;
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
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool NativeGetCursorPos(out Point point);
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
