using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

/// <summary>Private GDI and GDI+ registration; never installs or modifies a system font.</summary>
internal sealed partial class PixelFont : IDisposable
{
    private readonly PrivateFontCollection collection = new();
    private readonly nint bytes, registration;
    private static FontFamily? family;
    internal PixelFont()
    {
        using var source = typeof(PixelFont).Assembly.GetManifestResourceStream("BorderlessFlatWindow.App.Assets.Fonts.fusion-pixel-12px-proportional-zh_hans.ttf")
            ?? throw new InvalidOperationException("像素字体资源缺失。");
        using var buffer = new MemoryStream(); source.CopyTo(buffer); var data = buffer.ToArray();
        bytes = Marshal.AllocCoTaskMem(data.Length); Marshal.Copy(data, 0, bytes, data.Length);
        uint count = 0; registration = AddFontMemResourceEx(bytes, (uint)data.Length, 0, ref count);
        if (registration == 0 || count == 0) { Marshal.FreeCoTaskMem(bytes); throw new InvalidOperationException("私有像素字体加载失败。"); }
        collection.AddMemoryFont(bytes, data.Length); family = collection.Families[0];
    }
    internal static Font Create(float points = 9) => new(family ?? FontFamily.GenericSansSerif, points, FontStyle.Regular, GraphicsUnit.Point);
    internal static string FamilyName => family?.Name ?? "";
    public void Dispose()
    {
        family = null; collection.Dispose(); RemoveFontMemResourceEx(registration); Marshal.FreeCoTaskMem(bytes);
    }
    [DllImport("gdi32.dll")] private static extern nint AddFontMemResourceEx(nint data, uint size, nint reserved, ref uint fonts);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveFontMemResourceEx(nint handle);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetGlyphIndicesW(nint dc, string text, int count, [Out] ushort[] glyphs, uint flags);
}
