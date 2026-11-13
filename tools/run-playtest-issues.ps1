[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9._/-]+$')][string]$Baseline='28817e2')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'tests/out/playtest-issues'
New-Item -ItemType Directory -Force -Path $out | Out-Null
# Keep dotnet's configuration and build state inside this worktree; no NuGet download is needed.
$env:APPDATA=Join-Path $out 'appdata'
$env:DOTNET_CLI_HOME=Join-Path $out 'dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$config=Join-Path $out 'NuGet.Config'
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $config
$project=Join-Path $root 'tests/PlaytestIssuesTests'
dotnet restore $project --configfile $config
if($LASTEXITCODE){throw 'Offline restore failed'}
foreach($stage in @('before','after')) {
    dotnet run --no-restore -p:UseSharedCompilation=false --project $project -- $root $stage
    if($LASTEXITCODE){throw "$stage checks failed"}
}
# Compile the unmodified pre.8 core for an actual before/after species comparison, rather than approximating its picker.
$baselineRoot=Join-Path $out 'baseline-source'
New-Item -ItemType Directory -Force -Path "$baselineRoot/Core" | Out-Null
$utf8=New-Object System.Text.UTF8Encoding($false)
Push-Location $root
try {
    $files=@(git ls-tree -r --name-only $Baseline -- src/Core)
    if($LASTEXITCODE -or !$files.Count){throw 'Baseline commit is unavailable'}
    foreach($file in $files) {
        if(!$file.EndsWith('.cs')){continue}
        $content=(& git show "${Baseline}:$file") -join "`n"
        if($LASTEXITCODE){throw "Cannot read $file from baseline"}
        [IO.File]::WriteAllText((Join-Path "$baselineRoot/Core" (Split-Path -Leaf $file)),$content,$utf8)
    }
} finally {Pop-Location}
$harness=[Security.SecurityElement]::Escape((Join-Path $root 'tests/RegionalTreesTests/Harness.cs'))
$survey=[Security.SecurityElement]::Escape((Join-Path $project 'TreeSurvey.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="Core/*.cs"/><Compile Include="Main.cs"/><Compile Include="$harness"/><Compile Include="$survey"/></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath "$baselineRoot/Baseline.csproj"
@'
using EarthMap.Core;
using System.Globalization;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var data=EarthData.LoadFromFolder(args[0],true);
data.SetFineTemperature(true);data.SetFineRain(true);data.SetClimateFix(true);
TreeSurvey.Write(args[0],args[1],data);
'@ | Set-Content -LiteralPath "$baselineRoot/Main.cs"
dotnet restore "$baselineRoot/Baseline.csproj" --configfile $config
dotnet run --no-restore -p:UseSharedCompilation=false --project "$baselineRoot/Baseline.csproj" -- $root "$out/before"
if($LASTEXITCODE){throw 'Original picker survey failed'}
& (Join-Path $PSScriptRoot 'render-playtest-issues.ps1')
Write-Output "Evidence: $out (surface policy maps, not game-client renders). Baseline species: $Baseline."
