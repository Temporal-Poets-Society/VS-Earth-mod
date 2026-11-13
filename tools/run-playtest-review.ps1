param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'tests/out/playtest-issues'
New-Item -ItemType Directory -Force $out|Out-Null
$env:APPDATA=Join-Path $out 'appdata';$env:DOTNET_CLI_HOME=Join-Path $out 'dotnet'
$config=Join-Path $out 'NuGet.Config'
'<configuration><packageSources><clear /></packageSources></configuration>'|Set-Content $config
$project=Join-Path $root 'tests/PlaytestReviewTests'
dotnet restore $project --configfile $config
if($LASTEXITCODE){throw 'Offline review restore failed'}
dotnet run --no-restore -p:UseSharedCompilation=false --project $project -- $root
if($LASTEXITCODE){throw 'Review checks failed; inspect generated tables before acceptance'}
& (Join-Path $PSScriptRoot 'render-playtest-review.ps1')
