param(
    [ValidateSet('vanilla','full')][string]$Providers = 'vanilla',
    [switch]$NoExtinct,
    [switch]$LegacyRules,
    [string]$ExistingTestSave,
    [string]$BuiltModPath,
    [string]$OreSettingsFile,
    [switch]$UpgradeOreRules,
    [switch]$UpgradeGeologyRules,
    [int]$RadiusChunks = 1,
    [string]$GamePath = "$env:APPDATA\Vintagestory",
    [string]$Seed = '386217',
    [int]$Port = 0,
    [string[]]$Places = @('57,25','48.6,7.8','64.5,26','44,-73','37,-111','9,-83','-6.5,-68.9',
        '-1,36','-33.8,18.7','-19,47','27,85','35.5,137','2,113','-23.8,133.8','-35.7,174','25.2,-80.6','65,-168','-67,-65')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$label = $Providers + $(if ($NoExtinct) {'-living'} else {'-historical'}) + $(if ($LegacyRules) {'-legacy'} else {''})
$run = Join-Path $root ("testruns\regional-world-$label-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $run) { throw "Test directory already exists: $run" }
if (!$BuiltModPath) { $BuiltModPath = Join-Path $root 'bin\Debug\mod' }
if (!(Test-Path -LiteralPath (Join-Path $BuiltModPath 'EarthMap.dll'))) { throw 'Build Earth in Debug before starting a test.' }
if (!(Test-Path -LiteralPath "$GamePath\VintagestoryServer.exe")) { throw 'Vintage Story server executable not found.' }
if ($ExistingTestSave) {
    # This helper copies only isolated test saves; it never runs against a player save in place.
    $sourceSave = (Resolve-Path -LiteralPath $ExistingTestSave).Path
    $testRoot = [IO.Path]::GetFullPath((Join-Path $root 'testruns')) + [IO.Path]::DirectorySeparatorChar
    if (!$sourceSave.StartsWith($testRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'ExistingTestSave must be inside this workspace testruns directory.' }
}
New-Item -ItemType Directory -Path "$run\Mods", "$run\ModConfig", "$run\Saves" | Out-Null
Copy-Item -LiteralPath $BuiltModPath -Destination "$run\Mods\earthmap" -Recurse
if ($Providers -eq 'full') {
    $inventory = Get-Content -LiteralPath "$root\docs\research\ecology-2026-09-28\release-inventory.json" -Raw | ConvertFrom-Json
    foreach ($entry in $inventory) {
        Copy-Item -LiteralPath (Join-Path "$root\docs\research\ecology-2026-09-28" ($entry.id + '.zip')) -Destination "$run\Mods"
    }
    Copy-Item -LiteralPath "$root\docs\research\regions-2026-09-29\wildcrafttree.zip", "$root\obj\ecology-inspect\herbarium.zip", "$root\docs\research\regions-2026-09-29\geoaddons.zip" -Destination "$run\Mods"
}
if ($ExistingTestSave) { Copy-Item -LiteralPath $sourceSave -Destination "$run\Saves\regional.vcdbs" }
$server = Get-Content -LiteralPath "$root\testserver\serverconfig.json" -Raw | ConvertFrom-Json
$server.Ip = '127.0.0.1'
$server.Port = if ($Port -gt 0) {$Port} elseif ($Providers -eq 'vanilla') {42425} else {42426}
$server.AdvertiseServer = $false
$server.Upnp = $false
$server.LastLaunchMods = $null
$server.ModPaths = @('Mods', "$run\Mods")
$server.WorldConfig.SaveFileLocation = "$run\Saves\regional.vcdbs"
$server.WorldConfig.WorldName = 'Earth regional geology and fauna test'
$server.WorldConfig.Seed = $Seed
$server.WorldConfig.WorldConfiguration = @{worldWidth='256000';worldLength='102400';worldEdge='blocked';startingClimate='temperate';gameMode='survival'}
$server | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath "$run\serverconfig.json" -Encoding utf8
$earthSettings = @{
    RegionalTrees=$true; RegionalGeology=(!$LegacyRules); RegionalFauna=(!$LegacyRules); IncludeExtinctAnimals=(!$NoExtinct)
    UpgradeExistingWorldTreeRules=$false; UpgradeExistingWorldGeologyRules=[bool]$UpgradeGeologyRules; UpgradeExistingWorldFaunaRules=$false
    RegionalOres=(!$LegacyRules); UpgradeExistingWorldOreRules=[bool]$UpgradeOreRules
    MoveStoryLocations=$false
    SelfTest=@{Enabled=$true;Shutdown=$true;RadiusChunks=$RadiusChunks;RenderWorldMap=$false;CheckRivers=$false;Places=$Places}
}
if ($OreSettingsFile) { $earthSettings.Ores = Get-Content -LiteralPath $OreSettingsFile -Raw | ConvertFrom-Json }
$earthSettings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$run\ModConfig\earthmap.json" -Encoding utf8
$process = Start-Process -FilePath "$GamePath\VintagestoryServer.exe" -ArgumentList @('--dataPath',('"'+$run+'"')) -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run\stdout.log" -RedirectStandardError "$run\stderr.log"
[pscustomobject]@{ProcessId=$process.Id;RunPath=$run;Providers=$Providers;ExistingSaveCopied=[bool]$ExistingTestSave} | ConvertTo-Json
