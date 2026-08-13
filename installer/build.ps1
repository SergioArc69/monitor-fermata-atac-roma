# Publishes MonitorFermataAtacRoma as a self-contained single-file exe, then compiles the
# Inno Setup installer. Run from anywhere; paths are resolved relative to this script.
#
# Usage:  pwsh installer\build.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$installerDir = $PSScriptRoot
$publishDir = Join-Path $root "publish"
$csproj = Join-Path $root "MonitorFermataAtacRoma.csproj"

Write-Host "==> Publishing self-contained single-file build..." -ForegroundColor Cyan
# Target the .csproj explicitly: dotnet publish on the folder picks up the .slnx solution file
# instead, which silently drops project-level publish customizations (like the WebView2Loader.dll
# copy below the [Files] section relies on).
dotnet publish "$csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o "$publishDir"

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = Get-ChildItem -Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $iscc) { throw "ISCC.exe (Inno Setup compiler) not found. Install Inno Setup 6 first (winget install JRSoftware.InnoSetup)." }

Write-Host "==> Compiling installer with $($iscc.FullName)..." -ForegroundColor Cyan
& $iscc.FullName "$installerDir\setup.iss"

if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed" }

Write-Host "==> Done. Installer is in $installerDir\output\" -ForegroundColor Green
