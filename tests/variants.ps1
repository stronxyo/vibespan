# Renders the widget under a set of configurations and composes one contact sheet, so the
# customization can be checked at a glance. Uses --demo against a fixture: restarting the
# widget a dozen times against the real endpoint would trip its rate limit.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "C:\PROJECTS\vibespan\tests\fixtures\usage-future.json",
    [string]$Out     = "C:\PROJECTS\vibespan\docs\variants.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sig = @'
using System;using System.Runtime.InteropServices;
public class V {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
}
'@
try { Add-Type -TypeDefinition $sig } catch { }   # already loaded in this session

# Isolated: never write over a real install's settings.
$cfgDir  = "$env:TEMP\vibespan-shots"
$cfgPath = "$cfgDir\settings.json"

function Stop-Widget {
    Get-Process Vibespan -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Every variant starts from this baseline and mutates it, so each one differs in exactly the
# stated way.
function Base-Config {
    $rows = @('session','weekly','weekly:Opus','quantum_badger','monthly_teleport') | ForEach-Object {
        [ordered]@{ key = $_; visible = ($_ -in @('session','weekly'));
                    slots = @('label','percent','bar','reset');
                    resetFormat = 'countdown'; invert = $false }
    }
    [ordered]@{
        schemaVersion = 1
        window = [ordered]@{ x = 60; y = 200; scale = 1.0; contentOpacity = 1.0
                             backgroundAlpha = 0.95; orientation = 'horizontal'
                             clickThrough = $false; hideFullScreen = $false
                             showLogo = $true; showBorder = $true }
        theme  = [ordered]@{ preset = 'claude'; useServerSeverity = $false; overrides = @{} }
        rows   = $rows
        data   = [ordered]@{ pollSeconds = 300; useLiveFeed = $false }
        alerts = [ordered]@{ levels = @(); sound = $false; mutedUntil = 0 }
        lang   = 'en'
    }
}

$variants = @(
    @{ name = 'default  (5h + 7d, everything on)';       apply = { param($c) } },
    @{ name = 'scale 150%';                              apply = { param($c) $c.window.scale = 1.5 } },
    @{ name = 'scale 75%';                               apply = { param($c) $c.window.scale = 0.75 } },
    @{ name = 'bar only, no logo, no border';            apply = { param($c)
            foreach ($r in $c.rows) { $r.slots = @('label','bar') }
            $c.window.showLogo = $false; $c.window.showBorder = $false } },
    @{ name = 'percent only';                            apply = { param($c)
            foreach ($r in $c.rows) { $r.slots = @('label','percent') } } },
    @{ name = 'no countdown (bar + percent)';            apply = { param($c)
            foreach ($r in $c.rows) { $r.slots = @('label','percent','bar') } } },
    @{ name = 'clock instead of countdown';              apply = { param($c)
            foreach ($r in $c.rows) { $r.resetFormat = 'clock' } } },
    @{ name = 'remaining instead of used';               apply = { param($c)
            foreach ($r in $c.rows) { $r.invert = $true } } },
    @{ name = 'all 5 metrics visible';                   apply = { param($c)
            foreach ($r in $c.rows) { $r.visible = $true } } },
    @{ name = 'vertical orientation';                    apply = { param($c)
            $c.window.orientation = 'vertical' } },
    @{ name = 'accessible theme (Okabe-Ito)';            apply = { param($c)
            $c.theme.preset = 'accessible' } },
    @{ name = 'high contrast theme';                     apply = { param($c)
            $c.theme.preset = 'contrast' } },
    @{ name = 'mono theme, 70% opacity';                 apply = { param($c)
            $c.theme.preset = 'mono'; $c.window.contentOpacity = 0.7 } },
    @{ name = 'per-row custom colours';                  apply = { param($c)
            $c.rows[0].accent = '#56B4E9'; $c.rows[1].accent = '#C77DFF' } }
)

$shots = @()
foreach ($v in $variants) {
    Stop-Widget
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    $cfg = Base-Config
    & $v.apply $cfg
    $cfg | ConvertTo-Json -Depth 8 | Out-File $cfgPath -Encoding utf8

    Start-Process $Exe -ArgumentList @('--demo', "`"$Fixture`"", '--config', "`"$cfgDir`"") | Out-Null
    Start-Sleep -Milliseconds 2200

    $proc = Get-Process Vibespan -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
    if (-not $proc) { Write-Host "  !! $($v.name): process not running"; continue }
    $h = $proc.MainWindowHandle
    $r = New-Object V+R
    if ($h -eq 0 -or -not [V]::GetWindowRect($h, [ref]$r)) { Write-Host "  !! $($v.name): no rect"; continue }

    $w = $r.Rt - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Host "  !! $($v.name): empty rect"; continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $ht))
    $g.Dispose()
    $shots += @{ name = $v.name; bmp = $bmp }
    Write-Host ("  {0,-40} {1} x {2}" -f $v.name, $w, $ht)
}
Stop-Widget

# ---- contact sheet ----
$pad = 14; $labelW = 250; $gap = 10
$totalH = $pad
foreach ($s in $shots) { $totalH += [Math]::Max($s.bmp.Height, 22) + $gap }
# Measure-Object returns a double; the Bitmap ctor needs ints.
$maxShot = [int](($shots | ForEach-Object { $_.bmp.Width } | Measure-Object -Maximum).Maximum)
$totalW  = [int]($pad + $labelW + $maxShot + $pad)
$totalH  = [int]$totalH

$sheet = New-Object System.Drawing.Bitmap $totalW, $totalH
$gg = [System.Drawing.Graphics]::FromImage($sheet)
$gg.Clear([System.Drawing.Color]::FromArgb(255, 12, 13, 16))
$font = New-Object System.Drawing.Font 'Consolas', 9
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 170, 176, 190))

$y = $pad
foreach ($s in $shots) {
    $gg.DrawString($s.name, $font, $brush, [single]$pad, [single]($y + 6))
    $gg.DrawImage($s.bmp, [int]($pad + $labelW), [int]$y)
    $y += [Math]::Max($s.bmp.Height, 22) + $gap
    $s.bmp.Dispose()
}
$gg.Dispose()
$sheet.Save($Out)
$sheet.Dispose()
Write-Host "`ncontact sheet -> $Out"
