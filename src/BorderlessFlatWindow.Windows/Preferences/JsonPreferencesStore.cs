using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BorderlessFlatWindow.Application.Navigation;
using BorderlessFlatWindow.Application.Preferences;

namespace BorderlessFlatWindow.Windows.Preferences;

/// <summary>Bounded, strict configuration with an optional exclusive writer and atomic replacement.</summary>
public sealed class JsonPreferencesStore : IPreferencesStore
{
    public const int MaxBytes = 64 * 1024;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BorderlessFlatWindow", "settings.json");
    private readonly FileStream? lease;
    private byte[]? signature;
    private bool loadedValid, disposed;
    public string FilePath { get; }
    public bool CanWrite => !disposed && lease is not null && loadedValid;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    public JsonPreferencesStore(string path)
    {
        FilePath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        try { lease = new(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public static void EnsureSeparateDataFiles(string settings, string recovery, string bookmarks)
    {
        var paths = new[] { settings, recovery, bookmarks }.Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("设置、恢复日志与工作区文件必须使用不同路径。");
    }
    private byte[]? Read()
    {
        FileStream stream;
        try { stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; }
        using (stream)
        {
            if (stream.Length > MaxBytes) throw new InvalidDataException("设置超过 64 KiB 读取限制。");
            using var buffer = new MemoryStream(); var block = new byte[4096]; int count;
            while ((count = stream.Read(block)) > 0)
            { if (buffer.Length + count > MaxBytes) throw new InvalidDataException("设置超过读取限制。"); buffer.Write(block, 0, count); }
            return buffer.ToArray();
        }
    }
    public UserPreferences? Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this); loadedValid = false;
        var bytes = Read(); signature = bytes is null ? null : SHA256.HashData(bytes);
        if (bytes is null) { loadedValid = true; return null; }
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("设置必须是一个对象。");
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw new InvalidDataException("设置包含重复字段。");
            var dto = JsonSerializer.Deserialize<Dto>(bytes, Options) ?? throw new InvalidDataException("设置为空。");
            if (dto.Version != 1 || dto.VerticalWheel is null || dto.HorizontalWheel is null || dto.TwoButtonDrag is null || dto.WheelDistance is null
                || dto.Shortcut is null || dto.AutomaticAdmission is null || dto.FollowWallpaper is null || dto.AnimateOverview is null)
                throw new InvalidDataException("设置版本或字段不完整。");
            var value = new UserPreferences(new NavigationInputOptions(dto.VerticalWheel.Value, dto.HorizontalWheel.Value, dto.TwoButtonDrag.Value, dto.WheelDistance.Value),
                (ManualNavigationShortcut)dto.Shortcut.Value, dto.AutomaticAdmission.Value, dto.FollowWallpaper.Value, dto.AnimateOverview.Value);
            value.Validate(); loadedValid = true; return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("设置文件无效；原文件已保留。", error); }
    }
    private void EnsureUnchanged()
    {
        if (!CanWrite) throw new IOException("设置只读或尚未成功读取。");
        var current = Read(); var hash = current is null ? null : SHA256.HashData(current);
        if (signature is null ? hash is not null : hash is null || !signature.SequenceEqual(hash))
            throw new IOException("设置已被外部修改，请重新读取或明确保存当前选择。");
    }
    public void Save(UserPreferences preferences)
    {
        ObjectDisposedException.ThrowIf(disposed, this); preferences.Validate(); EnsureUnchanged();
        var navigation = preferences.Navigation;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Dto(1, navigation.VerticalWheel, navigation.HorizontalWheel, navigation.TwoButtonDrag,
            navigation.WheelDistance, (int)preferences.Shortcut, preferences.AutomaticAdmission, preferences.FollowWallpaper, preferences.AnimateOverview), Options);
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            EnsureUnchanged(); File.Move(temporary, FilePath, overwrite: true); signature = SHA256.HashData(bytes);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() { if (disposed) return; disposed = true; lease?.Dispose(); }
    private sealed record Dto(int? Version, bool? VerticalWheel, bool? HorizontalWheel, bool? TwoButtonDrag, int? WheelDistance,
        int? Shortcut, bool? AutomaticAdmission, bool? FollowWallpaper, bool? AnimateOverview);
}
