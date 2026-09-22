$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot '../../artifacts/deep-time-analysis-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$logPath = Join-Path $outputDirectory 'fixture.log'
$reportPath = Join-Path $outputDirectory 'report.json'
# A szélsőséges overlay nem szennyezheti a meleg teljes Build statisztikáját.
@'
  BuildStaticBaseLayer reszletek: total=99999ms terrainBasis=0ms (reused=True) mesh=99999ms
Build() seeds+craters=0ms
Build() hydrology(hydroLevel=8, lakes=42)=300,5ms [field=55ms]
  BuildStaticBaseLayer reszletek: total=100ms terrainBasis=10ms (reused=False) mesh=60ms
Build() TELJES = 1000ms
Build() seeds+craters=0ms
  BuildStaticBaseLayer reszletek: total=80ms terrainBasis=0ms (reused=True) mesh=40ms
[A5 static profile] complete=True
[A5 static phase] name=WorldGen.StaticBase.meshUpload ms=12.500 mainThreadBytes=1024 gc0=1 gc1=0 gc2=0 heapDeltaBytes=-4096
Build() TELJES = 500ms
Build() seeds+craters=0ms
  BuildStaticBaseLayer reszletek: total=90ms terrainBasis=0ms (reused=True)
[A5 static profile] allocationCounterSupported=False complete=True
[A5 static phase] name=WorldGen.StaticBase.emit ms=25.000 mainThreadBytes=-1 gc0=0 gc1=0 gc2=0 heapDeltaBytes=4096
Build() TELJES = 700ms
Build() seeds+craters=0ms
  BuildStaticBaseLayer reszletek: total=99999ms terrainBasis=0ms (reused=True)
'@ | Set-Content -Encoding utf8 -LiteralPath $logPath
& (Join-Path $PSScriptRoot 'analyze-deep-time.ps1') -LogPath $logPath -OutputPath $reportPath
$r = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
if ($r.completeBuilds -ne 3 -or $r.incompleteBuildsExcluded -ne 1 -or $r.standaloneStaticBuildsExcluded -ne 1) {
    throw 'A teljes/overlay/befejezetlen Build szétválasztása hibás.'
}
if ($r.groups.cold.count -ne 1 -or $r.groups.warm.count -ne 2 -or $r.groups.cold.phasesMs.hydrology.mean -ne 300.5) {
    throw 'A cache-csoportosítás vagy tizedesvessző feldolgozása hibás.'
}
$s = $r.groups.warm.phasesMs.'build.total'
if ($s.mean -ne 600 -or $s.median -ne 600 -or $s.populationStdDev -ne 100 -or $s.p95NearestRank -ne 700) {
    throw 'A statisztika hibás.'
}
if ($r.groups.warm.phasesMs.'static.mesh'.count -ne 1 -or $r.groups.warm.phasesMs.'static.mesh'.mean -ne 40) {
    throw 'A hiányzó fázis nem kezelhető nullaként.'
}
$allocation = $r.builds[1].allocationPhases[0]
if ($allocation.mainThreadBytes -ne 1024 -or $allocation.heapDeltaBytes -ne -4096 -or $allocation.gc0 -ne 1) {
    throw 'Az allokáció és a nettó heap-változás feldolgozása hibás.'
}
if ($null -ne $r.builds[2].allocationPhases[0].mainThreadBytes) {
    throw 'A nem támogatott allokációszámláló nem jelenhet meg mért nullaként.'
}
Write-Output 'PASS: Build-határok, cache, tizedesvessző, statisztika, hiányzó fázis, allokáció/heap.'
