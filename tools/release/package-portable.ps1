<#
.SYNOPSIS
  Portable ZIP a Unity Windows buildből (WF-BUILD-002, ND-112).

.DESCRIPTION
  A build mappát egy "<ExecutableName>" nevű gyökérmappába csomagolja:
    WorldGen-0.1.0-alpha-win64.zip
      WorldGen\WorldGen.exe
      WorldGen\WorldGen_Data\...
  A Unity "DoNotShip" / "DontShip" mappái kimaradnak. Mellé SHA-256 fájl készül.
  A név és a verzió a tools/release/release-identity.json-ból jön.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/release/package-portable.ps1 `
    -BuildDirectory artifacts/builds/WorldGen-0.1.0-alpha-win64
#>
param(
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [string]$IdentityFile,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1-ben a $PSScriptRoot a param-alapértékekben még üres (-File indításnál), ezért itt számoljuk.
if (-not $IdentityFile) { $IdentityFile = Join-Path $PSScriptRoot 'release-identity.json' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\..\artifacts\release' }
Add-Type -AssemblyName System.IO.Compression.FileSystem

$identity = Get-Content -Raw -Encoding UTF8 -Path $IdentityFile | ConvertFrom-Json
if ($identity.format -ne 'worldgen.release-identity') { throw "Unknown release identity format: $IdentityFile" }

$buildDir = (Resolve-Path -Path $BuildDirectory).Path
$exe = Join-Path $buildDir ($identity.executableName + '.exe')
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }

$zipName = "$($identity.executableName)-$($identity.version)-win64.zip"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outDir = (Resolve-Path -Path $OutputDirectory).Path
$zipPath = Join-Path $outDir $zipName

$excluded = @('*_BurstDebugInformation_DoNotShip', '*_BackUpThisFolder_ButDontShipItInYourBuild')
$staging = Join-Path ([IO.Path]::GetTempPath()) ('worldgen-package-' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $staging $identity.executableName

try {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem -LiteralPath $buildDir -Force | Where-Object {
        $item = $_
        -not ($excluded | Where-Object { $item.PSIsContainer -and $item.Name -like $_ })
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $root -Recurse -Force
    }

    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $zipName" -Encoding ASCII -NoNewline

    $sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Host "Portable package: $zipPath ($sizeMb MB)"
    Write-Host "SHA-256: $hash"
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
