param([switch]$VanillaOnly, [string]$GamePath = "$env:APPDATA\Vintagestory", [string]$ModsSource)
# Starts an isolated self-test server (fresh profile under testruns\) with the Debug build of Earth and, unless
# -VanillaOnly, the content mods of the pack. The self-test log lists regional tree selections per botanical
# region, generator and tier (t1 native, t2 regional analogue, t3 climate fallback).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$label = if ($VanillaOnly) { 'vanilla' } else { 'full' }
$run = Join-Path $root ("testruns\regional-trees-$label-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $run) { throw "Test directory already exists: $run" }
New-Item -ItemType Directory -Path "$run\Mods", "$run\ModConfig", "$run\Saves" | Out-Null
Copy-Item -LiteralPath "$root\bin\Debug\mod" -Destination "$run\Mods\earthmap" -Recurse
if (!$VanillaOnly) {
    # content mods: the pinned archives (default: the game's installed Mods folder, read-only)
    if (!$ModsSource) { $ModsSource = "$env:APPDATA\VintagestoryData\Mods" }
    $pack = Get-Content -LiteralPath "$root\modpack\pack-lock.json" -Raw | ConvertFrom-Json
    foreach ($entry in $pack.mods) {
        $source = Join-Path $ModsSource $entry.filename
        if (!(Test-Path -LiteralPath $source)) { throw "Missing $($entry.filename) in $ModsSource" }
        Copy-Item -LiteralPath $source -Destination "$run\Mods"
    }
}
$server = Get-Content -LiteralPath "$root\testserver\serverconfig.json" -Raw | ConvertFrom-Json
$server.Ip = '127.0.0.1'
$server.Port = if ($VanillaOnly) {42424} else {42423}
$server.AdvertiseServer = $false
$server.Upnp = $false
$server.LastLaunchMods = $null
$server.ModPaths = @('Mods', "$run\Mods")
$server.WorldConfig.SaveFileLocation = "$run\Saves\regional.vcdbs"
$server.WorldConfig.WorldName = 'Earth regional trees test'
$server.WorldConfig.Seed = '386217'
$server.WorldConfig.WorldConfiguration = @{worldWidth='256000'; worldLength='102400'; worldEdge='blocked'; startingClimate='temperate'; gameMode='survival'}
$server | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath "$run\serverconfig.json" -Encoding utf8
$places = @('57,25','48.6,7.78','44,-73','-35.7,174','-33.8,18.7','35.5,137','-23.8,133.8','25.2,-80.6','0.5,20','-40,-73','31,-86')
@{RegionalTrees=$true;MoveStoryLocations=$false;SelfTest=@{Enabled=$true;Shutdown=$true;RadiusChunks=1;RenderWorldMap=$false;CheckRivers=$false;Places=$places}} |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$run\ModConfig\earthmap.json" -Encoding utf8
$process = Start-Process -FilePath "$GamePath\VintagestoryServer.exe" -ArgumentList @('--dataPath',('"'+$run+'"')) -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run\stdout.log" -RedirectStandardError "$run\stderr.log"
[pscustomobject]@{ProcessId=$process.Id; RunPath=$run} | ConvertTo-Json
