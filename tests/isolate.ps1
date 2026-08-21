# Narrows down which config field stops the hairline lane stretching to the strip width.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class IS { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; } }
'@
} catch { }

$d = "$env:TEMP\vibespan-shots"
$dev = [System.Windows.Forms.Screen]::PrimaryScreen.DeviceName.Replace('\','\\')

function Try-Cfg($name, $style, $mark, $metrics, $slots, $border) {
    $rows = @()
    $rows += '{"key":"session","visible":true,"slots":' + $slots + ',"resetFormat":"off","invert":false}'
    $vis = if ($metrics -ge 2) { 'true' } else { 'false' }
    $rows += '{"key":"weekly","visible":' + $vis + ',"slots":' + $slots + ',"resetFormat":"off","invert":false}'
@"
{"schemaVersion":1,
 "window":{"x":200,"y":200,"monitor":"$dev","scale":1.0,"contentOpacity":1.0,"backgroundAlpha":0.99,
  "orientation":"horizontal","clickThrough":false,"hideFullScreen":false,
  "showLogo":true,"showBorder":$border,"mode":"hairline","hairlineEdge":"top",
  "hairlineThickness":8,"style":"$style","font":"","barStyle":"","mark":"$mark"},
 "theme":{"preset":"claude","useServerSeverity":false,"overrides":{}},
 "rows":[$($rows -join ',')],
 "data":{"pollSeconds":300,"useLiveFeed":false},
 "alerts":{"levels":[],"sound":false,"mutedUntil":0},"lang":"en"}
"@ | Out-File "$d\settings.json" -Encoding utf8

    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Milliseconds 400
    Start-Process "$env:TEMP\Vibespan.exe" -ArgumentList @('--demo',"`"$d\low.json`"",'--config',"`"$d`"") | Out-Null
    Start-Sleep -Milliseconds 2400
    $p = Get-Process Vibespan -EA SilentlyContinue |
         Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
    if (-not $p) { Write-Host "  $name : no window"; return }
    $r = New-Object IS+R; [void][IS]::GetWindowRect($p.MainWindowHandle,[ref]$r)
    $w=$r.Rt-$r.L; $h=$r.B-$r.T
    $b = New-Object System.Drawing.Bitmap $w,$h
    $g=[System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size $w,$h)); $g.Dispose()
    $last=-1
    for ($x=0; $x -lt $w; $x++) { $c=$b.GetPixel($x,[int]($h/2)); if (($c.R+$c.G+$c.B) -gt 200) { $last=$x } else { break } }
    $pct = [math]::Round(($last+1)/$w*100,1)
    $b.Dispose(); $p | Stop-Process -Force -EA SilentlyContinue
    Write-Host ("  {0,-34} fill={1,5}%  (want 61)" -f $name, $pct)
}

$bd = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'backdrop.ps1'))
Start-Sleep -Milliseconds 900
try {
    Try-Cfg 'gif config (baseline)'        'card'     'asterisk' 1 '["percent","bar"]' 'false'
    Try-Cfg 'style=vibespan'               'vibespan' 'asterisk' 1 '["percent","bar"]' 'false'
    Try-Cfg 'mark=""'                      'card'     ''         1 '["percent","bar"]' 'false'
    Try-Cfg '2 metrics'                    'card'     'asterisk' 2 '["percent","bar"]' 'false'
    Try-Cfg 'slots with label+reset'       'card'     'asterisk' 1 '["label","percent","bar","reset"]' 'false'
    Try-Cfg 'showBorder=true'              'card'     'asterisk' 1 '["percent","bar"]' 'true'
    Try-Cfg 'test-like (vibespan,2,full)'  'vibespan' ''         2 '["label","percent","bar","reset"]' 'true'
}
finally { if (-not $bd.HasExited) { $bd.Kill() } }
