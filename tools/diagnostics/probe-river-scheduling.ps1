param(
    [ValidateSet('baseline', 'old', 'dedicated')]
    [string]$Mode = 'dedicated',
    [string]$UnityDataPath = 'C:/Program Files/Unity/Hub/Editor/6000.0.77f1/Editor/Data'
)

$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $repoPath 'artifacts/river-scheduling-probe'
$monoRoot = Join-Path $UnityDataPath 'MonoBleedingEdge'
$referencePath = Join-Path $monoRoot 'lib/mono/4.8-api'
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = & $dotnetPath --version
if ($LASTEXITCODE -ne 0) { throw 'A .NET SDK verzioja nem kerdezheto le.' }
$compilerPath = Join-Path (Split-Path $dotnetPath -Parent) "sdk/$sdkVersion/Roslyn/bincore/csc.dll"
$coreProject = Join-Path $repoPath 'src/WorldGen.Core/WorldGen.Core.csproj'
$coreAssembly = Join-Path $repoPath 'artifacts/bin/WorldGen.Core/Debug/netstandard2.1/WorldGen.Core.dll'

& $dotnetPath build $coreProject --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'A Core forditasa sikertelen.' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$probePath = Join-Path $outputPath 'Probe.exe'
$compilerArguments = @(
    $compilerPath, '/nologo', '/target:exe', '/langversion:9', '/optimize+', '/nostdlib+',
    "/out:$probePath",
    "/reference:$referencePath/mscorlib.dll",
    "/reference:$referencePath/System.dll",
    "/reference:$referencePath/System.Core.dll",
    "/reference:$referencePath/Facades/netstandard.dll",
    "/reference:$coreAssembly",
    (Join-Path $PSScriptRoot 'ProbeRiverScheduling.cs')
)
& $dotnetPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Az utemezesi proba forditasa sikertelen.' }
Copy-Item -LiteralPath $coreAssembly -Destination (Join-Path $outputPath 'WorldGen.Core.dll')
& (Join-Path $monoRoot 'bin/mono.exe') $probePath $Mode
if ($LASTEXITCODE -ne 0) { throw 'Az utemezesi proba futasa sikertelen.' }
