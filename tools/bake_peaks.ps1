param([Parameter(Mandatory=$true)][string]$InputFile,[string]$OutputFile)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $OutputFile){$OutputFile=Join-Path $root 'earthdata/peaks.json'}
$target=[IO.Path]::GetFullPath($OutputFile)
if(-not $target.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay inside this working copy'}
$out=Join-Path $root 'tests/out/peak-bake';New-Item -ItemType Directory -Force $out|Out-Null
$nuget=Join-Path $out 'NuGet.Config';'<configuration><packageSources><clear /></packageSources></configuration>'|Set-Content $nuget
$oldAppData=$env:APPDATA;$oldCli=$env:DOTNET_CLI_HOME
try {
    $env:APPDATA=Join-Path $out 'appdata';$env:DOTNET_CLI_HOME=Join-Path $out 'dotnet'
    dotnet restore (Join-Path $PSScriptRoot 'PeakBake/PeakBake.csproj') --configfile $nuget
    if($LASTEXITCODE){throw 'Offline restore failed'}
    dotnet run --no-restore -p:UseSharedCompilation=false --project (Join-Path $PSScriptRoot 'PeakBake/PeakBake.csproj') -- ([IO.Path]::GetFullPath($InputFile)) $target
    if($LASTEXITCODE){throw 'Peak bake failed'}
} finally {$env:APPDATA=$oldAppData;$env:DOTNET_CLI_HOME=$oldCli}
