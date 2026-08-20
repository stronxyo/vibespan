# Builds docs/mode.gif: the widget collapsing from a card into the screen-edge hairline.
#
# Both end states are REAL captures of the running widget against a black backdrop - the
# in-between frames are an interpolation between those two bitmaps, not a redraw. The app
# itself switches instantly; the morph exists so the two shapes read as the same gauge.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "$env:TEMP\vibespan-shots\low.json",
    [string]$OutGif  = "C:\PROJECTS\vibespan\docs\mode.gif",
    [int]$CanvasW = 460,
    [int]$CanvasH = 150
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class GW { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; } }
'@
} catch { }

$cfgDir = "$env:TEMP\vibespan-shots"
$frames = "$env:TEMP\vibespan-gif"
New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
if (Test-Path $frames) { Remove-Item -LiteralPath $frames -Recurse -Force }
New-Item -ItemType Directory -Path $frames -Force | Out-Null

# A quiet reading, so the bar shows a fill and a track rather than a full bar.
@'
{"five_hour":{"utilization":14.0,"resets_at":"2026-12-31T20:00:00+00:00"},
 "seven_day":{"utilization":31.0,"resets_at":"2027-01-04T00:00:00+00:00"}}
'@ | Out-File $Fixture -Encoding utf8

$scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

function Write-Cfg($json) { $json | Out-File "$cfgDir\settings.json" -Encoding utf8 }
function Start-Widget {
    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Milliseconds 450
    Start-Process $Exe -ArgumentList @('--demo',"`"$Fixture`"",'--config',"`"$cfgDir`"") | Out-Null
    Start-Sleep -Milliseconds 2300
    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
}
function Grab($x,$y,$w,$h) {
    $b = New-Object System.Drawing.Bitmap $w,$h
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x,$y,0,0,(New-Object System.Drawing.Size $w,$h))
    $g.Dispose(); return $b
}

$backdrop = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'backdrop.ps1'))
Start-Sleep -Milliseconds 900

try {
    # ---------- state A: the card ----------
    $wx = $scr.Left + 200; $wy = $scr.Top + 200
    Write-Cfg @"
{"schemaVersion":1,
 "window":{"x":$wx,"y":$wy,"scale":2.4,"contentOpacity":1.0,"backgroundAlpha":0.99,
  "orientation":"vertical","clickThrough":false,"hideFullScreen":false,
  "showLogo":true,"showBorder":false,"mode":"widget","hairlineEdge":"top",
  "hairlineThickness":8,"style":"card","font":"","barStyle":"","mark":"asterisk"},
 "theme":{"preset":"claude","useServerSeverity":false,"overrides":{}},
 "rows":[{"key":"session","visible":true,"slots":["percent","bar"],"resetFormat":"off","invert":false},
         {"key":"weekly","visible":false,"slots":["percent","bar"],"resetFormat":"off","invert":false}],
 "data":{"pollSeconds":300,"useLiveFeed":false},
 "alerts":{"levels":[],"sound":false,"mutedUntil":0},"lang":"en"}
"@
    $p = Start-Widget
    if (-not $p) { throw 'widget did not start (card)' }
    $r = New-Object GW+R; [void][GW]::GetWindowRect($p.MainWindowHandle,[ref]$r)
    $card = Grab $r.L $r.T ($r.Rt-$r.L) ($r.B-$r.T)
    Write-Host ("card  : {0}x{1}" -f $card.Width, $card.Height)

    # ---------- state B: the hairline ----------
    Write-Cfg @"
{"schemaVersion":1,
 "window":{"x":$wx,"y":$wy,"monitor":"$($scr | Out-Null; [System.Windows.Forms.Screen]::PrimaryScreen.DeviceName.Replace('\','\\'))",
  "scale":1.0,"contentOpacity":1.0,"backgroundAlpha":0.99,
  "orientation":"horizontal","clickThrough":false,"hideFullScreen":false,
  "showLogo":true,"showBorder":false,"mode":"hairline","hairlineEdge":"top",
  "hairlineThickness":8,"style":"card","font":"","barStyle":"","mark":"asterisk"},
 "theme":{"preset":"claude","useServerSeverity":false,"overrides":{}},
 "rows":[{"key":"session","visible":true,"slots":["percent","bar"],"resetFormat":"off","invert":false},
         {"key":"weekly","visible":false,"slots":["percent","bar"],"resetFormat":"off","invert":false}],
 "data":{"pollSeconds":300,"useLiveFeed":false},
 "alerts":{"levels":[],"sound":false,"mutedUntil":0},"lang":"en"}
"@
    $p = Start-Widget
    if (-not $p) { throw 'widget did not start (hairline)' }
    $r2 = New-Object GW+R; [void][GW]::GetWindowRect($p.MainWindowHandle,[ref]$r2)
    $line = Grab $r2.L $r2.T ($r2.Rt-$r2.L) ($r2.B-$r2.T)
    Write-Host ("line  : {0}x{1}" -f $line.Width, $line.Height)

    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
}
finally { if (-not $backdrop.HasExited) { $backdrop.Kill() } }

