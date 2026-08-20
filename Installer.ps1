# ============================================================
#  Installs Vibespan.exe - RUN AS ADMINISTRATOR
#
#  1. builds src\*.cs with the C# compiler already in Windows
#  2. creates a local code-signing certificate and signs the exe
#  3. trusts that certificate on this machine, then DESTROYS ITS PRIVATE KEY
#  4. installs into Program Files and starts it WITHOUT elevation
#
#  About step 3. Windows grants the uiAccess privilege - the thing that lets the
#  widget draw above the taskbar - only to a binary whose signature chains to a
#  trusted root on this machine AND which lives in an admin-only directory. Both,
#  or the flag is ignored. There is no softer store: Trusted People satisfies MSIX
#  but not uiAccess.
#
#  Adding a root certificate is not a trivial change, so this installer limits the
#  damage in three ways the usual recipe does not:
#    * the certificate is EKU-constrained to Code Signing, so it cannot be used to
#      intercept TLS - only to sign code
#    * its PRIVATE KEY IS DELETED immediately after signing, so nobody (including
#      anything that later compromises this machine) can mint new code against the
#      trust it was granted
#    * Uninstall.ps1 removes all three certificate store entries
#
#  If you would rather not have a local root certificate at all, run
#  Installer.ps1 -NoUiAccess. The widget installs per-user, needs no admin, and
#  works normally - the taskbar can just draw over it.
#
#  Keep this file ASCII-only: PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
# ============================================================
[CmdletBinding()]
param(
    [switch]$NoUiAccess,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Fail($msg) {
    Write-Host "`n[ERROR] $msg" -ForegroundColor Red
    if (-not $Quiet) { Read-Host 'Press Enter to close' }
    exit 1
}
function Step($n, $msg) { Write-Host "$n $msg" }

try {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $src  = Join-Path $here 'src'
    $man  = Join-Path $here 'Vibespan.manifest'
    if (-not (Test-Path $src)) { Fail "src\ not found next to this script." }
    if (-not (Test-Path $man)) { Fail "Vibespan.manifest not found next to this script." }

    $sources = @(Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) { Fail "No .cs files in src\." }

    $isAdmin = (New-Object Security.Principal.WindowsPrincipal(
                    [Security.Principal.WindowsIdentity]::GetCurrent())
               ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $NoUiAccess -and -not $isAdmin) {
        Fail 'This script must run as administrator (use Installer.bat), or pass -NoUiAccess for a per-user install.'
    }

    $total = if ($NoUiAccess) { 4 } else { 6 }
    $n = 0

    # ---------- stop any running instance ----------
    $n++; Step "$n/$total" 'Stopping running instances...'
    Get-Process -Name 'Vibespan' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600

    # ---------- build ----------
    $n++; Step "$n/$total" "Building $($sources.Count) source files..."
    $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path (Join-Path $fw 'csc.exe'))) {
        $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    }
    $csc = Join-Path $fw 'csc.exe'
    if (-not (Test-Path $csc)) { Fail 'C# compiler (.NET Framework 4) not found.' }
    $wpf = Join-Path $fw 'WPF'

    $exe = Join-Path $env:TEMP 'Vibespan.build.exe'
    Remove-Item $exe -Force -ErrorAction SilentlyContinue

    # A uiAccess="true" binary that is not signed AND not in an admin-only directory does
    # not "silently fall back to a normal window" - Windows REFUSES TO LAUNCH IT, with
    # "A referral was returned from the server". So the per-user install has to be built
    # against a manifest that asks for uiAccess="false". One source of truth, patched.
    $useManifest = $man
    if ($NoUiAccess) {
        $useManifest = Join-Path $env:TEMP 'Vibespan.nouiaccess.manifest'
        (Get-Content $man -Raw).Replace('uiAccess="true"', 'uiAccess="false"') |
            Out-File $useManifest -Encoding utf8
    }

    # /codepage:65001 is a safety net: the sources are UTF-8, and an editor that strips
    # a BOM would otherwise mangle the translated strings.
    # /langversion:5 pins the dialect the in-box compiler actually supports, so a
    # newer-syntax mistake fails here rather than on somebody else's machine.
    $cscArgs = @(
        '/nologo', '/target:winexe', "/out:$exe", "/win32manifest:$useManifest",
        '/codepage:65001', '/langversion:5', '/optimize+', '/warnaserror-',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Xaml.dll',
        '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:Microsoft.CSharp.dll',
        "/r:$wpf\PresentationFramework.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\WindowsBase.dll"
    ) + $sources

    & $csc $cscArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { Fail 'Build failed (see messages above).' }

    # ---------- per-user, no certificate ----------
    if ($NoUiAccess) {
        $n++; Step "$n/$total" 'Installing (per-user, no certificate)...'
        $destDir = Join-Path $env:LOCALAPPDATA 'Vibespan'
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        $dest = Join-Path $destDir 'Vibespan.exe'
        Copy-Item $exe $dest -Force
        Remove-Item $exe -Force -ErrorAction SilentlyContinue

        $n++; Step "$n/$total" 'Starting...'
        Start-Process $dest

        Write-Host "`n[OK] Installed to $destDir" -ForegroundColor Green
        Write-Host 'No certificate was created and nothing needs administrator rights.'
        Write-Host 'The taskbar can draw over the widget in this mode.'
        if (-not $Quiet) { Read-Host 'Press Enter to close' }
        exit 0
    }

    # ---------- certificate ----------
    $n++; Step "$n/$total" 'Local signing certificate...'
    $subject = 'CN=Vibespan Local Signing'

    # Sweep out certificates from earlier installs FIRST. The usual recipe reuses an existing
    # certificate, which this installer cannot do because it destroys the private key after
    # signing - so without this, every reinstall silently adds another trusted root. Two
    # installs left two. The whole point of the key destruction is to keep the machine's trust
    # surface small, and quietly accumulating roots would undo it.
    $swept = 0
    foreach ($store in 'Root', 'TrustedPublisher', 'My') {
        try {
            # @() first, deliberately. Removing certificates while still enumerating the store
            # mutates the collection mid-pipeline and silently skips entries - it deleted only
            # one of two and reported success for both.
            $stale = @(Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction Stop |
                       Where-Object { $_.Subject -eq $subject })
            foreach ($c in $stale) {
                Remove-Item -Path "Cert:\LocalMachine\$store\$($c.Thumbprint)" -DeleteKey -Force -ErrorAction Stop
                $swept++
            }
        } catch { }
    }
    if ($swept -gt 0) { Write-Host "     removed $swept certificate(s) from a previous install" -ForegroundColor DarkGray }
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
                -KeyUsage DigitalSignature -CertStoreLocation 'Cert:\LocalMachine\My' `
                -NotAfter (Get-Date).AddYears(10)
    $thumb = $cert.Thumbprint

    # A long lifetime is deliberate. Shortening it does not reduce risk once the private
    # key is gone, but it DOES mean the chain expires and the widget silently loses
    # uiAccess on that date.

    $n++; Step "$n/$total" 'Trusting the certificate...'
    $cer = Join-Path $env:TEMP 'VibespanLocal.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
    Remove-Item $cer -Force -ErrorAction SilentlyContinue

    $n++; Step "$n/$total" 'Signing, discarding the key, and installing...'

    # Timestamp so the signature stays verifiable independently of the certificate.
    # No internet is not fatal here, so fall back rather than abort.
    $sig = $null
    try {
        $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
                   -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    } catch {
        Write-Host '     (timestamp server unreachable; signing without a timestamp)' -ForegroundColor DarkYellow
    }
    if ($null -eq $sig -or $sig.Status -ne 'Valid') {
        $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256
    }
    if ($sig.Status -ne 'Valid') { Fail ('Invalid signature: ' + $sig.StatusMessage) }

    # THE IMPORTANT BIT. The signature above is already permanent; the private key is not
    # needed again. Removing it from LocalMachine\My means the trust just granted cannot be
    # reused to sign anything else, which is the actual risk of a machine-trusted root.
    try {
        Remove-Item -Path "Cert:\LocalMachine\My\$thumb" -DeleteKey -Force -ErrorAction Stop
        Write-Host '     signing key destroyed (the certificate can no longer sign anything)' -ForegroundColor DarkGray
    } catch {
        Write-Host "     [WARN] could not remove the signing key: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host '            remove it by hand: certlm.msc > Personal > Certificates' -ForegroundColor Yellow
    }

    $destDir = Join-Path $env:ProgramFiles 'Vibespan'
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    $dest = Join-Path $destDir 'Vibespan.exe'
    Copy-Item $exe $dest -Force
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $here 'Uninstall.ps1') $destDir -Force -ErrorAction SilentlyContinue

    # ---------- start, de-elevated ----------
    $n++; Step "$n/$total" 'Starting...'
    # Launching directly from this elevated shell would hand the widget an admin token,
    # which it never needs: it would then take ownership of every file it writes -
    # including Claude Code's credentials file - and Windows suppresses notifications from
    # elevated processes. Handing the path to Explorer runs it as the logged-on user.
    try { Start-Process 'explorer.exe' -ArgumentList "`"$dest`"" }
    catch { Start-Process $dest }

    Write-Host "`n[OK] Installed to $destDir" -ForegroundColor Green
    Write-Host 'Right-click the widget for every setting. Drag it to move; drag the'
    Write-Host 'bottom-right corner to resize.'
    Write-Host ''
    Write-Host 'To remove it completely, including the certificate:' -ForegroundColor DarkGray
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$destDir\Uninstall.ps1`"" -ForegroundColor DarkGray
    if (-not $Quiet) { Read-Host 'Press Enter to close' }
}
catch {
    Fail $_.Exception.Message
}
