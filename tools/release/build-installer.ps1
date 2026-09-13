<#
.SYNOPSIS
  A Windows telepítő fordítása Inno Setup 6-tal (WF-INSTALL-001, ND-112).

.DESCRIPTION
  A release-identity.json-ból adja át a nevet, a cégnevet, a verziót és a
  numerikus fájlverziót a WorldGen.iss-nek. Az ISCC.exe helye: -IsccPath,
  vagy az alapértelmezett telepítési helyek.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/release/build-installer.ps1 -BuildNumber 12
#>
param(
    [int]$BuildNumber = 0,
    [string]$BuildDirectory,
    [string]$IdentityFile,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1-ben a $PSScriptRoot a param-alapértékekben még üres (-File indításnál), ezért itt számoljuk.
if (-not $IdentityFile) { $IdentityFile = Join-Path $PSScriptRoot 'release-identity.json' }

$identity = Get-Content -Raw -Encoding UTF8 -Path $IdentityFile | ConvertFrom-Json
if ($identity.format -ne 'worldgen.release-identity') { throw "Unknown release identity format: $IdentityFile" }
if ($BuildNumber -lt 0 -or $BuildNumber -gt 65535) { throw "BuildNumber must be between 0 and 65535." }

# SemVer "0.1.0-alpha+x" -> numerikus "0.1.0.<build>" (Windows VERSIONINFO)
if ($identity.version -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw "Invalid version: $($identity.version)" }
$numeric = "$($Matches[1]).$($Matches[2]).$($Matches[3]).$BuildNumber"

if (-not $BuildDirectory) {
    $BuildDirectory = Join-Path $PSScriptRoot "..\..\artifacts\builds\$($identity.executableName)-$($identity.version)-win64"
}
$buildDir = (Resolve-Path -Path $BuildDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $buildDir "$($identity.executableName).exe"))) { throw "Build not found: $buildDir" }

if (-not $IsccPath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $IsccPath) { throw "Inno Setup 6 (ISCC.exe) not found. Install it or pass -IsccPath." }

$outDir = Join-Path $PSScriptRoot '..\..\artifacts\release'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $IsccPath `
    "/DAppName=$($identity.productName)" `
    "/DCompanyName=$($identity.companyName)" `
    "/DExeName=$($identity.executableName)" `
    "/DAppVersion=$($identity.version)" `
    "/DNumericVersion=$numeric" `
    "/DBuildDir=$buildDir" `
    "/DOutputDir=$((Resolve-Path $outDir).Path)" `
    (Join-Path $PSScriptRoot 'WorldGen.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