# ---------- compose ----------
# Card rect, centred with a little headroom; line rect, flush to the top edge.
# Fit the card to ~58% of the canvas width, preserving aspect. Captured at 2.4x so this
# is a downscale, which stays sharp.
$target = [double]($CanvasW * 0.58)
$k = $target / $card.Width
$cw = [int]($card.Width  * $k)
$ch = [int]($card.Height * $k)
$cardRect = New-Object System.Drawing.RectangleF ([single](($CanvasW-$cw)/2)), ([single](($CanvasH-$ch)/2 + 8)), ([single]$cw), ([single]$ch)
$lineH = 3
$lineRect = New-Object System.Drawing.RectangleF 0, 0, ([single]$CanvasW), ([single]$lineH)

function Ease([double]$t) { if ($t -lt 0.5) { return 4*$t*$t*$t } else { $f = -2*$t+2; return 1 - ($f*$f*$f)/2 } }

function Lerp($a,$b,[double]$t) {
    New-Object System.Drawing.RectangleF `
        ([single]($a.X + ($b.X-$a.X)*$t)), ([single]($a.Y + ($b.Y-$a.Y)*$t)), `
        ([single]($a.Width + ($b.Width-$a.Width)*$t)), ([single]($a.Height + ($b.Height-$a.Height)*$t))
}

function DrawFrame($idx, [double]$t) {
    $bmp = New-Object System.Drawing.Bitmap $CanvasW, $CanvasH
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Black)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $rect = Lerp $cardRect $lineRect $t

    $ia = New-Object System.Drawing.Imaging.ImageAttributes
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    # card fades out over the first 70% of the morph, line fades in over the last 70%
    $cm.Matrix33 = [single][Math]::Max(0, 1 - ($t/0.7))
    $ia.SetColorMatrix($cm)
    $dst = New-Object System.Drawing.Rectangle ([int]$rect.X),([int]$rect.Y),([int]$rect.Width),([int]$rect.Height)
    if ($cm.Matrix33 -gt 0.01) {
        $g.DrawImage($card, $dst, 0, 0, $card.Width, $card.Height, 'Pixel', $ia)
    }
    $cm2 = New-Object System.Drawing.Imaging.ColorMatrix
    $cm2.Matrix33 = [single][Math]::Max(0, ($t-0.3)/0.7)
    $ia2 = New-Object System.Drawing.Imaging.ImageAttributes
    $ia2.SetColorMatrix($cm2)
    if ($cm2.Matrix33 -gt 0.01) {
        $g.DrawImage($line, $dst, 0, 0, $line.Width, $line.Height, 'Pixel', $ia2)
    }
    $g.Dispose()
    $bmp.Save((Join-Path $frames ("f{0:d3}.png" -f $idx)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$i = 0
foreach ($n in 1..12) { DrawFrame $i 0.0; $i++ }                       # hold card
foreach ($n in 1..14) { DrawFrame $i (Ease ($n/14)); $i++ }            # collapse
foreach ($n in 1..12) { DrawFrame $i 1.0; $i++ }                       # hold line
foreach ($n in 1..12) { DrawFrame $i (Ease (1 - $n/12)); $i++ }        # and back
$card.Dispose(); $line.Dispose()
Write-Host "frames: $i"

# ---------- encode ----------
$pal = "$frames\palette.png"
& ffmpeg -y -loglevel error -framerate 20 -i "$frames\f%03d.png" -vf "palettegen=max_colors=96:stats_mode=diff" $pal
& ffmpeg -y -loglevel error -framerate 20 -i "$frames\f%03d.png" -i $pal `
  -lavfi "paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle" -loop 0 $OutGif
if (Test-Path $OutGif) { Write-Host ("-> {0}  {1} KB" -f $OutGif, [math]::Round((Get-Item $OutGif).Length/1KB,1)) }
