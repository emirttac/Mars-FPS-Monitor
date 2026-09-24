# Build Mars FPS Monitor v3.0 + compile Inno Setup installer (with silent RTSS bootstrap)
#
# Optional Authenticode (skipped when unset — build still succeeds):
#   $env:MARS_SIGN_TOOL   = path to signtool.exe
#   $env:MARS_SIGN_CERT   = path to .pfx (or use certificate store via custom tool args)
#   $env:MARS_SIGN_PASSWORD = PFX password (optional)
#   $env:MARS_SIGN_TIMESTAMP = timestamp URL (default DigiCert)
#
# Version bump checklist (keep in sync):
#   AppInfo.cs  Version / VersionLabel
#   FPSOverlay.csproj  <Version>
#   installer.iss  #define MyAppVersion
#   Setup output name MarsFPSMonitor_Setup_v{version}.exe

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) {
    throw "Inno Setup 6 not found: $Iscc"
}

Write-Host "==> Publishing win-x64 (framework-dependent)..." -ForegroundColor Cyan
dotnet publish "$Root\FPSOverlay.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false `
    -o "$Root\publish\win-x64" --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "==> Compiling Inno Setup (MarsFPSMonitor_Setup_v3.0.0)..." -ForegroundColor Cyan
& $Iscc "$Root\installer.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$Setup = Get-ChildItem "$Root\dist\MarsFPSMonitor_Setup_*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $Setup) { throw "Setup exe not found under dist\" }

function Invoke-MarsSign([string]$Path) {
    $tool = $env:MARS_SIGN_TOOL
    $cert = $env:MARS_SIGN_CERT
    if ([string]::IsNullOrWhiteSpace($tool) -or [string]::IsNullOrWhiteSpace($cert)) {
        Write-Host "==> Signing skipped (set MARS_SIGN_TOOL + MARS_SIGN_CERT to enable)." -ForegroundColor DarkYellow
        return
    }
    if (-not (Test-Path $tool)) { throw "MARS_SIGN_TOOL not found: $tool" }
    if (-not (Test-Path $cert)) { throw "MARS_SIGN_CERT not found: $cert" }

    $ts = $env:MARS_SIGN_TIMESTAMP
    if ([string]::IsNullOrWhiteSpace($ts)) {
        $ts = "http://timestamp.digicert.com"
    }

    Write-Host "==> Signing $Path ..." -ForegroundColor Cyan
    $args = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $ts, "/f", $cert, $Path)
    if (-not [string]::IsNullOrWhiteSpace($env:MARS_SIGN_PASSWORD)) {
        $args = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $ts, "/f", $cert, "/p", $env:MARS_SIGN_PASSWORD, $Path)
    }
    & $tool @args
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}

Invoke-MarsSign $Setup.FullName

Write-Host "==> Done: $($Setup.FullName) ($([math]::Round($Setup.Length/1MB, 2)) MB)" -ForegroundColor Green
