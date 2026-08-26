param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\src\TwinQuota.Windows\Assets\TwinQuota.Source.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\TwinQuota.Windows\Assets')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Set-HighQualityRendering {
    param([System.Drawing.Graphics]$Graphics)

    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $Graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
}

function New-ResizedPngBytes {
    param(
        [System.Drawing.Image]$Image,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = [System.IO.MemoryStream]::new()

    try {
        Set-HighQualityRendering $graphics
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $destination = [System.Drawing.Rectangle]::new(0, 0, $Size, $Size)
        $graphics.DrawImage(
            $Image,
            $destination,
            0,
            0,
            $Image.Width,
            $Image.Height,
            [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$pngPath = Join-Path $resolvedOutput 'TwinQuota.png'
$icoPath = Join-Path $resolvedOutput 'TwinQuota.ico'

$source = [System.Drawing.Image]::FromFile($resolvedSource)
$largeCanvas = [System.Drawing.Bitmap]::new(
    2048,
    2048,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$largeGraphics = [System.Drawing.Graphics]::FromImage($largeCanvas)
$clipPath = New-RoundedRectanglePath ([System.Drawing.RectangleF]::new(70, 70, 1908, 1908)) 370

try {
    Set-HighQualityRendering $largeGraphics
    $largeGraphics.Clear([System.Drawing.Color]::Transparent)
    $largeGraphics.SetClip($clipPath)
    $largeGraphics.DrawImage(
        $source,
        [System.Drawing.Rectangle]::new(0, 0, 2048, 2048),
        0,
        0,
        $source.Width,
        $source.Height,
        [System.Drawing.GraphicsUnit]::Pixel)
    $largeGraphics.ResetClip()

    $finalPngBytes = New-ResizedPngBytes $largeCanvas 1024
    [System.IO.File]::WriteAllBytes($pngPath, $finalPngBytes)

    $finalImageStream = [System.IO.MemoryStream]::new($finalPngBytes, $false)
    $finalImage = [System.Drawing.Image]::FromStream($finalImageStream)
    try {
        $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
        $images = foreach ($size in $sizes) {
            [pscustomobject]@{
                Size = $size
                Data = New-ResizedPngBytes $finalImage $size
            }
        }

        $fileStream = [System.IO.File]::Create($icoPath)
        $writer = [System.IO.BinaryWriter]::new($fileStream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$images.Count)

            [uint32]$offset = 6 + (16 * $images.Count)
            foreach ($image in $images) {
                $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
                $writer.Write([byte]$dimension)
                $writer.Write([byte]$dimension)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$image.Data.Length)
                $writer.Write($offset)
                $offset += [uint32]$image.Data.Length
            }

            foreach ($image in $images) {
                $writer.Write([byte[]]$image.Data)
            }
        }
        finally {
            $writer.Dispose()
            $fileStream.Dispose()
        }
    }
    finally {
        $finalImage.Dispose()
        $finalImageStream.Dispose()
    }
}
finally {
    $clipPath.Dispose()
    $largeGraphics.Dispose()
    $largeCanvas.Dispose()
    $source.Dispose()
}

Write-Host "Created $pngPath"
Write-Host "Created $icoPath"
