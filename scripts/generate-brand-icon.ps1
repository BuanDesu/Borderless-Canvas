# Original multi-resolution artwork. Re-run when changing the application/tray mark.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/BorderlessFlatWindow.App/Assets/Borderless.ico'
New-Item -ItemType Directory -Path (Split-Path -Parent $iconPath) -Force | Out-Null
$images = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(2, 2, 24, 24, 180, 90); $shape.AddArc(38, 2, 24, 24, 270, 90)
    $shape.AddArc(38, 38, 24, 24, 0, 90); $shape.AddArc(2, 38, 24, 24, 90, 90); $shape.CloseFigure()
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0, 0, 64, 64), [Drawing.Color]::FromArgb(255, 37, 155), [Drawing.Color]::FromArgb(106, 39, 228), 55.0)
    $graphics.FillPath($gradient, $shape)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $cyan = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(72, 236, 255))
    $purple = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(78, 21, 151))
    $graphics.FillRectangle($purple, 11, 13, 43, 34)
    $graphics.FillRectangle($white, 10, 11, 43, 32)
    $graphics.FillRectangle($cyan, 15, 16, 33, 22)
    $graphics.FillRectangle($white, 27, 43, 10, 6)
    $graphics.FillRectangle($white, 20, 49, 24, 4)
    $graphics.FillRectangle($white, 19, 20, 5, 5)
    $graphics.FillRectangle($white, 24, 25, 5, 5)
    $graphics.FillRectangle($white, 29, 20, 5, 5)
    $buffer = [IO.MemoryStream]::new()
    $bitmap.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,$buffer.ToArray()
    $buffer.Dispose(); $white.Dispose(); $cyan.Dispose(); $purple.Dispose(); $gradient.Dispose(); $shape.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output 'Generated original Borderless icon (8 sizes).'
