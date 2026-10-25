$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$frames = foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    try {
        $scale = $size / 32.0
        $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))
        $accent = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(23, 107, 91))
        $drawing.DrawRoundedRectangle($accent, $null, [System.Windows.Rect]::new(1, 1, 30, 30), 4, 4)
        foreach ($index in 0..2) {
            $top = 6 + $index * 8
            $drawing.DrawRoundedRectangle([System.Windows.Media.Brushes]::White, $null, [System.Windows.Rect]::new(5, $top, 22, 5), 1, 1)
            $drawing.DrawEllipse($accent, $null, [System.Windows.Point]::new(9, ($top + 2.5)), 1, 1)
        }
        $drawing.Pop()
    } finally {
        $drawing.Close()
    }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    try {
        $encoder.Save($stream)
        [PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() }
    } finally {
        $stream.Dispose()
    }
}

$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'MarcelinoClient\Assets\MarcelinoClient.ico'
[System.IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($path))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }
} finally {
    $writer.Dispose()
}
Write-Output $path