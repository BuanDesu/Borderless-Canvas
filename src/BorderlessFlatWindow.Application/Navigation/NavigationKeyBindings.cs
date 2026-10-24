namespace BorderlessFlatWindow.Application.Navigation;

public sealed record KeyChord([property: System.Text.Json.Serialization.JsonRequired] uint Modifiers,
    [property: System.Text.Json.Serialization.JsonRequired] uint Key)
{
    public bool Matches(uint key, uint modifiers) => Key == key && Modifiers == modifiers;
}

/// <summary>Global commands require Ctrl+Alt; view keys are scoped to canvas navigation.</summary>
public sealed record NavigationKeyBindings
{
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Overview { get; init; } = new(3, 0x4F);
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Crosshair { get; init; } = new(3, 0x43);
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Touch { get; init; } = new(3, 0x56);
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Pin { get; init; } = new(3, 0x50);
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Home { get; init; } = new(0, 0x24);
    [System.Text.Json.Serialization.JsonRequired] public KeyChord Back { get; init; } = new(0, 0x08);
    public static NavigationKeyBindings Default { get; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<KeyChord> Global => [Overview, Crosshair, Touch, Pin];
    public void Validate()
    {
        var all = Global.Concat([Home, Back]).ToArray();
        if (all.Any(chord => chord is null)) throw new ArgumentException("快捷键不能为空。");
        foreach (var chord in all)
        {
            if (chord == Home && chord == new KeyChord(0, 0x24) || chord == Back && chord == new KeyChord(0, 0x08)) continue;
            // No Win, Alt-only, editing/navigation keys, F4, F12, Tab, Escape or Delete.
            // A conservative whitelist protects shell, accessibility, task switching and editing chords.
            if (chord.Modifiers is not (3 or 7) || !(chord.Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
                or >= 0x70 and <= 0x7A && chord.Key != 0x73))
                throw new ArgumentException("自定义组合须为 Ctrl+Alt（可加 Shift）与字母、数字或 F1–F11（不含 F4）；Home / Backspace 仅用于各自的画布导航操作。");
        }
        if (all.Distinct().Count() != all.Length) throw new ArgumentException("不同操作不能使用相同快捷键。");
    }
}
