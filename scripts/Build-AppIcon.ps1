# Generates the multi-resolution Windows icon using the geometry in app-icon.svg.
# No external packages, fonts, personal data or network access are required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot '..\CodexQuotaWidget\Assets'
$iconPath = Join-Path $assetDir 'app.ico'
$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $tile = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(12, 12, 116, 116, 180, 90)
    $tile.AddArc(128, 12, 116, 116, 270, 90)
    $tile.AddArc(128, 128, 116, 116, 0, 90)
    $tile.AddArc(12, 128, 116, 116, 90, 90)
    $tile.CloseFigure()
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#f3f7ff'))
    $glass = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#e5efff'))
    $back = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#8ebaff'))
    $front = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#4285ed'))
    $edge = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#2456a6'), 12)
    $tail = [System.Drawing.Pen]::new($edge.Color, 16)
    $tail.StartCap = $tail.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $circle = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $circle.AddEllipse(47, 47, 154, 154)
    $g.FillPath($paper, $tile)
    $g.FillPath($glass, $circle)
    $state = $g.Save()
    $g.SetClip($circle)
    foreach ($layer in @(0, 1)) {
        $wave = [System.Drawing.Drawing2D.GraphicsPath]::new()
        if ($layer -eq 0) {
            $wave.AddBezier(40,126,70,106,96,148,126,128)
            $wave.AddBezier(126,128,156,108,180,108,210,130)
        } else {
            $wave.AddBezier(40,146,72,125,101,161,130,143)
            $wave.AddBezier(130,143,159,125,179,129,210,149)
        }
        $wave.AddLine(210,210,40,210)
        $wave.CloseFigure()
        $g.FillPath($(if ($layer -eq 0) { $back } else { $front }), $wave)
        $wave.Dispose()
    }
    $g.Restore($state)
    $g.DrawPath($edge, $circle)
    $g.DrawLine($tail, 176,176,204,204)
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,($stream.ToArray())
    if ($size -eq 256) { $bmp.Save((Join-Path $assetDir 'app-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    foreach ($item in @($stream,$g,$bmp,$tile,$circle,$paper,$glass,$back,$front,$edge,$tail)) { $item.Dispose() }
}
$output = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $sizes = @(16,20,24,32,40,48,64,128,256)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated app.ico with $($frames.Count) resolutions."
