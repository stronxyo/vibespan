# ============================================================
#  One-line installer for Vibespan.
#
#    irm https://raw.githubusercontent.com/stronxyo/vibespan/main/web-install.ps1 | iex
#
#  Downloads this repository and runs Installer.ps1, which builds the widget from
#  source on your machine. Read the README before running it: the widget reads and
#  writes your Claude Code credentials, and the default install adds a local
#  certificate to your machine's trusted root store.
#
#  Pass -NoUiAccess for a per-user install with no administrator prompt and no
#  certificate at all:
#    & ([scriptblock]::Create((irm .../web-install.ps1))) -NoUiAccess
#
#  Keep this file ASCII-only: PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
# ============================================================
param([switch]$NoUiAccess)

$ErrorActionPreference = 'Stop'
$repo   = 'stronxyo/vibespan'
$branch = 'main'

Write-Host ''
Write-Host '  Vibespan - Claude usage widget for Windows' -ForegroundColor Cyan
Write-Host "  Source: https://github.com/$repo"
Write-Host ''

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Sweep up anything an interrupted earlier run left behind: closing the window during
# the elevated step skips the finally block below. Only touch folders older than an
# hour, so a concurrent run is left alone.
Get-ChildItem $env:TEMP -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'vibespan-*' -and $_.CreationTime -lt (Get-Date).AddHours(-1) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$work = Join-Path $env:TEMP ('vibespan-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    $zip = Join-Path $work 'source.zip'
    Write-Host '1/3 Downloading the source...'
    Invoke-WebRequest -Uri "https://github.com/$repo/archive/refs/heads/$branch.zip" `
        -OutFile $zip -UseBasicParsing

    Write-Host '2/3 Extracting...'
    Expand-Archive -Path $zip -DestinationPath $work -Force
    $extracted = Get-ChildItem $work -Directory | Select-Object -First 1
    if (-not $extracted) { throw 'The downloaded archive looks empty.' }
    $installer = Join-Path $extracted.FullName 'Installer.ps1'
    if (-not (Test-Path $installer)) { throw 'Installer.ps1 is missing from the archive.' }

    if ($NoUiAccess) {
        Write-Host '3/3 Installing per-user (no administrator prompt, no certificate).'
        & $installer -NoUiAccess
    }
    else {
        Write-Host '3/3 Running the installer - accept the administrator prompt.'
        Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $installer + '"'))
    }
}
catch {
    Write-Host ''
    Write-Host ('[ERROR] ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'You can also download the repository manually and run Installer.bat.'
}
finally {
    # The installer copied the built exe out; the sources are only needed for the build.
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
