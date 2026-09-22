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
[A5 static phase] name=WorldGen.StaticBase.meshUpload ms=12.500 mainThreadBytes=1024 gc0=1 gc1=0 gc2=0 heapDeltaBytes=-4096 threadCpuMs=0.000
[A5 static phase] name=WorldGen.StaticBase.legacy ms=1.000 mainThreadBytes=0 gc0=0 gc1=0 gc2=0 heapDeltaBytes=0
Build() TELJES = 500ms
Build() seeds+craters=0ms
  BuildStaticBaseLayer reszletek: total=90ms terrainBasis=0ms (reused=True)
[A5 static profile] allocationCounterSupported=False complete=True
[A5 static phase] name=WorldGen.StaticBase.emit ms=25.000 mainThreadBytes=-1 gc0=0 gc1=0 gc2=0 heapDeltaBytes=4096 threadCpuMs=-1.000
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
if ($allocation.threadCpuMs -ne 0 -or $null -ne $r.builds[2].allocationPhases[0].threadCpuMs -or $null -ne $r.builds[1].allocationPhases[1].threadCpuMs) {
    throw 'A mért nulla CPU-idő és a hiányzó/nem támogatott CPU-idő eltérő adat.'
}
& (Join-Path $PSScriptRoot 'analyze-deep-time.ps1') -LogPath $logPath -OutputPath $reportPath -Cache warm -Instrumentation profile -SkipBuilds 1 -MaxBuilds 1
$filtered = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
if ($filtered.selectedBuilds -ne 1 -or $filtered.groups.warm.phasesMs.'build.total'.mean -ne 700 -or $filtered.groups.cold.count -ne 0) {
    throw 'A kontroll/profil szétválasztása vagy a bemelegítések kihagyása hibás.'
}
Write-Output 'PASS: Build-határok, cache, tizedesvessző, statisztika, hiányzó fázis, allokáció/heap.'
