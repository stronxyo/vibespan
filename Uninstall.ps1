# ============================================================
#  Removes Vibespan completely - RUN AS ADMINISTRATOR
#
#  The predecessor to this project shipped no uninstaller and left four manual
#  certlm.msc steps in its README. Since the installer adds a certificate to the
#  machine's trusted root store, being able to take it back out again in one
#  command is not optional.
#
#  Removes, in order:
#    1. the running process
#    2. the Startup shortcut
#    3. the statusLine entry in ~/.claude/settings.json, if Vibespan wrote it
#    4. C:\Program Files\Vibespan  and  %LOCALAPPDATA%\Vibespan
#    5. the certificate from Root, TrustedPublisher and My
#
#  Keep this file ASCII-only: PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
# ============================================================
[CmdletBinding()]
param([switch]$KeepSettings, [switch]$Quiet)

$ErrorActionPreference = 'Continue'
$subject = 'CN=Vibespan Local Signing'
$removed = @()
$left    = @()

function Note($m) { Write-Host "  $m" }

Write-Host ''
Write-Host '  Vibespan uninstaller' -ForegroundColor Cyan
Write-Host ''

$isAdmin = (New-Object Security.Principal.WindowsPrincipal(
                [Security.Principal.WindowsIdentity]::GetCurrent())
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# 1. process
$proc = Get-Process -Name 'Vibespan' -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
    $removed += 'running process'
}

# 2. autostart shortcut
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Vibespan.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force -ErrorAction SilentlyContinue; $removed += 'startup shortcut' }

# 3. the statusLine entry, but only if it is ours
$claudeSettings = Join-Path $env:USERPROFILE '.claude\settings.json'
if (Test-Path $claudeSettings) {
    try {
        $raw = Get-Content $claudeSettings -Raw
        $j = $raw | ConvertFrom-Json
        $cmd = $null
        if ($j.PSObject.Properties.Name -contains 'statusLine') { $cmd = [string]$j.statusLine.command }
        if ($cmd -and ($cmd -match 'Vibespan' -or $cmd -match '--feed')) {
            $keep = [ordered]@{}
            foreach ($p in $j.PSObject.Properties) { if ($p.Name -ne 'statusLine') { $keep[$p.Name] = $p.Value } }
            ($keep | ConvertTo-Json -Depth 12) | Out-File $claudeSettings -Encoding utf8
            $removed += 'Claude Code statusLine entry'
        }
        elseif ($cmd) {
            Note 'left the statusLine entry alone - it was not written by Vibespan'
        }
    } catch { Note "could not read $claudeSettings ($($_.Exception.Message))" }
}

# 4. files
foreach ($d in @((Join-Path $env:ProgramFiles 'Vibespan'), (Join-Path $env:LOCALAPPDATA 'Vibespan'))) {
    if (-not (Test-Path $d)) { continue }
    if ($KeepSettings -and $d -like '*LOCALAPPDATA*') { continue }
    try {
        # Do not delete the copy of this script out from under itself.
        if ($PSCommandPath -and $PSCommandPath.StartsWith($d, [StringComparison]::OrdinalIgnoreCase)) {
            Get-ChildItem $d -Exclude (Split-Path $PSCommandPath -Leaf) |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            $removed += "$d (this script remains; delete the folder afterwards)"
        } else {
            Remove-Item $d -Recurse -Force -ErrorAction Stop
            $removed += $d
        }
    } catch { $left += "$d  ($($_.Exception.Message))" }
}
if ($KeepSettings) { Note 'kept %LOCALAPPDATA%\Vibespan (settings and log)' }

# 5. certificates
if ($isAdmin) {
    foreach ($store in 'Root', 'TrustedPublisher', 'My') {
        try {
            # @() is explicit on purpose: assigning a pipeline already materialises it, but
            # this reads identically to a streaming Remove-Item, which DOES skip entries.
            $found = @(Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction Stop |
                       Where-Object { $_.Subject -eq $subject })
            foreach ($c in $found) {
                Remove-Item -Path "Cert:\LocalMachine\$store\$($c.Thumbprint)" -DeleteKey -Force -ErrorAction Stop
                $removed += "certificate from LocalMachine\$store"
            }
        } catch { $left += "certificate in LocalMachine\$store  ($($_.Exception.Message))" }
    }
} else {
    $left += 'certificates - re-run this script as administrator to remove them'
}

Write-Host ''
if ($removed.Count -gt 0) {
    Write-Host '  Removed:' -ForegroundColor Green
    $removed | ForEach-Object { Write-Host "    - $_" }
} else {
    Write-Host '  Nothing found to remove.' -ForegroundColor DarkGray
}
if ($left.Count -gt 0) {
    Write-Host ''
    Write-Host '  Left behind:' -ForegroundColor Yellow
    $left | ForEach-Object { Write-Host "    - $_" }
}

# Verify rather than assert: the whole point of this script is the certificate.
Write-Host ''
$still = @()
foreach ($store in 'Root', 'TrustedPublisher', 'My') {
    try {
        if (Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction Stop |
            Where-Object { $_.Subject -eq $subject }) { $still += $store }
    } catch { }
}
if ($still.Count -eq 0) { Write-Host '  Verified: no Vibespan certificate remains.' -ForegroundColor Green }
else { Write-Host "  WARNING: a certificate still exists in: $($still -join ', ')" -ForegroundColor Red }

Write-Host ''
if (-not $Quiet) { Read-Host 'Press Enter to close' }
