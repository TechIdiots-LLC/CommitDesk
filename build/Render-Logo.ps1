# Renders the logo (CommitDesk/Resources/Logo/commitdesk-logo.svg) into the app's icon files. Run it after changing the
# SVG and commit the results:
#
#   ./build/Render-Logo.ps1
#
# Writes, all with a transparent background unless noted:
#   Resources/AppIcon/Desktop/appicon.png   Windows/macOS app icon (MauiIcon)
#   Resources/AppIcon/Desktop/CommitDesk.ico  exe, window, taskbar and installer icon (16-256 px)
#   Resources/AppIcon/appiconfg.png         Android/iOS icon foreground, over appicon.svg's navy
#   Resources/Images/logo.png               the logo on the welcome screen
#   Resources/Splash/splash.png             the splash screen
#
# Needs Windows with Microsoft Edge, which draws the SVG (headless, at 2x for clean downscaling).
param([string]$Root = (Join-Path $PSScriptRoot '..\CommitDesk\Resources'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Root = (Resolve-Path $Root).Path
$svg = Join-Path $Root 'Logo\commitdesk-logo.svg'

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found; it draws the SVG.' }

# Draw the SVG at 1024 px: a 512 px window at device scale 2, transparent background
$work = Join-Path ([IO.Path]::GetTempPath()) "commitdesk-logo-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
$master = Join-Path $work 'logo-1024.png'
$url = 'file:///' + ($svg -replace '\\', '/')
$edgeArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$work\profile",
    '--default-background-color=00000000', '--force-device-scale-factor=2', '--window-size=512,512',
    "--screenshot=$master", $url)
$proc = Start-Process $edge -ArgumentList $edgeArgs -PassThru -WindowStyle Hidden
# Edge writes the screenshot and then sometimes lingers; wait for the file rather than the process
for ($i = 0; $i -lt 60 -and -not (Test-Path $master); $i++) { Start-Sleep -Milliseconds 500 }
Start-Sleep -Milliseconds 500
if (-not $proc.HasExited) { $proc.Kill() }
if (-not (Test-Path $master)) { throw 'Edge did not render the SVG.' }

function New-Resized([System.Drawing.Image]$Source, [int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($Source, 0, 0, $Size, $Size)
    $g.Dispose()
    $bmp
}

function Save-Png([System.Drawing.Image]$Source, [int]$Size, [string]$Path) {
    $bmp = New-Resized $Source $Size
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $Path ($Size px)"
}

# A Windows .ico with PNG-compressed entries at the sizes Explorer and the taskbar use
function Save-Ico([System.Drawing.Image]$Source, [string]$Path, [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)) {
    $pngs = foreach ($s in $Sizes) {
        $bmp = New-Resized $Source $s
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $stream = [IO.MemoryStream]::new()
    $w = [IO.BinaryWriter]::new($stream)
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$Sizes.Count)          # ICONDIR
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {                                          # ICONDIRENTRY
        $dim = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Flush(); [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    Write-Host "wrote $Path (sizes $($Sizes -join ', '))"
}

$src = [System.Drawing.Image]::FromFile($master)
try {
    Save-Png $src 512 (Join-Path $Root 'AppIcon\Desktop\appicon.png')
    Save-Ico $src (Join-Path $Root 'AppIcon\Desktop\CommitDesk.ico')
    Save-Png $src 512 (Join-Path $Root 'AppIcon\appiconfg.png')
    Save-Png $src 512 (Join-Path $Root 'Images\logo.png')
    Save-Png $src 512 (Join-Path $Root 'Splash\splash.png')
}
finally {
    $src.Dispose()
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
