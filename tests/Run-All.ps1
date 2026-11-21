<#
.SYNOPSIS
Runs Earth's offline tests (EA-0088, code review 14 November 2026): every tests\*Tests project from one table, one log
each in a folder under C:\VS mods\work (or -Out), and a summary of pass, fail or skip with the time each took.

.DESCRIPTION
Builds EarthMap.csproj once first (RequiredModsTests links its DLL), packing the zip into the run's own folder (never
over release\; EarthWorldOptionsTests checks that zip through EARTH_RELEASE_DIR). Each project gets the arguments it takes (most the
repo root; EarthMapTests the root and 384; RiverIndexTests a report path; ElevationTests GLO-90 sample tiles from
rawdata). A project whose needs are missing on this machine is skipped and said so, not failed: the game (for its DLLs),
the raw data (rawdata or EARTH_RAW), the ore research outputs (EARTH_RESEARCH or C:\VS mods\data\earth-research).
ElevationTests also gets the GLO-90 store where this PC has one (EARTH_STORE's glo90, else the player's or the AgentBridge
bench's ModData\earth\glo90; read only): its release-file fetch and "download all" stream with resume (FetchFile,
StreamPart) run only with it. That row runs on the game's .NET 10 (DOTNET_ROLL_FORWARD=LatestMajor), where the tiles'
checks differ from .NET 8's if anything does (Fable's re-review, 14 November 2026; EA-0088, EA-0106).
-Quick leaves out the slow ones (over two minutes each); -Only runs the named projects; -Python adds the ore Python
checks (the repo's .venv). A project not in the table fails the run, so a new one gets a line here.
The whole run takes about 35 minutes on this PC; -Quick about 4.

.EXAMPLE
pwsh tests\Run-All.ps1 -Quick
powershell -File tests\Run-All.ps1 -Only EarthWorldOptionsTests,VolcanoTests
#>
param(
    [switch]$Quick,
    [string[]]$Only,
    [switch]$Python,
    [switch]$NoBuild,
    [string]$Out,
    [string]$VintageStoryPath = $(if ($env:VINTAGE_STORY) { $env:VINTAGE_STORY } else { Join-Path $env:APPDATA 'Vintagestory' })
)
$ErrorActionPreference = 'Stop'
# (-Only A,B through powershell -File arrives as one string)
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$raw = if ($env:EARTH_RAW) { $env:EARTH_RAW } else { Join-Path $root 'rawdata' }
$research = if ($env:EARTH_RESEARCH) { $env:EARTH_RESEARCH } else { 'C:\VS mods\data\earth-research' }
# the GLO-90 store ElevationTests reads with --store (41 tiles or more), or none
$glo90 = @($(if ($env:EARTH_STORE) { Join-Path $env:EARTH_STORE 'glo90' }),
    (Join-Path $env:APPDATA 'VintagestoryData\ModData\earth\glo90'),
    (Join-Path $env:APPDATA 'VintagestoryData-agentbridge\ModData\earth\glo90')) |
    Where-Object { $_ -and (Test-Path $_) -and @(Get-ChildItem $_ -Filter '*.egl' -ErrorAction SilentlyContinue | Select-Object -First 41).Count -ge 41 } |
    Select-Object -First 1
if (-not $Out) { $Out = Join-Path 'C:\VS mods\work' ('earth-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $Out | Out-Null

# Project, arguments ({root}, {out}, {raw}), needs (game, raw), minutes on this PC (14 November 2026), slow (left out by -Quick);
# Store: --store={the GLO-90 store} added where there is one; Game: run on the game's .NET (DOTNET_ROLL_FORWARD=LatestMajor)
$table = @(
    @{ P = 'ClimateDataTests';        A = @('{root}');                 N = @();        M = 0.3 }
    @{ P = 'SeasonTests';             A = @('{root}');                         N = @();        M = 0.1 }
    @{ P = 'ColumnStoreTests';        A = @('{root}');                 N = @();        M = 0.2 }
    @{ P = 'DataReadersTests';        A = @('{root}');                 N = @();        M = 0.3 }
    @{ P = 'EarthMapTests';           A = @('{root}', '384');          N = @();        M = 3.5; Slow = $true }
    @{ P = 'EarthWorldOptionsTests';  A = @('{root}');                 N = @('game');  M = 0.2 }
    @{ P = 'ElevationTests';          A = @('{raw}\copernicus-glo90-sample', '{root}\data\glo90-tiles.bin', '{out}\elevation-work'); N = @('raw'); M = 1.1; Store = $true; Game = $true }
    @{ P = 'FloodplainTests';         A = @('{root}');                 N = @();        M = 0.2 }
    @{ P = 'GrassShaderCheck';        A = @();                         N = @('game');  M = 0.1 }
    @{ P = 'GroundColumnsTests';      A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'GroundDownloadTests';     A = @();                         N = @();        M = 1 }
    @{ P = 'ModelCacheTests';         A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'OreDepositsTests';        A = @();                         N = @();        M = 0.1 }
    @{ P = 'OreLayersTests';          A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'PatchVanillaTests';       A = @('{root}');                 N = @('game');  M = 0.1 }
    @{ P = 'PlacementTests';          A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'PlaytestIssuesTests';     A = @('{root}');                 N = @();        M = 0.2 }
    @{ P = 'PlaytestReviewTests';     A = @('{root}');                 N = @();        M = 0.3 }
    @{ P = 'ReflectionContractTests'; A = @('{root}');                 N = @('game');  M = 0.1 }
    @{ P = 'RegionalFaunaTests';      A = @('{root}');                 N = @();        M = 0.4 }
    @{ P = 'RegionalGeologyTests';    A = @('{root}');                 N = @();        M = 4.8; Slow = $true }
    @{ P = 'RegionalOresTests';       A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'RegionalPlantsTests';     A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'RegionalTreesTests';      A = @('{root}');                 N = @();        M = 0.2 }
    @{ P = 'RequiredModsTests';       A = @('{root}');                 N = @('game');  M = 0.1 }
    @{ P = 'RiverIndexTests';         A = @('{out}\riverindex-report.txt'); N = @();   M = 0.2 }
    @{ P = 'RiverWaterTests';         A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'SavedWorldDigestTests';   A = @('{root}');                 N = @('game');  M = 1.0 }
    @{ P = 'SeasonDataTests';         A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'SweepMemoryTests';        A = @('{root}');                 N = @('game');  M = 3.5; Slow = $true }
    @{ P = 'ServerToolsTests';        A = @();                         N = @();        M = 0.2 }
    @{ P = 'SiteDriversTests';        A = @();                         N = @();        M = 0.1 }
    @{ P = 'SkyLongitudeTests';       A = @('{root}');                 N = @('game');  M = 0.1 }
    @{ P = 'SoilTests';               A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'SpawnDistributionTests';  A = @('{root}');                 N = @();        M = 12.5; Slow = $true }
    @{ P = 'StreamsTests';            A = @('{root}');                 N = @('game');  M = 0.1 }
    @{ P = 'TerrainExpansionTests';   A = @('{root}');                 N = @();        M = 0.3 }
    @{ P = 'TerrainRulesTests';       A = @();                         N = @();        M = 0.1 }
    @{ P = 'VerticalProfileTests';    A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'VolcanoTests';            A = @('{root}');                 N = @();        M = 0.5 }
    @{ P = 'WaterTableTests';         A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'WeatherContractTests';    A = @('{root}');                 N = @();        M = 0.2 }
    @{ P = 'WeatherTests';            A = @('{root}');                 N = @();        M = 7.5; Slow = $true }
    @{ P = 'WetlandTests';            A = @('{root}');                 N = @();        M = 0.1 }
    @{ P = 'ZoneTests';               A = @('{root}');                 N = @();        M = 0.2 }
)
# folders under tests\ that are not test projects of their own (DataReleaseCheck asks the data releases over the network,
# EA-0191: run by hand before a release, Earth's AGENTS.md)
$notProjects = @('fixtures', 'out', 'RawElevationSurvey', 'RequiredModsServerProbe', 'TerrainHandlerProbe', 'Earth.Core.Tests.Common', 'DataReleaseCheck')

$listed = $table | ForEach-Object { $_.P }
$folders = Get-ChildItem (Join-Path $root 'tests') -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } | ForEach-Object Name
$unlisted = $folders | Where-Object { $_ -notin $listed -and $_ -notin $notProjects }
$gone = $listed | Where-Object { $_ -notin $folders }

function Has([string]$need) {
    switch ($need) {
        'game' { return Test-Path (Join-Path $VintageStoryPath 'VintagestoryAPI.dll') }
        'raw' { return Test-Path (Join-Path $raw 'copernicus-glo90-sample') }
        default { return $false }
    }
}

$results = New-Object System.Collections.Generic.List[object]
# (Windows PowerShell 5.1 makes a native command's stderr lines errors; under Stop they would end the run)
$ErrorActionPreference = 'Continue'
if (-not $NoBuild) {
    $t0 = Get-Date
    $log = Join-Path $Out 'EarthMap-build.txt'
    # (FP-19) packed into this run's own folder, never over release\ (whose zip of this version may be the published one)
    $releaseDir = Join-Path $Out 'release'
    & dotnet build (Join-Path $root 'EarthMap.csproj') -c Release "-p:ReleaseDir=$releaseDir" "-p:VintageStoryPath=$VintageStoryPath" 2>&1 | Out-File -FilePath $log -Encoding utf8
    $env:EARTH_RELEASE_DIR = $releaseDir
    $ok = $LASTEXITCODE -eq 0
    $results.Add([pscustomobject]@{ Test = 'EarthMap build'; Result = $(if ($ok) { 'pass' } else { 'FAIL' }); Seconds = [int]((Get-Date) - $t0).TotalSeconds; Note = $log })
    if (-not $ok) { Write-Host "The build failed: $log"; $results | Format-Table -AutoSize; exit 1 }
}

foreach ($t in $table) {
    if ($Only -and $t.P -notin $Only) { continue }
    if ($Quick -and $t.Slow) { $results.Add([pscustomobject]@{ Test = $t.P; Result = 'skip'; Seconds = 0; Note = "-Quick (about $($t.M) min)" }); continue }
    $missing = @($t.N | Where-Object { -not (Has $_) })
    if ($missing.Count -gt 0) { $results.Add([pscustomobject]@{ Test = $t.P; Result = 'skip'; Seconds = 0; Note = 'needs ' + ($missing -join ', ') }); continue }
    $arguments = @($t.A | ForEach-Object { $_.Replace('{root}', $root).Replace('{out}', $Out).Replace('{raw}', $raw) })
    if ($t.Store -and $glo90) { $arguments += "--store=$glo90" }
    $log = Join-Path $Out "$($t.P).txt"
    $t0 = Get-Date
    Write-Host ("{0,-24} ..." -f $t.P) -NoNewline
    $rollForward = $env:DOTNET_ROLL_FORWARD
    if ($t.Game) { $env:DOTNET_ROLL_FORWARD = 'LatestMajor' }
    & dotnet run -c Release --project (Join-Path $root "tests\$($t.P)") "-p:VintageStoryPath=$VintageStoryPath" -- @arguments 2>&1 | Out-File -FilePath $log -Encoding utf8
    $code = $LASTEXITCODE
    $env:DOTNET_ROLL_FORWARD = $rollForward
    $s = [int]((Get-Date) - $t0).TotalSeconds
    $last = (Get-Content $log -Tail 1 -ErrorAction SilentlyContinue)
    # exit 3: passed what it could check, skipped the rest for missing data (SavedWorldDigestTests without the data store)
    $result = if ($code -eq 0) { 'pass' } elseif ($code -eq 3) { 'pass, part skipped' } else { "FAIL ($code)" }
    Write-Host (" {0} ({1} s)" -f $result, $s)
    if ($t.Store) { $last = $(if ($glo90) { '(with the store) ' } else { '(no GLO-90 store here: FetchFile and StreamPart untested) ' }) + $last }
    $results.Add([pscustomobject]@{ Test = $t.P; Result = $result; Seconds = $s; Note = $(if ($code -eq 0 -or $code -eq 3) { $last } else { $log }) })
}

if ($Python -and -not $Only) {
    $py = Join-Path $root '.venv\Scripts\python.exe'
    foreach ($script in Get-ChildItem (Join-Path $root 'tests\RegionalOresTests') -Filter 'test_*.py') {
        $name = 'py ' + $script.Name
        if (-not (Test-Path $py)) { $results.Add([pscustomobject]@{ Test = $name; Result = 'skip'; Seconds = 0; Note = 'needs .venv' }); continue }
        $log = Join-Path $Out ($script.BaseName + '.txt')
        $t0 = Get-Date
        $env:EARTH_RESEARCH = $research
        & $py -I $script.FullName 2>&1 | Out-File -FilePath $log -Encoding utf8
        $code = $LASTEXITCODE
        $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($code -eq 0) { 'pass' } else { "FAIL ($code)" }); Seconds = [int]((Get-Date) - $t0).TotalSeconds; Note = $log })
    }
}

foreach ($u in $unlisted) { $results.Add([pscustomobject]@{ Test = $u; Result = 'FAIL'; Seconds = 0; Note = 'a test project not in tests\Run-All.ps1''s table: add its line' }) }
foreach ($g in $gone) { if (-not $Only) { $results.Add([pscustomobject]@{ Test = $g; Result = 'FAIL'; Seconds = 0; Note = 'in the table but not under tests\' }) } }

$results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Tee-Object -FilePath (Join-Path $Out 'summary.txt') | Write-Host
$failed = @($results | Where-Object { $_.Result -like 'FAIL*' }).Count
Write-Host ("{0} run, {1} failed, {2} skipped; logs in {3}" -f @($results | Where-Object { $_.Result -ne 'skip' }).Count, $failed, @($results | Where-Object { $_.Result -eq 'skip' }).Count, $Out)
exit $(if ($failed -gt 0) { 1 } else { 0 })
