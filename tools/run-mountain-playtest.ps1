[CmdletBinding()]
param(
    [ValidateSet('A','B','C','D','E','F','G','H','I','J','K')][string]$Case='K',
    [ValidateSet(256,384,512,768)][int]$Height=512,
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunName,
    [string]$GamePath=(Join-Path $env:APPDATA 'Vintagestory'),
    [string]$CylinderZip,
    [string]$BuiltModPath,
    [string[]]$ExtraMods=@(),
    [ValidateSet('legacy','log1000','linear','log250','log500','log2000')][string]$PreferredCurve='log1000',
    [ValidateSet('normal','half','off')][string]$Detail='half',
    [int]$Port=42728,
    [switch]$Continents,
    [switch]$DisableCylinderForActivationTest,
    [switch]$Smoke,
    [switch]$Resume,
    [switch]$Play
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$BuiltModPath){
    $BuiltModPath=Join-Path $root 'bin\Release\mod'
    if(!(Test-Path -LiteralPath (Join-Path $BuiltModPath 'EarthMap.dll'))){$BuiltModPath=Join-Path $root 'bin\Debug\mod'}
}
if(!$CylinderZip){throw 'Pass -CylinderZip with the Cylinder World 0.11.11+ package to test.'}
if(!(Test-Path -LiteralPath (Join-Path $BuiltModPath 'EarthMap.dll'))){throw 'Build the mod first.'}
if(!$RunName){$RunName=('mountain-{0}-h{1}-{2}' -f $Case,$Height,(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$run=Join-Path (Join-Path $root 'testruns') $RunName
if((Test-Path -LiteralPath $run) -and !$Resume){throw "Test folder exists; choose another RunName or use -Resume: $run"}
New-Item -ItemType Directory -Force -Path "$run\Mods","$run\ModConfig","$run\Saves" | Out-Null
New-Item -ItemType Directory -Force -Path "$run\Mods\earthmap" | Out-Null
Get-ChildItem -LiteralPath $BuiltModPath | Copy-Item -Destination "$run\Mods\earthmap" -Recurse -Force
Copy-Item -LiteralPath $CylinderZip -Destination "$run\Mods" -Force
foreach($extra in $ExtraMods){Copy-Item -LiteralPath $extra -Destination "$run\Mods" -Force}
$budget=if($Case -eq 'A'){'legacy'}elseif($Case -eq 'G'){'110'}else{'128'}
# 0.17.0-pre.4 presets (Codex's pre.1/pre.2 matrix used gentle=2000, balanced=6000 and adaptive for C, D and F)
$curve=switch($Case){'A'{'legacy'} 'B'{'legacy'} 'C'{'log2000'} 'D'{'log500'} 'E'{'linear'} 'F'{'log1000'} default{$PreferredCurve}}
$detailSetting=if($Case -in @('H','I','J','K')){$Detail}else{'normal'}
$wc=@{worldWidth='512000';worldLength='204800';worldEdge='blocked';startingClimate='temperate';gameMode='creative';earthWeather='vanilla';
earthWorldSize='512000';earthSpawn='custom';earthSpawnCoordinates='35.36,138.73';earthVerticalBudget=$budget;earthHeightCurve=$curve;
earthTerrainDetail=$detailSetting;earthAlpineEcology=$(if($Case -in @('I','K')){'local'}else{'legacy'});
earthRockExposure=$(if($Case -in @('J','K')){'physical'}else{'legacy'})}
$server=@{ServerName="Earth mountain $Case h$Height";Ip='127.0.0.1';Port=$Port;Upnp=$false;AdvertiseServer=$false;MaxClients=4;
DefaultRoleCode='crplayer';Roles=@(@{Code='crplayer';Name='Playtest player';PrivilegeLevel=100;DefaultGameMode=2;Color='Green';
Privileges=@('chat','build','useblock','attackcreatures','freemove','gamemode','tp','time','weather','controlserver')});
ModPaths=@('Mods',"$run\Mods");MapSizeX=512000;MapSizeY=$Height;MapSizeZ=204800;
WorldConfig=@{WorldName="Mountain $Case h$Height";Seed='386217';SaveFileLocation="$run\Saves\mountain.vcdbs";MapSizeY=$Height;
    WorldType='standard';PlayStyle='earthmap';PlayStyleLangCode='preset-earthmap';AllowCreativeMode=$true;WorldConfiguration=$wc}}
if(!$Resume){$server | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$run\serverconfig.json" -Encoding utf8}
$places=if($Smoke){@('35.36,138.73','27.99,86.93','-15.97,-5.71')}else{@('35.36,138.73','27.99,86.93','-15.97,-5.71','36.40,25.40','9.03,38.74','56.95,24.10','jumpline','-85.5,20.0')}
if($Continents){
    $sites=Get-Content -LiteralPath (Join-Path $root 'tests\fixtures\mountain-sites.json') -Raw | ConvertFrom-Json
    $places=@($sites | ForEach-Object { $_.latitude.ToString([cultureinfo]::InvariantCulture)+','+$_.longitude.ToString([cultureinfo]::InvariantCulture) })
}
@{MoveStoryLocations=(!$Smoke);Rivers=(!$Smoke);Weather=$false;SelfTest=@{Enabled=(!$Play);Shutdown=$true;RadiusChunks=1;RenderWorldMap=$false;CheckRivers=$false;Places=$places}} |
ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$run\ModConfig\earthmap.json" -Encoding utf8
# No player profile, credentials or real save is copied. Only the named disposable test folder is written.
@{Enabled=(!$DisableCylinderForActivationTest);EarthProportions=$true;JumpInset=0;SeamHalfWidth=0;PolarClimate=$true;WallDepth=128} |
ConvertTo-Json | Set-Content -LiteralPath "$run\ModConfig\cylinderworld.json" -Encoding utf8
$process=Start-Process -FilePath (Join-Path $GamePath 'VintagestoryServer.exe') -ArgumentList @('--dataPath',('"'+$run+'"')) -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run\stdout.log" -RedirectStandardError "$run\stderr.log"
[pscustomobject]@{ProcessId=$process.Id;RunPath=$run;Case=$Case;Height=$Height;Smoke=[bool]$Smoke;Join=$(if($Play){"127.0.0.1:$Port"}else{'self-test exits automatically'})} | ConvertTo-Json
