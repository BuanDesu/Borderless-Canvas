using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BorderlessFlatWindow.App;

internal sealed partial class PixelFont
{
    internal static void VerifyGlyphs()
    {
        using var font = Create(); var hfont = font.ToHfont(); var dc = CreateCompatibleDC(0); var old = SelectObject(dc, hfont);
        try
        {
            const string text = "简体中文空间总览窗口画布原点工作区开始暂停恢复设备诊断ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var glyphs = new ushort[text.Length];
            if (GetGlyphIndicesW(dc, text, text.Length, glyphs, 1) == uint.MaxValue || glyphs.Contains(ushort.MaxValue))
                throw new InvalidOperationException("Private pixel font is missing Chinese or Latin glyphs.");
        }
        finally { SelectObject(dc, old); DeleteObject(hfont); DeleteDC(dc); }
    }
}
