# Contact sheet of the visual-identity axis: style presets, marks, bar styles, fonts and the
# label toggle. Uses --demo so restarting a dozen times never touches the rate-limited endpoint.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "C:\PROJECTS\vibespan\tests\fixtures\usage-future.json",
    [string]$Out     = "C:\PROJECTS\vibespan\docs\styles.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class VS { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; } }
'@
} catch { }

# Isolated: never write over a real install's settings.
$cfgDir = "$env:TEMP\vibespan-shots"; $cfgPath = "$cfgDir\settings.json"

function Base-Config {
    $rows = @('session','weekly') | ForEach-Object {
        [ordered]@{ key=$_; visible=$true; slots=@('label','percent','bar','reset')
                    resetFormat='countdown'; invert=$false }
    }
    [ordered]@{
        schemaVersion=1
        window=[ordered]@{ x=260; y=300; scale=1.0; contentOpacity=1.0; backgroundAlpha=0.95
                           orientation='horizontal'; clickThrough=$false; hideFullScreen=$false
                           showLogo=$true; showBorder=$true
                           style='vibespan'; font=''; barStyle=''; mark='' }
        theme=[ordered]@{ preset='claude'; useServerSeverity=$false; overrides=@{} }
        rows=$rows
        data=[ordered]@{ pollSeconds=300; useLiveFeed=$false }
        alerts=[ordered]@{ levels=@(); sound=$false; mutedUntil=0 }
        lang='en'
    }
}

$variants = @(
    @{ n='style: Vibespan  (default)'; a={ param($c) } },
    @{ n='style: Classic';             a={ param($c) $c.window.style='classic' } },
    @{ n='style: Slim';                a={ param($c) $c.window.style='slim' } },
    @{ n='style: Card';                a={ param($c) $c.window.style='card' } },
    @{ n='style: Terminal';            a={ param($c) $c.window.style='terminal' } },
    @{ n='bar: continuous';            a={ param($c) $c.window.barStyle='continuous' } },
    @{ n='bar: segmented';             a={ param($c) $c.window.barStyle='segmented' } },
    @{ n='bar: blocks';                a={ param($c) $c.window.barStyle='blocks' } },
    @{ n='mark: asterisk';             a={ param($c) $c.window.mark='asterisk' } },
    @{ n='mark: rail';                 a={ param($c) $c.window.mark='rail' } },
    @{ n='mark: dot';                  a={ param($c) $c.window.mark='dot' } },
    @{ n='mark: none';                 a={ param($c) $c.window.mark='none' } },
    @{ n='NO LABEL (title removed)';   a={ param($c) foreach($r in $c.rows){ $r.slots=@('percent','bar','reset') } } },
    @{ n='no label + no mark';         a={ param($c) $c.window.mark='none'
                                           foreach($r in $c.rows){ $r.slots=@('percent','bar','reset') } } },
    @{ n='font: Segoe UI';             a={ param($c) $c.window.font='Segoe UI' } },
    @{ n='font: Consolas';             a={ param($c) $c.window.font='Consolas' } },
    @{ n='font: Cascadia Mono';        a={ param($c) $c.window.font='Cascadia Mono' } },
    @{ n='font: Georgia';              a={ param($c) $c.window.font='Georgia' } },
    @{ n='Terminal + accessible';      a={ param($c) $c.window.style='terminal'; $c.theme.preset='accessible' } },
    @{ n='Card + high contrast';       a={ param($c) $c.window.style='card'; $c.theme.preset='contrast' } }
)

# ---- black backdrop -------------------------------------------------------
# The widget is translucent, so the desktop composites into every captured
# pixel. Put something blank behind it before capturing anything.
$backdrop = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',
    (Join-Path $PSScriptRoot 'backdrop.ps1'))
Start-Sleep -Milliseconds 900
function Stop-Backdrop {
    if ($script:backdrop -and -not $script:backdrop.HasExited) {
        try { $script:backdrop.Kill() } catch { }
    }
}

$shots = @()
foreach ($v in $variants) {
    Get-Process Vibespan -EA SilentlyContinue | Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Milliseconds 420
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    $cfg = Base-Config; & $v.a $cfg
    $cfg | ConvertTo-Json -Depth 8 | Out-File $cfgPath -Encoding utf8
    Start-Process $Exe -ArgumentList @('--demo', "`"$Fixture`"", '--config', "`"$cfgDir`"") | Out-Null
    Start-Sleep -Milliseconds 2100
    $p = Get-Process Vibespan -EA SilentlyContinue | Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
    if (-not $p -or $p.MainWindowHandle -eq 0) { Write-Host "  !! $($v.n)"; continue }
    $r = New-Object VS+R
    if (-not [VS]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { continue }
    $w = $r.Rt-$r.L; $h = $r.B-$r.T
    if ($w -le 0 -or $h -le 0) { continue }
    $b = New-Object System.Drawing.Bitmap $w,$h
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size $w,$h)); $g.Dispose()
    $shots += @{ n=$v.n; b=$b }
    Write-Host ("  {0,-34} {1} x {2}" -f $v.n, $w, $h)
}
Get-Process Vibespan -EA SilentlyContinue | Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
Stop-Backdrop

$pad=14; $labelW=230; $gap=9
$totalH=[int]$pad; foreach($s in $shots){ $totalH += [Math]::Max($s.b.Height,20)+$gap }
$maxW=[int](($shots | ForEach-Object { $_.b.Width } | Measure-Object -Maximum).Maximum)
$totalW=[int]($pad+$labelW+$maxW+$pad)
$sheet = New-Object System.Drawing.Bitmap $totalW,([int]$totalH)
$gg=[System.Drawing.Graphics]::FromImage($sheet)
$gg.Clear([System.Drawing.Color]::FromArgb(255,12,13,16))
$font=New-Object System.Drawing.Font 'Consolas',9
$brush=New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,170,176,190))
$y=$pad
foreach($s in $shots){
  $gg.DrawString($s.n,$font,$brush,[single]$pad,[single]($y+5))
  $gg.DrawImage($s.b,[int]($pad+$labelW),[int]$y)
  $y += [Math]::Max($s.b.Height,20)+$gap; $s.b.Dispose()
}
$gg.Dispose(); $sheet.Save($Out); $sheet.Dispose()
Write-Host "`n-> $Out"
