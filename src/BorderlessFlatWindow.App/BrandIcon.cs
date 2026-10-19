namespace BorderlessFlatWindow.App;

internal static class BrandIcon
{
    internal static Icon Load(int size)
    {
        using var source = typeof(BrandIcon).Assembly.GetManifestResourceStream("BorderlessFlatWindow.App.Assets.Borderless.ico")
            ?? throw new InvalidOperationException("应用图标资源缺失。");
        using var icon = new Icon(source, new Size(size, size));
        return (Icon)icon.Clone();
    }
}
