# Regression test: the widget's height must follow the number of visible metrics.
#
# It did not. A MinHeight of two rows on the row host, plus a rail mark built at a fixed
# two-row height, meant one selected metric still produced a two-metric-tall widget.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "C:\PROJECTS\vibespan\tests\fixtures\usage-future.json",
    [string]$Out     = "C:\PROJECTS\vibespan\docs\rowcount.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class RC { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; } }
'@
} catch { }

$cfgDir = "$env:TEMP\vibespan-shots"; $cfgPath = "$cfgDir\settings.json"
$allKeys = @('session','weekly','weekly:Opus','quantum_badger','monthly_teleport')

function Make-Config($count, $orientation, $mark) {
    $rows = @()
    for ($i = 0; $i -lt $allKeys.Count; $i++) {
        $rows += [ordered]@{ key=$allKeys[$i]; visible=($i -lt $count)
                             slots=@('label','percent','bar','reset')
                             resetFormat='countdown'; invert=$false }
    }
    [ordered]@{
        schemaVersion=1
        window=[ordered]@{ x=120; y=260; scale=1.0; contentOpacity=1.0; backgroundAlpha=0.97
                           orientation=$orientation; clickThrough=$false; hideFullScreen=$false
                           showLogo=$true; showBorder=$true
                           style='vibespan'; font=''; barStyle=''; mark=$mark }
        theme=[ordered]@{ preset='claude'; useServerSeverity=$false; overrides=@{} }
        rows=$rows
        data=[ordered]@{ pollSeconds=300; useLiveFeed=$false }
        alerts=[ordered]@{ levels=@(); sound=$false; mutedUntil=0 }
        lang='en'
    }
}

function Measure-Case($count, $orientation, $mark) {
    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Milliseconds 420
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    (Make-Config $count $orientation $mark) | ConvertTo-Json -Depth 8 | Out-File $cfgPath -Encoding utf8
    Start-Process $Exe -ArgumentList @('--demo', "`"$Fixture`"", '--config', "`"$cfgDir`"") | Out-Null
    Start-Sleep -Milliseconds 2100
    $p = Get-Process Vibespan -EA SilentlyContinue |
         Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
    if (-not $p -or $p.MainWindowHandle -eq 0) { return $null }
    $r = New-Object RC+R
    if (-not [RC]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { return $null }
    @{ w=($r.Rt-$r.L); h=($r.B-$r.T); L=$r.L; T=$r.T; hwnd=$p.MainWindowHandle }
}

$fail = 0
$shots = @()

foreach ($mark in @('rail','asterisk','none')) {
    Write-Host "`n-- horizontal, mark=$mark --"
    $prev = $null
    for ($n = 1; $n -le 4; $n++) {
        $m = Measure-Case $n 'horizontal' $mark
        if (-not $m) { Write-Host "  !! $n rows: no window"; $fail++; continue }
        $note = ''
        if ($prev -ne $null) {
            $delta = $m.h - $prev
            # Each additional row must add height. Equal height for 1 and 2 rows is the bug.
            if ($delta -le 0) { $note = "  <-- FAIL: adding a row did not grow the widget"; $fail++ }
            else { $note = "  (+$delta)" }
        }
        Write-Host ("  {0} row(s): {1} x {2}{3}" -f $n, $m.w, $m.h, $note)
        $prev = $m.h

        if ($mark -eq 'rail') {
            $b = New-Object System.Drawing.Bitmap $m.w, $m.h
            $g = [System.Drawing.Graphics]::FromImage($b)
            $g.CopyFromScreen($m.L, $m.T, 0, 0, (New-Object System.Drawing.Size $m.w, $m.h)); $g.Dispose()
            $shots += @{ n="$n metric$(if($n -gt 1){'s'})"; b=$b }
        }
    }
}

Write-Host "`n-- vertical, mark=rail --"
$prev = $null
for ($n = 1; $n -le 3; $n++) {
    $m = Measure-Case $n 'vertical' 'rail'
    if (-not $m) { Write-Host "  !! $n rows: no window"; $fail++; continue }
    $note = ''
    if ($prev -ne $null) {
        $delta = $m.h - $prev
        if ($delta -le 0) { $note = '  <-- FAIL'; $fail++ } else { $note = "  (+$delta)" }
    }
    Write-Host ("  {0} row(s): {1} x {2}{3}" -f $n, $m.w, $m.h, $note)
    $prev = $m.h
}

Get-Process Vibespan -EA SilentlyContinue |
    Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue

if ($shots.Count -gt 0) {
    $pad=12; $labelW=110; $gap=10
    $totalH=[int]$pad; foreach($s in $shots){ $totalH += [Math]::Max($s.b.Height,18)+$gap }
    $maxW=[int](($shots | ForEach-Object { $_.b.Width } | Measure-Object -Maximum).Maximum)
    $sheet = New-Object System.Drawing.Bitmap ([int]($pad+$labelW+$maxW+$pad)), ([int]$totalH)
    $gg=[System.Drawing.Graphics]::FromImage($sheet)
    $gg.Clear([System.Drawing.Color]::FromArgb(255,12,13,16))
    $font=New-Object System.Drawing.Font 'Consolas',9
    $brush=New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,170,176,190))
    $y=$pad
    foreach($s in $shots){
        $gg.DrawString($s.n,$font,$brush,[single]$pad,[single]($y+4))
        $gg.DrawImage($s.b,[int]($pad+$labelW),[int]$y)
        $y += [Math]::Max($s.b.Height,18)+$gap; $s.b.Dispose()
    }
    $gg.Dispose(); $sheet.Save($Out); $sheet.Dispose()
    Write-Host "`n-> $Out"
}

if ($fail -eq 0) { Write-Host "`nPASS: height tracks the number of visible metrics" -ForegroundColor Green }
else { Write-Host "`n$fail FAILURE(S)" -ForegroundColor Red; exit 1 }
