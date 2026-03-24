# Builds Release and zips Dalamud payload (Felix.dll + Felix.json) for custom repo / dashboard hosting.
# Requires a normal Dalamud dev environment (Dalamud.NET.Sdk can resolve XIVLauncher hooks).
# Usage (from repo root or this folder):
#   .\felix-plugin\scripts\Package-FelixPlugin.ps1
# Optional: copy zip to dashboard static files:
#   .\felix-plugin\scripts\Package-FelixPlugin.ps1 -CopyZipToDashboard "..\cap-bot\cap\felixthebot-dashboard\public\downloads\dalamud\Felix\latest.zip"

param(
    [string] $CopyZipToDashboard = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "Felix\Felix.csproj"
$bin = Join-Path $root "bin"
$dist = Join-Path $root "dist"

if (-not (Test-Path $csproj)) {
    Write-Error "Felix.csproj not found at $csproj"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "dotnet build Release -> $bin"
dotnet build $csproj -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $bin "Felix.dll"
$json = Join-Path $bin "Felix.json"
if (-not (Test-Path $dll)) { Write-Error "Missing $dll after build." }
if (-not (Test-Path $json)) { Write-Error "Missing $json after build." }

$zipLocal = Join-Path $dist "Felix-latest.zip"
if (Test-Path $zipLocal) { Remove-Item $zipLocal -Force }
Compress-Archive -LiteralPath $dll, $json -DestinationPath $zipLocal -CompressionLevel Optimal
Write-Host "Wrote $zipLocal"

$manifest = Get-Content $json -Raw | ConvertFrom-Json
Write-Host ("AssemblyVersion: {0}  DalamudApiLevel: {1}" -f $manifest.AssemblyVersion, $manifest.DalamudApiLevel)
Write-Host "Set dashboard env FELIX_PLUGIN_ASSEMBLY_VERSION=$($manifest.AssemblyVersion) if it differs from server default."

if ($CopyZipToDashboard) {
    $destDir = Split-Path -Parent $CopyZipToDashboard
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }
    Copy-Item -LiteralPath $zipLocal -Destination $CopyZipToDashboard -Force
    Write-Host "Copied zip -> $CopyZipToDashboard"
}
