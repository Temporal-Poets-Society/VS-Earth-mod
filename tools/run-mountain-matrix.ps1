[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CylinderZip,
    [string]$GamePath=(Join-Path $env:APPDATA 'Vintagestory'),
    [ValidateSet(256,384,512,768)][int[]]$Heights=@(384,512),
    [ValidateSet('A','B','C','D','E','F','G','H','I','J','K')][string[]]$Cases=@('A','B','C','D','E','F','G','H','I','J','K'),
    [ValidateRange(1,10)][int]$Repeats=1,
    [ValidateSet('normal','half','off')][string]$Detail='half',
    [string[]]$ExtraMods=@(),
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$BatchName=('mountain-matrix-'+(Get-Date -Format 'yyyyMMdd-HHmmss')),
    [int]$Port=42740,
    [switch]$Smoke
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$batch=Join-Path (Join-Path $root 'testruns') $BatchName
if(Test-Path -LiteralPath $batch){throw 'Batch exists; use a new BatchName so prior evidence remains intact.'}
New-Item -ItemType Directory -Path $batch | Out-Null
$results=@()
$buildSource=Join-Path $root 'bin\Release\mod'
$snapshot=Join-Path $batch 'build'
New-Item -ItemType Directory -Path $snapshot | Out-Null
Get-ChildItem -LiteralPath $buildSource | Copy-Item -Destination $snapshot -Recurse
$assembly=Join-Path $snapshot 'EarthMap.dll'
$buildHash=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$packageFolder=Join-Path $batch 'packages'
New-Item -ItemType Directory -Path $packageFolder | Out-Null
$snapshots=@()
$packageFacts=@()
foreach($package in @($CylinderZip)+$ExtraMods){
    $source=Get-Item -LiteralPath $package
    $destination=Join-Path $packageFolder $source.Name
    Copy-Item -LiteralPath $source.FullName -Destination $destination
    $packageFacts+=@{File=$source.Name;SHA256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash}
    $snapshots+=$destination
}
$CylinderZip=$snapshots[0]
$extraSnapshots=@($snapshots | Select-Object -Skip 1)
$expectedSites=@(Get-Content -LiteralPath (Join-Path $root 'tests\fixtures\mountain-sites.json') -Raw | ConvertFrom-Json).Count
@{AssemblySHA256=$buildHash;Packages=$packageFacts;Detail=$Detail;ExpectedSites=$expectedSites} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $batch 'inputs.json') -Encoding utf8
foreach($repeat in 1..$Repeats){
    $orderedCases=if($repeat % 2 -eq 0){@($Cases[($Cases.Length-1)..0])}else{$Cases}
    foreach($height in $Heights){foreach($case in $orderedCases){
        $name="$BatchName-$case-h$height-r$repeat"
        $started=[datetime]::UtcNow
        $launch=& "$PSScriptRoot\run-mountain-playtest.ps1" -Case $case -Height $height -RunName $name -CylinderZip $CylinderZip -GamePath $GamePath -BuiltModPath (Split-Path -Parent $assembly) -ExtraMods $extraSnapshots -Detail $Detail -Port $Port -Continents -Smoke:$Smoke | ConvertFrom-Json
        $process=Get-Process -Id $launch.ProcessId -ErrorAction Stop
        $peakWorkingSet=0L;$cpuSeconds=0.0
        $deadline=[datetime]::UtcNow.AddMinutes(20)
        while(!$process.WaitForExit(500) -and [datetime]::UtcNow -lt $deadline){
            try{
                $process.Refresh()
                $peakWorkingSet=[math]::Max($peakWorkingSet,$process.PeakWorkingSet64)
                $cpuSeconds=[math]::Max($cpuSeconds,$process.TotalProcessorTime.TotalSeconds)
            }catch [System.InvalidOperationException] { }
        }
        $finished=$process.HasExited
        if(!$finished){$process.Kill();$process.WaitForExit()}
        $run=$launch.RunPath
        $reportPath=Join-Path $run 'EarthMap\selftest-report.txt'
        $report=if(Test-Path -LiteralPath $reportPath){Get-Content -LiteralPath $reportPath -Raw}else{''}
        $log=Get-Content -LiteralPath (Join-Path $run 'Logs\server-main.log') -Raw
        $errorLines=@($log -split "`n" | Where-Object {$_ -match '\[Error\]|\[Fatal\]|Exception|timed out'})
        $summaries=@($report -split "`n" | Where-Object {$_ -match '^world |^terrain profile|^vertical |columns in .*top blocks:'})
        $siteCount=@($summaries | Where-Object {$_ -match 'columns in .*top blocks:'}).Count
        $bytes=(Get-ChildItem -LiteralPath (Join-Path $run 'Saves') -File | Measure-Object -Property Length -Sum).Sum
        $ok=$finished -and $report.Contains('vertical sea agreement: PASS') -and !$report.Contains('FAIL') -and $errorLines.Count -eq 0 -and $siteCount -eq $expectedSites
        $results+= [pscustomobject]@{Case=$case;Height=$height;Repeat=$repeat;Detail=$Detail;RunPath=$run;AssemblySHA256=$buildHash;StartedUTC=$started.ToString('o');ElapsedSeconds=[math]::Round(([datetime]::UtcNow-$started).TotalSeconds,2);SaveBytes=$bytes;PeakWorkingSetBytes=$peakWorkingSet;ObservedCpuSeconds=$cpuSeconds;ExpectedSites=$expectedSites;ReportedSites=$siteCount;Pass=$ok;Errors=$errorLines;SiteSummaries=$summaries}
        $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $batch 'results.json') -Encoding utf8
        [pscustomobject]@{Completed=$results.Count;Total=$Repeats*$Heights.Count*$Cases.Count;LastCase=$case;LastHeight=$height;Pass=$ok;RunPath=$run} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $batch 'progress.json') -Encoding utf8
        Write-Output ("{0} h{1} repeat {2}: {3}" -f $case,$height,$repeat,$ok)
    }}
}
Write-Output "Finished: $batch"
