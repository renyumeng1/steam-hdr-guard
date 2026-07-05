$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SteamHdrGuardNativeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

$out = Join-Path $PSScriptRoot "app.ico"
$bitmap = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$handle = [IntPtr]::Zero
$icon = $null
$fileStream = $null

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 17, 17, 17))
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 12
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $font = New-Object System.Drawing.Font "Segoe UI", 48, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    try {
        $graphics.FillEllipse($background, 20, 20, 216, 216)
        $graphics.DrawEllipse($ring, 70, 70, 116, 116)
        $graphics.DrawString("HDR", $font, $textBrush, ([System.Drawing.RectangleF]::new(20, 20, 216, 216)), $format)
    }
    finally {
        $background.Dispose()
        $ring.Dispose()
        $textBrush.Dispose()
        $font.Dispose()
        $format.Dispose()
    }

    $handle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($handle).Clone()
    $fileStream = [System.IO.File]::Create($out)
    $icon.Save($fileStream)
}
finally {
    if ($fileStream -ne $null) { $fileStream.Dispose() }
    if ($icon -ne $null) { $icon.Dispose() }
    if ($handle -ne [IntPtr]::Zero) { [SteamHdrGuardNativeIcon]::DestroyIcon($handle) | Out-Null }
    $graphics.Dispose()
    $bitmap.Dispose()
}
