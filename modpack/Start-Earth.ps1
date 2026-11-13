param([string]$GamePath = "$env:APPDATA\Vintagestory", [string]$ProfilePath = (Join-Path $PSScriptRoot 'EarthProfile'))
$ErrorActionPreference = 'Stop'
$ProfilePath = [IO.Path]::GetFullPath($ProfilePath)
if (!(Test-Path -LiteralPath (Join-Path $ProfilePath '.earth-pack.json'))) { throw 'Run Setup-Earth.cmd first.' }
$marker = Get-Content -LiteralPath (Join-Path $ProfilePath '.earth-pack.json') -Raw | ConvertFrom-Json
if (!$marker.complete) { throw 'Installation is incomplete. Run Setup-Earth.cmd again.' }
$exe = Join-Path $GamePath 'Vintagestory.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Game not found. Run Start-Earth.ps1 with -GamePath pointing to your Vintage Story installation.' }
# Player-invoked launcher: opening the game is the purpose of this script.
Start-Process -FilePath $exe -WorkingDirectory $GamePath -ArgumentList @('--dataPath', ('"'+$ProfilePath+'"'))
