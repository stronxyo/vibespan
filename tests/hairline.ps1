# Verifies hairline mode: the window must span a full monitor edge, be exactly
# thickness x visible-metrics tall, and sit flush against the chosen edge.
param(
    [string]$Exe     = "$env:TEMP\Vibespan.exe",
    [string]$Fixture = "C:\PROJECTS\vibespan\tests\fixtures\usage-future.json",
    [string]$Out     = "C:\PROJECTS\vibespan\docs\hairline.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
try {
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class HL { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; } }
'@
} catch { }

$cfgDir = "$env:TEMP\vibespan-shots"; $cfgPath = "$cfgDir\settings.json"
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host ("primary monitor: {0},{1} {2}x{3}" -f $screen.Left,$screen.Top,$screen.Width,$screen.Height)

function Make($edge, $thick, $visible) {
    $keys = @('session','weekly','weekly:Opus')
    $rows = @()
    for ($i=0; $i -lt $keys.Count; $i++) {
        $rows += [ordered]@{ key=$keys[$i]; visible=($i -lt $visible)
                             slots=@('label','percent','bar','reset'); resetFormat='countdown'; invert=$false }
    }
    [ordered]@{
        schemaVersion=1
        window=[ordered]@{ x=$screen.Left+50; y=$screen.Top+50; monitor=[System.Windows.Forms.Screen]::PrimaryScreen.DeviceName
                           scale=1.0; contentOpacity=1.0; backgroundAlpha=0.95
                           orientation='horizontal'; clickThrough=$false; hideFullScreen=$false
                           showLogo=$true; showBorder=$true
                           mode='hairline'; hairlineEdge=$edge; hairlineThickness=$thick
                           style='vibespan'; font=''; barStyle=''; mark='' }
        theme=[ordered]@{ preset='claude'; useServerSeverity=$false; overrides=@{} }
        rows=$rows
        data=[ordered]@{ pollSeconds=300; useLiveFeed=$false }
        alerts=[ordered]@{ levels=@(); sound=$false; mutedUntil=0 }
        lang='en'
    }
}

function Run($edge, $thick, $visible) {
    Get-Process Vibespan -EA SilentlyContinue |
        Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Milliseconds 450
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    (Make $edge $thick $visible) | ConvertTo-Json -Depth 8 | Out-File $cfgPath -Encoding utf8
    Start-Process $Exe -ArgumentList @('--demo',"`"$Fixture`"",'--config',"`"$cfgDir`"") | Out-Null
    Start-Sleep -Milliseconds 2300
    $p = Get-Process Vibespan -EA SilentlyContinue |
         Where-Object { $_.Path -like "$env:TEMP*" } | Select-Object -First 1
    if (-not $p -or $p.MainWindowHandle -eq 0) { return $null }
    $r = New-Object HL+R
    if (-not [HL]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { return $null }
    @{ L=$r.L; T=$r.T; W=($r.Rt-$r.L); H=($r.B-$r.T) }
}

# Blank backdrop: the strip is translucent, so whatever is on the desktop composites into
# every pixel this test reads.
$backdrop = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'backdrop.ps1'))
Start-Sleep -Milliseconds 900

$fail = 0
function Check($cond, $msg) {
    if ($cond) { Write-Host "     ok   $msg" }
    else { Write-Host "     FAIL $msg" -ForegroundColor Red; $script:fail++ }
}

foreach ($case in @(
    @{e='bottom'; t=2; v=1}, @{e='bottom'; t=2; v=2}, @{e='bottom'; t=4; v=1},
    @{e='top';    t=3; v=2}, @{e='left';   t=3; v=1}, @{e='right'; t=2; v=2})) {

    $m = Run $case.e $case.t $case.v
    Write-Host ("`n-- edge={0} thickness={1} metrics={2} --" -f $case.e,$case.t,$case.v)
    if (-not $m) { Write-Host "     FAIL no window"; $fail++; continue }
    Write-Host ("     rect {0},{1}  {2}x{3}" -f $m.L,$m.T,$m.W,$m.H)

    $expect = $case.t * $case.v
    if ($case.e -eq 'bottom' -or $case.e -eq 'top') {
        Check ($m.W -eq $screen.Width) "spans the full monitor width ($($screen.Width))"
        Check ($m.H -eq $expect)       "thickness x metrics = $expect px"
        Check ($m.L -eq $screen.Left)  "flush to the left edge"
        if ($case.e -eq 'bottom') { Check (($m.T + $m.H) -eq $screen.Bottom) "flush to the bottom" }
        else                      { Check ($m.T -eq $screen.Top)             "flush to the top" }
    } else {
        Check ($m.H -eq $screen.Height) "spans the full monitor height ($($screen.Height))"
        Check ($m.W -eq $expect)        "thickness x metrics = $expect px"
        if ($case.e -eq 'left') { Check ($m.L -eq $screen.Left)            "flush to the left" }
        else                    { Check (($m.L + $m.W) -eq $screen.Right)  "flush to the right" }
    }
}

# ---- fill proportion ----
# Geometry passing is not enough: the strip was once the right size on screen while the
# fill inside it rendered 4% for a 61% reading, because the width came from a SizeChanged
# handler that did not fire after the window was widened. Measure the actual pixels.
Write-Host "`n-- fill proportion (fixture: session 88%, weekly 96.5%) --"
$m = Run 'bottom' 6 2
if ($m) {
    $b = New-Object System.Drawing.Bitmap $m.W, $m.H
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($m.L, $m.T, 0, 0, (New-Object System.Drawing.Size $m.W, $m.H)); $g.Dispose()
    foreach ($lane in @(@{y=2; want=96.5; name='outer lane'}, @{y=9; want=88.0; name='inner lane'})) {
        if ($lane.y -ge $m.H) { continue }
        # Walk the CONTIGUOUS run from x=0, not the last bright pixel anywhere on the row.
        # The strip is translucent, so a bright window edge behind it reads as "fill" and
        # inflates the measurement - 96.5% once measured as 99.7% because of a grey pixel
        # near the screen edge showing through.
        $last = -1
        for ($x = 0; $x -lt $m.W; $x++) {
            $c = $b.GetPixel($x, $lane.y)
            if (($c.R + $c.G + $c.B) -gt 200) { $last = $x } else { break }
        }
        $pct = [math]::Round(($last + 1) / $m.W * 100, 1)
        $ok = [math]::Abs($pct - $lane.want) -le 1.5
        Check $ok ("{0}: fill {1}% (expected ~{2}%)" -f $lane.name, $pct, $lane.want)
    }
    $b.Dispose()
} else { Write-Host '     FAIL could not measure fill'; $fail++ }

# a picture of the most useful case
$m = Run 'bottom' 3 2
if ($m) {
    $band = 60
    $b = New-Object System.Drawing.Bitmap $screen.Width, $band
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($screen.Left, ($screen.Bottom - $band), 0, 0,
                      (New-Object System.Drawing.Size $screen.Width, $band))
    $g.Dispose(); $b.Save($Out); $b.Dispose()
    Write-Host "`nscreenshot -> $Out"
}
Get-Process Vibespan -EA SilentlyContinue |
    Where-Object { $_.Path -like "$env:TEMP*" } | Stop-Process -Force -EA SilentlyContinue
if (-not $backdrop.HasExited) { $backdrop.Kill() }

if ($fail -eq 0) { Write-Host "`nPASS" -ForegroundColor Green }
else { Write-Host "`n$fail FAILURE(S)" -ForegroundColor Red; exit 1 }
