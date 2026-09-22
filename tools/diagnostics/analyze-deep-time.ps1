param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [string]$OutputPath,
    [ValidateSet('any', 'control', 'profile')][string]$Instrumentation = 'any',
    [ValidateSet('any', 'cold', 'warm', 'unknown')][string]$Cache = 'any',
    [ValidateRange(0, 1000000)][int]$SkipBuilds = 0,
    [ValidateRange(0, 1000000)][int]$MaxBuilds = 0
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
function Read-Number([string]$Value) {
    [double]::Parse($Value.Replace(',', '.'), $culture)
}
function Get-Statistics($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mean = ($sorted | Measure-Object -Average).Average
    $sum = 0.0
    foreach ($value in $sorted) { $sum += ($value - $mean) * ($value - $mean) }
    [ordered]@{
        count = $sorted.Count
        min = $sorted[0]
        median = if ($sorted.Count % 2) { $sorted[[int][Math]::Floor($sorted.Count / 2)] } else {
            ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
        }
        p95NearestRank = $sorted[[int][Math]::Ceiling(0.95 * $sorted.Count) - 1]
        max = $sorted[-1]
        mean = $mean
        populationStdDev = [Math]::Sqrt($sum / $sorted.Count)
    }
}

# A teljes Build kezdete/vége keretezi a mintát. Az overlay saját statikus
# rebuildje nem teljes világépítés; a hiányzó érték nem nulla.
$builds = [Collections.Generic.List[object]]::new()
$current = $null
$incomplete = 0
$standaloneStatic = 0
$lineNumber = 0
foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $LogPath).Path)) {
    $lineNumber++
    if ($line -match '^Build\(\) seeds\+craters=') {
        if ($null -ne $current) { $incomplete++ }
        $current = [ordered]@{
            startLine = $lineNumber; cache = 'unknown'; instrumentation = 'control'; phasesMs = [ordered]@{}
            profileContext = $null; allocationPhases = [Collections.Generic.List[object]]::new()
        }
    }
    if ($line -match '^\s*BuildStaticBaseLayer reszletek:') {
        if ($null -eq $current) { $standaloneStatic++; continue }
        foreach ($match in [regex]::Matches($line, '(?<key>\w+)=(?<value>\d+[.,]?\d*)ms')) {
            $current.phasesMs['static.' + $match.Groups['key'].Value] = Read-Number $match.Groups['value'].Value
        }
        $basis = [regex]::Match($line, 'terrainBasis=\S+ \(reused=(True|False)\)')
        if ($basis.Success) {
            $current.cache = if ($basis.Groups[1].Value -eq 'True') { 'warm' } else { 'cold' }
        }
    }
    if ($null -eq $current) { continue }
    if ($line -match '^Build\(\) (hydrology|ice|riverNetwork\+lakeSurface).*?=([0-9]+[.,]?[0-9]*)ms') {
        $current.phasesMs[$Matches[1]] = Read-Number $Matches[2]
    }
    if ($line -match '^\[A5 static profile\]') {
        $current.profileContext = $line
        $current.instrumentation = 'profile'
    }
    if ($line -match '^\[A5 static phase\] name=(\S+) ms=(\S+) mainThreadBytes=(-?\d+) gc0=(\d+) gc1=(\d+) gc2=(\d+) heapDeltaBytes=(-?\d+)(?: threadCpuMs=(\S+))?') {
        $current.allocationPhases.Add([ordered]@{
            name = $Matches[1]; ms = Read-Number $Matches[2]
            mainThreadBytes = if ([long]$Matches[3] -lt 0) { $null } else { [long]$Matches[3] }
            gc0 = [int]$Matches[4]; gc1 = [int]$Matches[5]; gc2 = [int]$Matches[6]
            heapDeltaBytes = [long]$Matches[7]
            threadCpuMs = if ($Matches.ContainsKey(8) -and (Read-Number $Matches[8]) -ge 0) { Read-Number $Matches[8] } else { $null }
        })
    }
    if ($line -match '^Build\(\) TELJES = ([0-9]+[.,]?[0-9]*)ms') {
        $current.phasesMs['build.total'] = Read-Number $Matches[1]
        $current['endLine'] = $lineNumber
        $builds.Add($current)
        $current = $null
    }
}
if ($null -ne $current) { $incomplete++ }
$selected = @($builds | Where-Object {
    ($Instrumentation -eq 'any' -or $_.instrumentation -eq $Instrumentation) -and
    ($Cache -eq 'any' -or $_.cache -eq $Cache)
} | Select-Object -Skip $SkipBuilds)
if ($MaxBuilds -gt 0) { $selected = @($selected | Select-Object -First $MaxBuilds) }
$groups = [ordered]@{}
foreach ($cacheGroup in @('cold', 'warm', 'unknown')) {
    $group = @($selected | Where-Object { $_.cache -eq $cacheGroup })
    $statistics = [ordered]@{}
    $keys = @($group | ForEach-Object { $_.phasesMs.Keys } | Sort-Object -Unique)
    foreach ($key in $keys) {
        $values = @($group | Where-Object { $_.phasesMs.Contains($key) } | ForEach-Object { $_.phasesMs[$key] })
        $statistics[$key] = Get-Statistics $values
    }
    $groups[$cacheGroup] = [ordered]@{ count = $group.Count; phasesMs = $statistics }
}
$report = [ordered]@{
    source = (Resolve-Path -LiteralPath $LogPath).Path
    sha256 = (Get-FileHash -LiteralPath $LogPath -Algorithm SHA256).Hash
    completeBuilds = $builds.Count
    selectedBuilds = $selected.Count
    selection = [ordered]@{ instrumentation = $Instrumentation; cache = $Cache; skipBuilds = $SkipBuilds; maxBuilds = $MaxBuilds }
    incompleteBuildsExcluded = $incomplete
    standaloneStaticBuildsExcluded = $standaloneStatic
    notes = @(
        'Cache class uses static terrainBasis reused; cold can load a disk cache.'
        'Mixed times/configurations measure workload variation too, not only runtime noise.'
        'Main-thread bytes exclude workers/native/GPU; heap delta is process-wide net memory, not allocations.'
        'GC counts are process-wide, not pause duration or proof of causality.'
        'Statistics use population standard deviation and nearest-rank p95; missing phases are omitted.'
        'Instrumentation is detected from A5 static profile headers; an external profiler without these headers cannot be detected.'
    )
    groups = $groups
    builds = $selected
}
$json = $report | ConvertTo-Json -Depth 12
if ($OutputPath) {
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
} else { $json }
