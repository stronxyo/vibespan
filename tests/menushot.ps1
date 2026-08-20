# Captures the right-click menu against a black backdrop.
#
# The previous menu screenshot was taken over a live desktop and had folder names, drive
# labels and a file listing legible behind it. A screenshot in a public README is published
# data - it gets the same care as anything else the widget touches.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "C:\PROJECTS\vibespan\tests\fixtures\usage-future.json",
    [string]$Out     = "C:\PROJECTS\vibespan\docs\shot-menu.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class MS {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
 public const uint RD=0x0008, RU=0x0010;
 public static void Right(int x,int y){ SetCursorPos(x,y); mouse_event(RD,0,0,0,UIntPtr.Zero); mouse_event(RU,0,0,0,UIntPtr.Zero); }
}
'@
} catch { }

$cfgDir = "$env:TEMP\vibespan-shots"
New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
$scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$wx = $scr.Left + 120; $wy = $scr.Top + 120

@"
{"schemaVersion":1,
 "window":{"x":$wx,"y":$wy,"scale":1.0,"contentOpacity":1.0,"backgroundAlpha":0.97,
  "orientation":"horizontal","clickThrough":false,"hideFullScreen":false,
  "showLogo":true,"showBorder":true,"mode":"widget","hairlineEdge":"bottom",
  "hairlineThickness":2,"style":"vibespan","font":"","barStyle":"","mark":""},
 "theme":{"preset":"claude","useServerSeverity":false,"overrides":{}},
 "rows":[{"key":"session","visible":true,"slots":["label","percent","bar","reset"],"resetFormat":"countdown","invert":false},
         {"key":"weekly","visible":true,"slots":["label","percent","bar","reset"],"resetFormat":"countdown","invert":false}],
 "data":{"pollSeconds":300,"useLiveFeed":false},
 "alerts":{"levels":[95],"sound":false,"mutedUntil":0},"lang":"en"}
"@ | Out-File "$cfgDir\settings.json" -Encoding utf8

$backdrop = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'backdrop.ps1'))
Start-Sleep -Milliseconds 900

Get-Process Vibespan -EA SilentlyContinue |
    Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Milliseconds 400
Start-Process $Exe -ArgumentList @('--demo',"`"$Fixture`"",'--config',"`"$cfgDir`"") | Out-Null
Start-Sleep -Milliseconds 2400

$p = Get-Process Vibespan -EA SilentlyContinue |
     Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
if (-not $p) { Write-Host 'widget did not start'; if(-not $backdrop.HasExited){$backdrop.Kill()}; exit 1 }

$r = New-Object MS+R
[void][MS]::GetWindowRect($p.MainWindowHandle, [ref]$r)
Write-Host ("widget at {0},{1} {2}x{3}" -f $r.L,$r.T,($r.Rt-$r.L),($r.B-$r.T))

[MS]::Right(($r.L + 60), ($r.T + 14))
Start-Sleep -Milliseconds 1500

# Capture widget + the menu that drops below it.
$x = $r.L - 12; $y = $r.T - 12
$w = 340; $h = 430
if (($x + $w) -gt $scr.Right)  { $w = $scr.Right  - $x }
if (($y + $h) -gt $scr.Bottom) { $h = $scr.Bottom - $y }
$b = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose(); $b.Save($Out); $b.Dispose()
Write-Host "-> $Out  (${w}x${h})"

$p | Stop-Process -Force -EA SilentlyContinue
if (-not $backdrop.HasExited) { $backdrop.Kill() }
