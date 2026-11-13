param(
    [Parameter(Mandatory=$true)][string[]]$RunPaths,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$rows = foreach ($path in $RunPaths) {
    $run = (Resolve-Path -LiteralPath $path).Path
    $log = Get-Content -LiteralPath "$run\stdout.log" -Raw
    $reportFile = "$run\EarthMap\selftest-report.txt"
    $report = if (Test-Path -LiteralPath $reportFile) { Get-Content -LiteralPath $reportFile -Raw } else { '' }
    $assets = @{}
    foreach ($file in @('geology-rules-v1.json','geology-grids-v1.json','fauna-species.json','ore-heatmaps-v1.json')) {
        $assetPath = "$run\Mods\earthmap\assets\earth\config\$file"
        if (Test-Path -LiteralPath $assetPath) { $assets[$file] = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash }
    }
    $observations = [regex]::Matches($report,'(?m)^(.+) loaded animals/entities: (\d+); controlled fauna=(\d+), (?:outside current range|geographic violations)=(\d+);')
    $controlled = 0; $outside = 0
    foreach ($match in $observations) { $controlled += [int]$match.Groups[3].Value; $outside += [int]$match.Groups[4].Value }
    [pscustomobject][ordered]@{
        runPath = $run
        earthVersion = (Get-Content -LiteralPath "$run\Mods\earthmap\modinfo.json" -Raw | ConvertFrom-Json).version
        earthDllSha256 = (Get-FileHash -LiteralPath "$run\Mods\earthmap\EarthMap.dll" -Algorithm SHA256).Hash
        assetSha256 = $assets
        completed = $log.Contains('SELFTEST done, stopping the server')
        stoppedGracefully = $log.Contains('Stopped the server!')
        regionalSettings = [regex]::Match($report,'(?m)^regional rules: .+$').Value.Trim()
        oreSettings = [regex]::Match($report,'(?m)^ore rules: .+$').Value.Trim()
        bogPeatAdapter = [regex]::Match($report,'(?m)^bog peat adapter: .+$').Value.Trim()
        prospectingPassSites = [regex]::Matches($report,'ore prospecting adapters: PASS').Count
        maximumBogPeatPlacementEvents = @([regex]::Matches($report,'bogpeat=(\d+)') | ForEach-Object {[long]$_.Groups[1].Value} | Measure-Object -Maximum).Maximum
        siteCount = [regex]::Matches($report,'(?m)^.+ at .+: \d+ columns in ').Count
        analysisFailures = [regex]::Matches($log,'SELFTEST .*analysis failed:').Count
        timedOutSites = [regex]::Matches($log,'SELFTEST .*timed out with').Count
        worldgenErrors = [regex]::Matches($log,'An error was thrown in pass ').Count
        serverErrors = [regex]::Matches($log,'\[Server Error\]').Count
        criticalErrors = [regex]::Matches($log,'Critical error occurred').Count
        saltMapSamples = @([regex]::Matches($report,'(?m)^(.+) halite prospecting map: (\d+)/(\d+) positive cells, maximum native value (\d+)') | ForEach-Object {
            [pscustomobject]@{place=$_.Groups[1].Value;positiveCells=[int]$_.Groups[2].Value;totalCells=[int]$_.Groups[3].Value;maximumNativeValue=[int]$_.Groups[4].Value}
        })
        controlledFaunaObserved = $controlled
        controlledOutsideCurrentRange = $outside
        observationCaveat = 'Counts are local snapshots, not population estimates. Animals can move beyond their spawn range.'
    }
}
[ordered]@{created=(Get-Date -Format o);runs=@($rows)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
@($rows) | Select-Object runPath, completed, siteCount, worldgenErrors, serverErrors, criticalErrors, controlledFaunaObserved, controlledOutsideCurrentRange | Format-Table -AutoSize
