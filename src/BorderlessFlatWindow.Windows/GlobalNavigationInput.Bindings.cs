using BorderlessFlatWindow.Application.Navigation;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.Windows;

public sealed partial class GlobalNavigationInput
{
    private const int TouchId = 0x4BF7;
    private NavigationKeyBindings bindings = NavigationKeyBindings.Default;
    private readonly HashSet<int> registeredCommands = [];
    private string bindingsMessage = "";
    public NavigationKeyBindings KeyBindings => Volatile.Read(ref bindings);
    public string BindingsMessage => Volatile.Read(ref bindingsMessage);
    public static string Describe(KeyChord chord)
    {
        var key = chord.Key switch { 0x08 => "Backspace", 0x24 => "Home", >= 0x70 and <= 0x7B => "F" + (chord.Key - 0x6F), _ => ((char)chord.Key).ToString() };
        return ((chord.Modifiers & 2) != 0 ? "Ctrl+" : "") + ((chord.Modifiers & 1) != 0 ? "Alt+" : "") + ((chord.Modifiers & 4) != 0 ? "Shift+" : "") + key;
    }
    private static IEnumerable<(int Id, KeyChord Chord, string Name)> Commands(NavigationKeyBindings value)
        => [(OverviewId, value.Overview, "总览"), (CrosshairId, value.Crosshair, "准星"), (TouchId, value.Touch, "触控"), (PinId, value.Pin, "便签")];
    private bool RegisterCommands(NavigationKeyBindings value)
    {
        var failures = new List<string>();
        foreach (var (id, chord, name) in Commands(value))
        {
            if (RegisterKey(id, chord.Modifiers | 0x4000, chord.Key)) registeredCommands.Add(id);
            else failures.Add($"{name} {Describe(chord)} 注册失败（{Marshal.GetLastPInvokeError()}）");
        }
        overviewRegistered = registeredCommands.Contains(OverviewId);
        bindingsMessage = failures.Count == 0 ? "快捷键已就绪。" : string.Join("；", failures) + "；可使用界面入口。";
        return failures.Count == 0;
    }
    private void ReleaseCommands()
    { foreach (var id in registeredCommands) ReleaseKey(id); registeredCommands.Clear(); overviewRegistered = false; }
    public bool TryChangeBindings(NavigationKeyBindings value, out string message)
    {
        value.Validate();
        var completion = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            var previous = bindings; Leave("快捷键设置已更新。"); ReleaseCommands();
            var accepted = RegisterCommands(value);
            var failure = bindingsMessage;
            if (accepted) Volatile.Write(ref bindings, value);
            else { ReleaseCommands(); var restored = RegisterCommands(previous); bindingsMessage = failure + (restored ? " 已保留原快捷键。" : " 原键位也出现冲突，请使用界面入口。"); }
            var chord = bindings.Overview;
            Publish(false, overviewRegistered, new(chord.Modifiers, chord.Key, Describe(chord)), bindingsMessage);
            UpdateOverviewMessage(); completion.SetResult((accepted, bindingsMessage));
        });
        var result = completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); message = result.Item2; return result.Item1;
    }
    private void UpdateOverviewMessage() => Volatile.Write(ref overviewMessage,
        $"桌面 Tab / Shift+Tab 总览；{Describe(bindings.Overview)} 总览 · {Describe(bindings.Crosshair)} 准星 · {Describe(bindings.Touch)} 触控。 " + bindingsMessage);
    // Retained for native registration fixtures; changes the overview chord only.
    public void ChangeHotkey(NavigationHotkey hotkey)
    {
        var next = bindings with { Overview = new(hotkey.Modifiers, hotkey.Key) }; next.Validate();
        Post(() =>
        {
            if (registeredCommands.Remove(OverviewId)) ReleaseKey(OverviewId);
            overviewRegistered = RegisterKey(OverviewId, hotkey.Modifiers | 0x4000, hotkey.Key);
            if (overviewRegistered) registeredCommands.Add(OverviewId);
            Volatile.Write(ref bindings, next);
            Publish(false, overviewRegistered, hotkey, overviewRegistered ? "总览快捷键已就绪。" : "总览快捷键注册失败，请换一个组合。"); UpdateOverviewMessage();
        });
    }
}
