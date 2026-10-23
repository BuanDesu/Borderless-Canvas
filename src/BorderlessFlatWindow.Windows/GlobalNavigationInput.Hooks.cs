using System.ComponentModel;
using System.Runtime.InteropServices;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private readonly InputHookLiveness hookLiveness = new();
    private int hookRevision;
    private string hookHealth = "输入接管尚未启用。";
    public string HookHealth => Volatile.Read(ref hookHealth);
    internal int HookRevision => Volatile.Read(ref hookRevision);

    private void InstallInputHooks()
    {
        if (!nativeInput) return;
        var mouse = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (mouse == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法安装导航鼠标钩子。");
        var keyboard = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
        if (keyboard == 0)
        { var error = Marshal.GetLastPInvokeError(); UnhookWindowsHookEx(mouse); throw new Win32Exception(error, "无法安装桌面总览键盘钩子。"); }
        // Replace a pair together; keep the old pair if preparing either new hook fails.
        var oldMouse = hook; var oldKeyboard = keyboardHook; hook = mouse; keyboardHook = keyboard;
        if (oldMouse != 0) UnhookWindowsHookEx(oldMouse);
        if (oldKeyboard != 0) UnhookWindowsHookEx(oldKeyboard);
        hookLiveness.Callback(InputTime);
        Interlocked.Increment(ref hookRevision);
        Volatile.Write(ref hookHealth, "键盘与鼠标输入接管就绪。");
    }
    private bool ReestablishInputHooks()
    {
        try { InstallInputHooks(); return true; }
        catch (Win32Exception error)
        {
            available = false;
            Volatile.Write(ref hookHealth, $"输入接管失败（{error.NativeErrorCode}），请暂停后重新开始。");
            Leave(HookHealth); Queue(new(NavigationInputKind.Fault, PointD.Zero)); return false;
        }
    }
    private bool ObserveRawHookHealth()
    {
        if (!hookLiveness.ObserveRawMovement(InputTime, nativeInput && available && !overviewOpen && !MenuActive()
            && !Down(1) && !Down(2) && !Down(4) && !Down(5) && !Down(6))) return true;
        // Windows can silently remove a timed-out low-level hook. Continued Raw
        // Input without mouse callbacks is evidence to rebuild both hooks once,
        // without periodic reordering or injecting probe keys/clicks into apps.
        Leave("检测到输入接管中断，已退出当前手势并重新连接。");
        gate.Route(NavigationInputKind.LeftUp); gate.Route(NavigationInputKind.RightUp);
        automatic.ObserveButton(NavigationInputKind.LeftUp, false);
        automatic.ObserveButton(NavigationInputKind.RightUp, false);
        capturedKeys.RemoveWhere(key => !Down((int)key));
        if (!Down(9)) tabHeld = tabCaptured = false;
        if (!Down(0x0D)) pinMenuEnterHeld = false;
        if (pinMenuDown is { } pressed) { PostMessage(pressed.Identity.Handle, 0x1F, 0, 0); pinMenuDown = null; }
        if (ReestablishInputHooks()) Volatile.Write(ref hookHealth, "检测到输入中断，键盘与鼠标接管已重新连接。");
        return false;
    }
    internal void ReestablishInactiveHooksForCheck()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                if (!nativeInput || available || gate.Active) throw new InvalidOperationException("Native hook checks require inactive capture.");
                InstallInputHooks(); done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        });
        done.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
}
