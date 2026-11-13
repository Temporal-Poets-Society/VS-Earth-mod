[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('valid','missing-hydrology','missing-sea','incompatible-hydrology','incompatible-sea','saved-incompatible-hydrology','standard-incompatible-hydrology')][string]$Case,
    [Parameter(Mandatory)][string]$SessionRoot,
    [string]$SavedWorld,
    [string]$GamePath='C:/Users/andri/AppData/Roaming/Vintagestory',
    [string]$CylinderZip='C:/Users/andri/AppData/Roaming/VintagestoryData/Mods/cylinderworld-0.11.15-playtest.6.zip',
    [string]$HydrologyZip='C:/VS mods/Hydrology/.claude/worktrees/codex-world-options/release/hydrology-0.1.13.zip',
    [string]$SeaZip='C:/VS mods/Sea/.claude/worktrees/codex-world-options/release/sea-0.4.7-pre.12.zip',
    [int]$Port=42749
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$testRoot=[IO.Path]::GetFullPath((Join-Path $repo 'testruns'))+[IO.Path]::DirectorySeparatorChar
$session=[IO.Path]::GetFullPath($SessionRoot)
if(!$session.StartsWith($testRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'SessionRoot must be inside this checkout\testruns.'}
$run=Join-Path $session $Case
if(Test-Path -LiteralPath $run){throw "Test case already exists: $run"}
$shared=Join-Path $session 'shared'
New-Item -ItemType Directory -Force "$shared/earth","$shared/hydrology","$shared/sea","$shared/probe/observer","$shared/oldhydrology","$shared/badsea/sea","$run/ModConfig","$run/Saves" | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $repo 'modinfo.json') -Raw | ConvertFrom-Json
$earthZip=Join-Path $repo "release/earth-$($manifest.version).zip"
function Copy-Once([string]$from,[string]$to){if(!(Test-Path -LiteralPath $to)){Copy-Item -LiteralPath $from -Destination $to}}
Copy-Once $earthZip "$shared/earth/earth.zip"
foreach($id in @('earthterraindata','earthwaterdata')){
    $version=$manifest.dependencies.$id
    if($version){Copy-Once (Join-Path $repo "release/$id-$version.zip") "$shared/earth/$id.zip"}
}
Copy-Once $CylinderZip "$shared/earth/cylinderworld.zip"
Copy-Once $HydrologyZip "$shared/hydrology/hydrology.zip"
Copy-Once $SeaZip "$shared/sea/sea.zip"
Copy-Once (Join-Path $repo 'tests/RequiredModsServerProbe/bin/Release/net8.0/RequiredModsServerProbe.dll') "$shared/probe/observer/RequiredModsServerProbe.dll"
Copy-Once (Join-Path $repo 'tests/RequiredModsServerProbe/modinfo.json') "$shared/probe/observer/modinfo.json"
if(!(Test-Path -LiteralPath "$shared/oldhydrology/hydrology.zip")){
    # This test-only copy deliberately advertises a newer version while retaining the old query API.
    Copy-Item -LiteralPath 'C:/VS mods/Hydrology/release/hydrology-0.1.9.zip' -Destination "$shared/oldhydrology/hydrology.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::Open("$shared/oldhydrology/hydrology.zip",[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.GetEntry('modinfo.json');$reader=[IO.StreamReader]::new($entry.Open())
        try{$info=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
        $info.version='0.1.10';$entry.Delete();$writer=[IO.StreamWriter]::new($zip.CreateEntry('modinfo.json').Open())
        try{$writer.Write(($info|ConvertTo-Json -Depth 10))}finally{$writer.Dispose()}
    }finally{$zip.Dispose()}
}
if(!(Test-Path -LiteralPath "$shared/badsea/sea/modinfo.json")){
    @{type='content';modid='sea';name='TEST ONLY incompatible Sea';version='0.4.0';side='Universal';dependencies=@{game='1.22.0'}} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$shared/badsea/sea/modinfo.json" -Encoding utf8
}
$paths=@('Mods',"$shared/earth","$shared/probe")
if($Case -ne 'missing-hydrology'){$paths+=if($Case -match 'incompatible-hydrology'){"$shared/oldhydrology"}else{"$shared/hydrology"}}
if($Case -ne 'missing-sea'){$paths+=if($Case -eq 'incompatible-sea'){"$shared/badsea"}else{"$shared/sea"}}
$save="$run/Saves/acceptance.vcdbs"
if($Case -eq 'saved-incompatible-hydrology'){
    if(!$SavedWorld){throw 'Pass the disposable valid case save to SavedWorld.'}
    Copy-Item -LiteralPath $SavedWorld -Destination $save
}
$standard=$Case -eq 'standard-incompatible-hydrology'
$wc=@{worldWidth='256000';worldLength='102400';startingClimate='temperate';gameMode='creative';earthWorldSize='256000';
    earthSpawn='riga';earthVerticalBudget='110';earthHeightCurve='log1000';earthWeather='earth'}
$config=@{ServerName="Earth required mods $Case";Ip='127.0.0.1';Port=$Port;Upnp=$false;AdvertiseServer=$false;MaxClients=1;
    DefaultRoleCode='crplayer';Roles=@(@{Code='crplayer';Name='Test player';PrivilegeLevel=100;DefaultGameMode=2;Color='Green';Privileges=@('chat','build','useblock','controlserver')});
    ModPaths=$paths;MapSizeX=256000;MapSizeY=512;MapSizeZ=102400;
    WorldConfig=@{WorldName="Required mods $Case";Seed='386217';SaveFileLocation=$save;MapSizeY=512;
        WorldType='standard';PlayStyle=$(if($standard){'surviveandbuild'}else{'earth'});WorldConfiguration=$wc}}
$config|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$run/serverconfig.json" -Encoding utf8
@{Enabled=($Case -ne 'saved-incompatible-hydrology');MoveStoryLocations=$false;SelfTest=@{Enabled=$false}}|
    ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$run/ModConfig/earth.json" -Encoding utf8
@{Enabled=$true;EarthProportions=$true;PolarClimate=$true;PolarWalls=$true;WallDepth=128}|
    ConvertTo-Json|Set-Content -LiteralPath "$run/ModConfig/cylinderworld.json" -Encoding utf8
$process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('VintagestoryServer.dll','--dataPath',('"'+$run+'"')) -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/stdout.log" -RedirectStandardError "$run/stderr.log"
[pscustomobject]@{ProcessId=$process.Id;RunPath=$run;Save=$save;Case=$Case}|ConvertTo-Json
$process.WaitForExit()
Write-Output 'Server exited.'
