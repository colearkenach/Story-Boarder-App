# Renders feature-graphic.html to ../feature-graphic.png (1024x500, no transparency, as Google Play requires),
# using Microsoft Edge without a window. Run from anywhere: powershell -File store/graphics/source/render.ps1
$ErrorActionPreference = 'Stop'
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$html = Join-Path $PSScriptRoot 'feature-graphic.html'
$out = Join-Path (Split-Path $PSScriptRoot) 'feature-graphic.png'
$profile = Join-Path $env:TEMP 'storyboarder-render'
Start-Process -FilePath $edge -Wait -RedirectStandardError (Join-Path $env:TEMP 'storyboarder-render.log') -ArgumentList @(
    '--headless', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', '--window-size=1024,500',
    '--allow-file-access-from-files', "--user-data-dir=$profile", "--screenshot=$out", "file:///$($html -replace '\\','/')")
# Edge saves 32-bit PNGs; Play wants 24-bit for the feature graphic.
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($out)
$rgb = New-Object System.Drawing.Bitmap $img.Width, $img.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($rgb); $g.DrawImage($img, 0, 0, $img.Width, $img.Height); $g.Dispose(); $img.Dispose()
$rgb.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $rgb.Dispose()
Write-Output "Saved $out"
