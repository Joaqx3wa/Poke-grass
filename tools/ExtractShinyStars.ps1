$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'assets\reference\pokegrass_encounter_effects.png'
$outputPath = Join-Path $root 'assets\ui\effects\stars.png'
$previewPath = Join-Path $root 'assets\ui\effects\previews'
$source = [System.Drawing.Bitmap]::new($sourcePath)
try {
    # The original art's lower-right star cluster is isolated by source region.
    # Low-alpha sheet haze is removed with the same alpha cutoff as the approved sheets.
    $crop = $source.Clone([System.Drawing.Rectangle]::new(1120, 748, 416, 276), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $cleared = 0
        for ($y = 0; $y -lt $crop.Height; $y++) {
            for ($x = 0; $x -lt $crop.Width; $x++) {
                $pixel = $crop.GetPixel($x, $y)
                if ($pixel.A -lt 51) {
                    $crop.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
                    $cleared++
                }
            }
        }
        $crop.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

        foreach ($mode in @('light','dark','checker')) {
            $preview = [System.Drawing.Bitmap]::new($crop.Width, $crop.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
            $g = [System.Drawing.Graphics]::FromImage($preview)
            try {
                if ($mode -eq 'light') { $g.Clear([System.Drawing.Color]::White) }
                elseif ($mode -eq 'dark') { $g.Clear([System.Drawing.Color]::FromArgb(28,28,28)) }
                else {
                    $g.Clear([System.Drawing.Color]::White)
                    for ($y=0; $y -lt $crop.Height; $y+=16) { for ($x=0; $x -lt $crop.Width; $x+=16) { if ((($x/16)+($y/16)) % 2 -eq 0) { $g.FillRectangle([System.Drawing.Brushes]::LightGray,$x,$y,16,16) } } }
                }
                $g.DrawImageUnscaled($crop,0,0)
                $preview.Save((Join-Path $previewPath "stars-$mode.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $g.Dispose(); $preview.Dispose() }
        }
        Write-Host "Saved stars $($crop.Width)x$($crop.Height); cleared $cleared low-alpha pixels."
    } finally { $crop.Dispose() }
} finally { $source.Dispose() }
