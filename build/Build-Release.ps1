<#
.SYNOPSIS
  Builds a self-contained MdViewer release, full cleanup tool, and Windows installer.

.PARAMETER Version
  Semantic version written into the assembly and installer.

.PARAMETER SkipInstaller
  Only publish + zip; do not build the setup EXE.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Project = Join-Path $Root "src\MdViewer\MdViewer.csproj"
$CleanupProject = Join-Path $Root "src\MdViewer.Cleanup\MdViewer.Cleanup.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$CleanupDir = Join-Path $Root "artifacts\cleanup"
$ZipDir = Join-Path $Root "artifacts\portable"
$InstallerDir = Join-Path $Root "artifacts\installer"
$Iss = Join-Path $Root "installer\MdViewer.iss"

if (-not $Version) {
    [xml]$csproj = Get-Content $Project
    $Version = @($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $Version) { $Version = "1.1.5" }
}

$FourPart = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

Write-Host "==> MdViewer release $Version" -ForegroundColor Cyan
Write-Host "Root: $Root"

foreach ($dir in @($PublishDir, $CleanupDir, $ZipDir, $InstallerDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Write-Host "==> Publishing self-contained win-x64 app..." -ForegroundColor Cyan
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=false `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$FourPart `
    -p:FileVersion=$FourPart `
    -p:InformationalVersion=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed" }

$exe = Join-Path $PublishDir "MdViewer.exe"
if (-not (Test-Path $exe)) { throw "MdViewer.exe missing in publish output" }

Write-Host "==> Publishing self-contained cleanup tool..." -ForegroundColor Cyan
dotnet publish $CleanupProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$FourPart `
    -p:FileVersion=$FourPart `
    -o $CleanupDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish (cleanup) failed" }

$cleanupExe = Join-Path $CleanupDir "MdViewer-Cleanup.exe"
if (-not (Test-Path $cleanupExe)) { throw "MdViewer-Cleanup.exe missing" }

# Also place cleanup next to installer artifacts for standalone use
Copy-Item $cleanupExe (Join-Path $InstallerDir "MdViewer-Cleanup.exe") -Force

$portableZip = Join-Path $ZipDir "MdViewer-$Version-win-x64.zip"
Write-Host "==> Creating portable zip: $portableZip" -ForegroundColor Cyan
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $portableZip -CompressionLevel Optimal

$setupPath = $null
if (-not $SkipInstaller) {
    Write-Host "==> Locating Inno Setup compiler..." -ForegroundColor Cyan
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        Write-Host "Inno Setup not found - installing via winget..." -ForegroundColor Yellow
        winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
        Start-Sleep -Seconds 2
        $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) {
            $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($cmd) { $iscc = $cmd.Source }
        }
    }

    if (-not $iscc) {
        Write-Warning "ISCC.exe still not found. Portable zip + cleanup tool were created; installer skipped."
    }
    else {
        Write-Host "Using $iscc" -ForegroundColor Green
        & $iscc `
            "/DMyAppVersion=$Version" `
            "/DPublishDir=$PublishDir" `
            "/DCleanupDir=$CleanupDir" `
            "/DOutputDir=$InstallerDir" `
            $Iss
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
        $setupPath = Get-ChildItem $InstallerDir -Filter "MdViewer-*-Setup.exe" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

Write-Host ""
Write-Host "Release complete." -ForegroundColor Green
Write-Host "  Portable  : $portableZip"
Write-Host "  Cleanup   : $cleanupExe"
if ($setupPath) { Write-Host "  Installer : $setupPath" }
Write-Host "  App folder: $PublishDir"
Get-Item $exe, $cleanupExe, $portableZip -ErrorAction SilentlyContinue | Format-Table Name, Length, LastWriteTime -AutoSize
if ($setupPath) { Get-Item $setupPath | Format-Table Name, Length, LastWriteTime -AutoSize }
