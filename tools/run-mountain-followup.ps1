[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CylinderZip,
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$BatchPrefix='mountain-followup',
    [string]$GamePath=(Join-Path $env:APPDATA 'Vintagestory')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path (Join-Path $root 'testruns') $BatchPrefix
if(Test-Path -LiteralPath $folder){throw 'Follow-up folder exists; preserve it and choose a new BatchPrefix.'}
New-Item -ItemType Directory -Path $folder | Out-Null
function Progress([string]$stage,[string]$state){
    @{Stage=$stage;State=$state;UpdatedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'progress.json') -Encoding utf8
}
try{
    Progress 'Fresh performance repetitions' 'running'
    $performanceName="$BatchPrefix-performance"
    & "$PSScriptRoot\run-mountain-matrix.ps1" -CylinderZip $CylinderZip -GamePath $GamePath -Cases @('A','B') -Repeats 3 -Detail normal -BatchName $performanceName -Port 42741
    Progress 'Reload measurements' 'running'
    $performanceFolder=Join-Path (Join-Path $root 'testruns') $performanceName
    $fresh=Get-Content -LiteralPath (Join-Path $performanceFolder 'results.json') -Raw | ConvertFrom-Json
    $reloads=@()
    foreach($sample in $fresh){
        $run=$sample.RunPath
        $archive=Join-Path $run 'first-generation'
        New-Item -ItemType Directory -Path $archive | Out-Null
        Copy-Item -LiteralPath (Join-Path $run 'EarthMap\selftest-report.txt'),(Join-Path $run 'Logs\server-main.log') -Destination $archive
        $name=Split-Path -Leaf $run
        $started=[datetime]::UtcNow
        $launch=& "$PSScriptRoot\run-mountain-playtest.ps1" -Case $sample.Case -Height $sample.Height -RunName $name -CylinderZip $CylinderZip -GamePath $GamePath -BuiltModPath (Join-Path $performanceFolder 'build') -Resume -Continents | ConvertFrom-Json
        $process=Get-Process -Id $launch.ProcessId -ErrorAction Stop
        $finished=$process.WaitForExit(1200000)
        if(!$finished){$process.Kill();$process.WaitForExit()}
        $report=Get-Content -LiteralPath (Join-Path $run 'EarthMap\selftest-report.txt') -Raw
        $log=Get-Content -LiteralPath (Join-Path $run 'Logs\server-main.log') -Raw
        $errors=@($log -split "`n" | Where-Object {$_ -match '\[Error\]|\[Fatal\]|Exception|timed out'})
        $summaries=@($report -split "`n" | Where-Object {$_ -match '^world |^terrain profile|^vertical |columns in .*top blocks:'})
        $sites=@($summaries | Where-Object {$_ -match 'columns in .*top blocks:'}).Count
        $pass=$finished -and $errors.Count -eq 0 -and !$report.Contains('FAIL') -and $report.Contains('vertical sea agreement: PASS') -and $sites -eq $sample.ExpectedSites
        $reloads+=@{Case=$sample.Case;Height=$sample.Height;Repeat=$sample.Repeat;RunPath=$run;AssemblySHA256=$sample.AssemblySHA256;ElapsedSeconds=[math]::Round(([datetime]::UtcNow-$started).TotalSeconds,2);Pass=$pass;ReportedSites=$sites;Errors=$errors;SiteSummaries=$summaries}
        $reloads | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $folder 'reload-results.json') -Encoding utf8
        Write-Output ("Reload {0} h{1} r{2}: {3}" -f $sample.Case,$sample.Height,$sample.Repeat,$pass)
    }
    Progress 'Source terrain without detail' 'running'
    & "$PSScriptRoot\run-mountain-matrix.ps1" -CylinderZip $CylinderZip -GamePath $GamePath -Cases H -Detail off -BatchName "$BatchPrefix-detail-off" -Port 42742
    Progress 'Floral Zones and Wildcraft providers' 'running'
    $installed=Join-Path $env:APPDATA 'VintagestoryData\Mods'
    $providers=@(Get-ChildItem -LiteralPath $installed -Filter '*.zip' | Where-Object {$_.Name -match '^floralzones|^(herbarium|wildcrafttree)-'} | Select-Object -ExpandProperty FullName)
    if($providers.Count -gt 0){
        & "$PSScriptRoot\run-mountain-matrix.ps1" -CylinderZip $CylinderZip -GamePath $GamePath -Cases @('C','K') -ExtraMods $providers -BatchName "$BatchPrefix-providers" -Port 42743
    }
    Progress 'Installed mod set in isolated test worlds' 'running'
    $installedCylinder=@(Get-ChildItem -LiteralPath $installed -Filter 'cylinderworld*.zip' | Sort-Object Name)
    if($installedCylinder.Count -ne 1){throw 'Expected exactly one installed Cylinder World package for the integration test.'}
    $otherMods=@(Get-ChildItem -LiteralPath $installed -Filter '*.zip' | Where-Object {$_.Name -notmatch '^(earthmap|cylinderworld)-'} | Select-Object -ExpandProperty FullName)
    & "$PSScriptRoot\run-mountain-matrix.ps1" -CylinderZip $installedCylinder[0].FullName -GamePath $GamePath -Cases K -ExtraMods $otherMods -BatchName "$BatchPrefix-installed-mods" -Port 42744
    Progress 'All follow-up batches' 'completed'
}catch{
    @{Stage='Follow-up pipeline';State='failed';Error=$_.Exception.Message;UpdatedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'progress.json') -Encoding utf8
    throw
}
