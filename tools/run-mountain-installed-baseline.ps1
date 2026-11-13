[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path $root 'testruns\mountain-installed-baseline'
if(Test-Path -LiteralPath $folder){throw 'Baseline evidence already exists; do not overwrite it.'}
New-Item -ItemType Directory -Path $folder | Out-Null
@{State='waiting';Reason='Preserve clean performance conditions.'} | ConvertTo-Json | Set-Content -LiteralPath "$folder\progress.json" -Encoding utf8
$deadline=[datetime]::Parse('2026-10-27T06:00:00Z').ToUniversalTime()
while([datetime]::UtcNow -lt $deadline){
    $state=$null
    try{$state=Get-Content -LiteralPath (Join-Path $root 'testruns\clean-performance-status.json') -Raw | ConvertFrom-Json}catch{ }
    if($state.State -eq 'completed'){break}
    Start-Sleep -Seconds 5
}
if([datetime]::UtcNow -ge $deadline){throw 'Morning cutoff reached before the baseline could start.'}
@{State='running';StartedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$folder\progress.json" -Encoding utf8
$installed=Join-Path $env:APPDATA 'VintagestoryData\Mods'
$original=@(Get-ChildItem -LiteralPath $installed -Filter 'earthmap*.zip')
if($original.Count -ne 1){throw 'Expected exactly one installed Earth package for the original-build control.'}
$archive=Join-Path $folder $original[0].Name
Copy-Item -LiteralPath $original[0].FullName -Destination $archive
$build=Join-Path $folder 'original-build'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archive,$build)
$packages=Join-Path $root 'testruns\mountain-followup-installed-mods\packages'
$cylinder=@(Get-ChildItem -LiteralPath $packages -Filter 'cylinderworld*.zip')
if($cylinder.Count -ne 1){throw 'Expected exactly one frozen Cylinder World package.'}
$others=@(Get-ChildItem -LiteralPath $packages -Filter '*.zip' | Where-Object {$_.Name -notmatch '^cylinderworld-'} | Select-Object -ExpandProperty FullName)
$launch=& "$PSScriptRoot\run-mountain-playtest.ps1" -Case A -Height 384 -RunName 'mountain-installed-baseline-world' -CylinderZip $cylinder[0].FullName -ExtraMods $others -BuiltModPath $build -Continents -Port 42749 | ConvertFrom-Json
$process=Get-Process -Id $launch.ProcessId
$finished=$process.WaitForExit(1200000)
if(!$finished){$process.Kill();$process.WaitForExit()}
$log=Get-Content -LiteralPath (Join-Path $launch.RunPath 'Logs\server-main.log') -Raw
$reportPath=Join-Path $launch.RunPath 'EarthMap\selftest-report.txt'
$report=if(Test-Path -LiteralPath $reportPath){Get-Content -LiteralPath $reportPath -Raw}else{''}
$errors=@($log -split "`n" | Where-Object {$_ -match '\[Error\]|\[Fatal\]|Exception|timed out'})
$packaging=@($errors | Where-Object {$_ -match 'Failed to find language folder: .*hydrology-0\.1\.11\.zip_.*assets\\game\\lang'})
$other=@($errors | Where-Object {$_ -notmatch 'Failed to find language folder: .*hydrology-0\.1\.11\.zip_.*assets\\game\\lang'})
$sites=@($report -split "`n" | Where-Object {$_ -match 'columns in .*top blocks:'})
@{State='completed';OriginalEarthPackage=$original[0].Name;OriginalEarthSHA256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;RunPath=$launch.RunPath;ProcessCompleted=$finished;ReportedSites=$sites.Count;HydrologyPackagingErrorReproduced=($packaging.Count -gt 0);PackagingErrors=$packaging;OtherErrors=$other;FinishedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$folder\results.json" -Encoding utf8
@{State='completed';FinishedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$folder\progress.json" -Encoding utf8
