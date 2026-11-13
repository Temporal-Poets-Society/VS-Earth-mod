param([switch]$MeasureBaseline, [switch]$FilterSurvey)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'tests/out/earth-playstyle'
New-Item -ItemType Directory -Force $output | Out-Null
# Keep dotnet/NuGet state inside the working copy, including on restricted offline machines.
$names = @('APPDATA','DOTNET_CLI_HOME','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE','DOTNET_ADD_GLOBAL_TOOLS_TO_PATH')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:APPDATA = Join-Path $output 'profile'
    $env:DOTNET_CLI_HOME = Join-Path $output 'dotnet'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $nuget = Join-Path $output 'NuGet.Config'
    [IO.File]::WriteAllText($nuget, '<configuration><packageSources><clear /></packageSources></configuration>')
    foreach ($project in @('EarthWorldOptionsTests','SpawnDistributionTests')) {
        $file = Join-Path $repo "tests/$project/$project.csproj"
        & dotnet restore $file --configfile $nuget
        if ($LASTEXITCODE) { throw "Restore failed: $project" }
        & dotnet build $file --no-restore -c Release -p:UseSharedCompilation=false
        if ($LASTEXITCODE) { throw "Build failed: $project" }
    }
    & dotnet (Join-Path $repo 'tests/EarthWorldOptionsTests/bin/Release/net8.0/EarthWorldOptionsTests.dll') $repo 2>&1 | Tee-Object (Join-Path $output 'options.log')
    if ($LASTEXITCODE) { throw 'World option checks failed' }
    $spawn = Join-Path $repo 'tests/SpawnDistributionTests/bin/Release/net8.0/SpawnDistributionTests.dll'
    if ($MeasureBaseline) {
        & dotnet $spawn $repo before 2>&1 | Tee-Object (Join-Path $output 'spawn-before.log')
        if ($LASTEXITCODE) { throw 'Baseline spawn searches failed' }
    }
    & dotnet $spawn $repo after 2>&1 | Tee-Object (Join-Path $output 'spawn-after.log')
    if ($LASTEXITCODE) { throw 'Spawn distribution regression failed' }
    if ($FilterSurvey) {
        & dotnet $spawn $repo filters 2>&1 | Tee-Object (Join-Path $output 'spawn-filter-survey.log')
        if ($LASTEXITCODE) { throw 'Filter survey failed' }
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
