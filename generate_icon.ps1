Add-Type -AssemblyName System.Drawing

$width = 256
$height = 256
$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# background color (purple)
$bg = [System.Drawing.Color]::FromArgb(255, 95, 39, 205)
$g.Clear($bg)

# Draw white initials centered
$font = New-Object System.Drawing.Font -ArgumentList @("Segoe UI", 120, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$brushText = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)
$rectf = New-Object System.Drawing.RectangleF -ArgumentList 0,0,$width,$height
$g.DrawString("NB", $font, $brushText, $rectf, $format)

# Ensure Assets folder exists
$assets = Join-Path (Get-Location) "Assets"
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }
$out = Join-Path $assets "NeeshiIcon.ico"

# Save a temporary PNG (not required but useful)
$tmpPng = Join-Path $env:TEMP "neeshi_icon.png"
$bmp.Save($tmpPng, [System.Drawing.Imaging.ImageFormat]::Png)

# Convert bitmap to icon and save
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)

$fs = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()

# Clean up handles and objects
# Destroy icon handle
$signature = @"
using System;
using System.Runtime.InteropServices;
public class NativeMethods { [DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyIcon(IntPtr hIcon); }
"@
Add-Type -TypeDefinition $signature
[NativeMethods]::DestroyIcon($hIcon) | Out-Null
$icon.Dispose()
$g.Dispose()
$bmp.Dispose()

Write-Host "Icon generated at: $out"