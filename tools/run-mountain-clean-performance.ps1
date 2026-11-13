[CmdletBinding()]
param([Parameter(Mandatory)][string]$CylinderZip)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$waitingFor=Join-Path $root 'testruns\mountain-followup\progress.json'
$statusPath=Join-Path $root 'testruns\clean-performance-status.json'
@{State='waiting';Reason='Let activation, provider and installed-mod tests finish before a clean sequential timing series.'} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding utf8
$deadline=[datetime]::Parse('2026-10-27T06:00:00Z').ToUniversalTime()
while([datetime]::UtcNow -lt $deadline){
    $status=$null
    try{$status=Get-Content -LiteralPath $waitingFor -Raw | ConvertFrom-Json}catch [System.ArgumentException] { }
    if($status.State -eq 'failed'){throw 'Follow-up pipeline failed; inspect it before running more timing tests.'}
    if($status.State -eq 'completed'){break}
    Start-Sleep -Seconds 5
}
if([datetime]::UtcNow -ge $deadline){throw 'Morning cutoff reached before the clean timing series could start.'}
@{State='running';StartedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding utf8
& "$PSScriptRoot\run-mountain-matrix.ps1" -CylinderZip $CylinderZip -Cases @('A','B') -Repeats 3 -Detail normal -BatchName 'mountain-clean-performance' -Port 42748
@{State='completed';FinishedUTC=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding utf8
